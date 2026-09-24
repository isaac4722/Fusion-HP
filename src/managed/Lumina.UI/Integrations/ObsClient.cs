// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ObsClient.cs : cliente obs-websocket 5.x (requisito spec §3.3 «integración
//  con OBS Studio: envío de texto y versículos en tiempo real mediante
//  WebSocket» + acción de activadores «cambiar escena»).
//
//  NET48 EXCLUSIVO (ClientWebSocket no existe en net35): todo el archivo queda
//  guardado con #if — la variante net35 de la UI compila SIN esta clase y el
//  botón de OBS avisa «requiere .NET Framework 4.8».
//
//  Patrón de hilos (sin async/await en la capa gestionada):
//    * Hilo de recepción dedicado con ReceiveAsync().Wait() — bloqueo sano
//      (hilo propio, sin SynchronizationContext).
//    * Envíos con lock + SendAsync().Wait() (mensajes pequeños, pocas ops/s).
//    * Reconnect manual (botón) + auto-reintento en Send si la conexión murió.
// ============================================================================
#if !LUMINA_NET35
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using lumina.core;

namespace lumina.ui
{
    public sealed class ObsClient : IDisposable
    {
        private readonly object _sync = new object();
        private ClientWebSocket _ws;
        private Thread _rxThread;
        private volatile bool _running;
        private volatile bool _identified;
        private string _url = "ws://127.0.0.1:4455";
        private string _password = string.Empty;
        private long _reqCounter;
        private DateTime _lastActivityUtc;

        /// <summary>Estado legible para la UI (hilo seguro).</summary>
        public string LastError = string.Empty;

        /// <summary>Conectado Y autenticado (Identified recibido).</summary>
        public bool IsReady { get { return _identified; } }

        /// <summary>Evento crudo del servidor OBS (hilo de recepción).</summary>
        public event Action<string, string> EventReceived;   // (eventType, eventDataJson|null)

        /// <summary>Respuestas de requests (hilo de recepción).</summary>
        public event Action<ObsProtocol.ObsResponse> ResponseReceived;

        public string Url { get { return _url; } }
        public DateTime LastActivityUtc { get { return _lastActivityUtc; } }

        // ---------------------------------------------------------------- ciclo

        /// <summary>Conecta y hace el handshake V5 (Hello → Identify → Identified).</summary>
        public bool Connect(string url, string password, int timeoutMs)
        {
            Disconnect();
            lock (_sync)
            {
                _url = string.IsNullOrEmpty(url) ? "ws://127.0.0.1:4455" : url.Trim();
                _password = password ?? string.Empty;
                _reqCounter = 0;
                LastError = string.Empty;
            }
            try
            {
                ClientWebSocket ws = new ClientWebSocket();
                using (CancellationTokenSource cts = new CancellationTokenSource(timeoutMs))
                {
                    ws.ConnectAsync(new Uri(_url), cts.Token).Wait(timeoutMs + 1000);
                }
                lock (_sync) { _ws = ws; }
                _running = true;
                _rxThread = new Thread(ReceiveLoop);
                _rxThread.IsBackground = true;
                _rxThread.Name = "Lumina.ObsClient.Rx";
                _rxThread.Start();

                // El Hello llega inmediato: esperar Identified con tope
                int waited = 0;
                while (!_identified && waited < timeoutMs && _running)
                {
                    Thread.Sleep(50);
                    waited += 50;
                }
                if (!_identified)
                {
                    LastError = _running
                        ? "OBS no completó el handshake (versión obs-websocket antigua o sin contraseña correcta)."
                        : "No se pudo conectar con OBS (" + _url + "). ¿está abierto con el servidor WebSocket activo?";
                    Disconnect();
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LastError = "No se pudo conectar con OBS (" + _url + ") — " + ex.Message;
                Disconnect();
                return false;
            }
        }

        public void Disconnect()
        {
            _running = false;
            _identified = false;
            ClientWebSocket ws;
            lock (_sync)
            {
                ws = _ws;
                _ws = null;
            }
            Thread t = _rxThread;
            if (t != null && t.IsAlive)
            {
                try { t.Join(1500); } catch (Exception) { }
            }
            _rxThread = null;
            if (ws != null)
            {
                try
                {
                    if (ws.State == WebSocketState.Open)
                        ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye",
                            CancellationToken.None).Wait(1000);
                }
                catch (Exception) { }
                try { ws.Dispose(); } catch (Exception) { }
            }
        }

        public void Dispose() { Disconnect(); }

        // ---------------------------------------------------------------- recepción

        private void ReceiveLoop()
        {
            ClientWebSocket ws;
            lock (_sync) { ws = _ws; }
            byte[] buf = new byte[256 * 1024];
            while (_running && ws != null)
            {
                WebSocketReceiveResult res;
                try
                {
                    // Tope de 120 s: OBS sin eventos queda vivo; si el socket
                    // murió en silencio, el token corta la espera y salimos.
                    using (CancellationTokenSource cts = new CancellationTokenSource(120000))
                    {
                        res = ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token)
                               .ConfigureAwait(false)
                               .GetAwaiter().GetResult();
                    }
                }
                catch (OperationCanceledException)
                {
                    lock (_sync) { ws = _ws; }
                    if (ws != null && ws.State == WebSocketState.Open) continue;  // solo inactividad
                    _running = false;
                    _identified = false;
                    LastError = "OBS sin actividad (timeout de recepción).";
                    return;
                }
                catch (Exception ex)
                {
                    _running = false;
                    _identified = false;
                    LastError = "Conexión OBS perdida — " + ex.Message;
                    return;
                }

                try
                {
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        _running = false;
                        _identified = false;
                        LastError = "OBS cerró la conexión.";
                        return;
                    }
                    using (MemoryStream acc = new MemoryStream())
                    {
                        acc.Write(buf, 0, res.Count);
                        while (!res.EndOfMessage)
                        {
                            using (CancellationTokenSource cts = new CancellationTokenSource(30000))
                            {
                                res = ws.ReceiveAsync(new ArraySegment<byte>(buf), cts.Token)
                                       .ConfigureAwait(false)
                                       .GetAwaiter().GetResult();
                            }
                            acc.Write(buf, 0, res.Count);
                        }
                        string msg = new UTF8Encoding(false).GetString(acc.ToArray());
                        HandleMessage(msg);
                    }
                }
                catch (Exception)
                {
                    // Mensaje parcial/truncado: ignorar y seguir.
                }
                lock (_sync) { ws = _ws; }
            }
        }

        private void HandleMessage(string json)
        {
            _lastActivityUtc = DateTime.UtcNow;
            try
            {
                if (ObsProtocol.IsHello(json))
                {
                    SendRaw(ObsProtocol.BuildIdentify(json, _password, 1,
                        ObsProtocol.EventSubscriptionsBasic));
                    return;
                }
                if (ObsProtocol.IsIdentified(json))
                {
                    _identified = true;
                    return;
                }
                string eventType;
                Dictionary<string, object> eventData;
                if (ObsProtocol.ParseEvent(json, out eventType, out eventData))
                {
                    Action<string, string> h = EventReceived;
                    if (h != null)
                    {
                        h(eventType, eventData != null ? MiniJson.Serialize(eventData) : null);
                    }
                    return;
                }
                ObsProtocol.ObsResponse r = ObsProtocol.ParseResponse(json);
                if (r != null)
                {
                    Action<ObsProtocol.ObsResponse> h = ResponseReceived;
                    if (h != null) h(r);
                }
            }
            catch (Exception)
            {
                // JSON inesperado: ignorar (protocolo tolerante).
            }
        }

        // ---------------------------------------------------------------- envío

        private bool SendRaw(string json)
        {
            ClientWebSocket ws;
            lock (_sync) { ws = _ws; }
            if (ws == null || ws.State != WebSocketState.Open) return false;
            try
            {
                lock (_sync)
                {
                    byte[] b = new UTF8Encoding(false).GetBytes(json);
                    ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Text, true,
                        CancellationToken.None).Wait(5000);
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string NextRequestId()
        {
            long n = Interlocked.Increment(ref _reqCounter);
            return "lumina-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Cambia la escena programada de OBS.</summary>
        public bool SetScene(string sceneName)
        {
            if (!_identified) return false;
            return SendRaw(ObsProtocol.BuildSetScene(NextRequestId(), sceneName ?? string.Empty));
        }

        /// <summary>Envía texto a una fuente de texto de OBS (títulos en vivo).</summary>
        public bool SetInputText(string sourceName, string text)
        {
            if (!_identified) return false;
            return SendRaw(ObsProtocol.BuildSetInputText(NextRequestId(), sourceName ?? string.Empty,
                                                          text ?? string.Empty));
        }
    }
}
#endif
