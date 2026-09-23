// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  JsLibHost.cs (v6.1.0 «GUION») : el objeto «jslib» visible desde JScript
//  (requisito §3.3 del spec — «JSLib-like mínimo»):
//
//      jslib.version()                       → '6.1.0'
//      jslib.log(msg)                        → registro (Integraciones › Módulos JS)
//      jslib.notify(titulo, texto)           → registro destacado + aviso de la UI
//      jslib.httpGet(url, cb)                → GET 15 s; cb(status, body)
//      jslib.tcp(id, host, puerto, onLine)   → socket TCP persistente por «id»
//      jslib.tcpSend(id, texto) / tcpClose(id)
//      jslib.ws(id, url, onMessage)          → WebSocket persistente por «id»
//      jslib.wsSend(id, texto) / wsClose(id)
//      jslib.cmd('next'|'prev'|'black'|'clear'|'show'[, indice]) → motor
//      jslib.showText(texto[, segundos])     → aviso en pantalla (lower third)
//      jslib.onEvent('slide_changed', cb)    → cb(payloadJson) — mismos eventos
//                                              que los activadores
//      jslib.setTimeout(ms, cb) → id;  jslib.clearTimeout(id)
//
//  Reglas de diseño (lecciones del ObsClient):
//    * SIN async/await (contrato de la capa gestionada): hilos de recepción
//      propios con bloqueo sano + ThreadPool para httpGet.
//    * El motor IActiveScript es APARTAMENTO-hilo: TODO callback JS se ejecuta
//      en el hilo de UI (Dispatcher.BeginInvoke desde los hilos de fondo).
//    * ComVisible AutoDispatch: resolución por nombre vía IDispatch (GetIDsOfNames),
//      SIN sobrecargas (no desambiguan) — los opcionales llegan como object.
//    * _dead: tras Shutdown() los hilos de fondo siguen viviendo un instante —
//      sus líneas tardías se descartan en vez de invocar callbacks de un motor
//      ya cerrado (huérfanos por diseño en cada recarga).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Threading;
using lumina.core;

namespace lumina.wpf.scripting
{
    /// <summary>Destino de las acciones del script (lo implementa la MainWindow).</summary>
    public interface IJsLibSink
    {
        /// <summary>cmd(action, index) → resultado legible (o null).</summary>
        string Command(string action, int index);

        /// <summary>showText(text, seconds) → aviso en pantalla.</summary>
        void ShowText(string text, int seconds);

        /// <summary>notify(title, text) → aviso destacado de la UI.</summary>
        void Notify(string title, string text);
    }

    /// <summary>
    /// Contrato COM del objeto «jslib» — interfaz DUAL con DispIds EXPLÍCITOS
    /// (lección del tercer run del tag: con ClassInterface AutoDispatch el CCW
    /// no publica TypeInfo y el JScript clásico envuelve el objeto como opaco
    /// — «jslib» existe pero «jslib.log» evalúa a no-función → «Function
    /// expected»). Con una interfaz dual ComVisible el CCW expone IDispatch
    /// CON TypeInfo real (generado de la definición): JScript resuelve los
    /// miembros por nombre (GetIDsOfNames) o por enlace temprano, y los
    /// parámetros object reciben VT_BSTR/VT_I4/VT_DISPATCH sin fricción.
    /// DispIds fijos y bajos (1..17) para estabilidad entre versiones.
    /// </summary>
    [ComVisible(true)]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IJsLibApi
    {
        [DispId(1)] string version();
        [DispId(2)] void log(object msg);
        [DispId(3)] void notify(object title, object text);
        [DispId(4)] void onEvent(string name, object callback);
        [DispId(5)] void httpGet(string url, object callback);
        [DispId(6)] bool tcp(string id, string host, int port, object onLine);
        [DispId(7)] bool tcpSend(string id, string text);
        [DispId(8)] void tcpClose(string id);
        [DispId(9)] bool tcpConnected(string id);
        [DispId(10)] bool ws(string id, string url, object onMessage);
        [DispId(11)] bool wsSend(string id, string text);
        [DispId(12)] void wsClose(string id);
        [DispId(13)] bool wsConnected(string id);
        [DispId(14)] int setTimeout(object ms, object callback);
        [DispId(15)] void clearTimeout(object id);
        [DispId(16)] string cmd(object action, [Optional][DefaultParameterValue(null)] object index);
        [DispId(17)] void showText(object text, [Optional][DefaultParameterValue(null)] object seconds);
    }

    /// <summary>
    /// El objeto «jslib» de JScript: implementa IJsLibApi (dual, con TypeInfo).
    /// Los métodos se llaman SIEMPRE desde el hilo de UI (el script corre allí).
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.None)]
    public sealed class JsLibHost : IJsLibApi
    {
        private readonly JsEngine _engine;      // registro/compartido con el motor
        private readonly IJsLibSink _sink;      // cmd/showText/notify → la app
        private readonly Dispatcher _ui;        // TODO callback JS corre aquí
        private readonly JsTimerIds _timerIds = new JsTimerIds();

        // Estado por conexión — mutado SOLO en el hilo de UI (los hilos de
        // recepción solo leen su propia conexión y despachan líneas a la UI).
        private readonly Dictionary<string, TcpConn> _tcp = new Dictionary<string, TcpConn>();
        private readonly Dictionary<string, WsConn> _ws = new Dictionary<string, WsConn>();
        private readonly Dictionary<int, Timer> _timers = new Dictionary<int, Timer>();

        private volatile bool _dead;            // Shutdown() ya corrió

        internal JsLibHost(JsEngine engine, IJsLibSink sink, Dispatcher ui)
        {
            _engine = engine;
            _sink = sink;
            _ui = ui;
        }

        /// <summary>Cierra sockets/timers y marca el host muerto (hilo de UI,
        /// llamado por JsEngine.Dispose/recargar — los callbacks huérfanos NO
        /// se invocan después de esto, decisión documentada del diseño).</summary>
        internal void Shutdown()
        {
            _dead = true;
            foreach (TcpConn c in _tcp.Values)
            {
                try { c.Closed = true; if (c.Client != null) c.Client.Close(); }
                catch (Exception) { }
            }
            _tcp.Clear();
            foreach (WsConn c in _ws.Values)
            {
                try { c.Closed = true; if (c.Socket != null) c.Socket.Abort(); }
                catch (Exception) { }
            }
            _ws.Clear();
            foreach (Timer t in _timers.Values)
            {
                try { t.Dispose(); } catch (Exception) { }
            }
            _timers.Clear();
        }

        // ------------------------------------------------------------ registro

        /// <summary>Versión del motor (visible desde script).</summary>
        public string version() { return JsPrelude.Version; }

        /// <summary>Registro visible en la página Integraciones (ring de 60).</summary>
        public void log(object msg)
        {
            _engine.LogLine(ToStringValue(msg));
        }

        /// <summary>Registro destacado + aviso de la aplicación.</summary>
        public void notify(object title, object text)
        {
            string t = ToStringValue(title);
            string x = ToStringValue(text);
            _engine.LogLine("★ " + (t.Length > 0 ? t + " — " : string.Empty) + x);
            if (_sink != null)
            {
                try { _sink.Notify(t, x); } catch (Exception) { }
            }
        }

        // ------------------------------------------------------------- eventos

        /// <summary>Suscribe cb al evento (payload JSON como string; parsear
        /// con jsonParse() del preámbulo). Mismos nombres que los activadores.</summary>
        public void onEvent(string name, object callback)
        {
            if (name == null || callback == null) return;
            _engine.SubscribeEvent(name, callback);
        }

        // ---------------------------------------------------------------- http

        /// <summary>GET con tope de 15 s. cb(status, body) — status 0 = fallo.</summary>
        public void httpGet(string url, object callback)
        {
            if (callback == null) return;
            string u = url == null ? string.Empty : url.Trim();
            if (u.Length == 0 ||
                (!u.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                 !u.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
            {
                DispatchToUi(delegate { InvokeCallback(callback, new object[] { 0, "(url inválida)" }); });
                return;
            }
            ThreadPool.QueueUserWorkItem(delegate
            {
                int status = 0;
                string body = string.Empty;
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(u);
                    req.Method = "GET";
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 15000;
                    req.UserAgent = "LuminaPresentation-JSLib/" + JsPrelude.Version;
                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    {
                        status = (int)resp.StatusCode;
                        using (StreamReader r = new StreamReader(
                            resp.GetResponseStream(), new UTF8Encoding(false)))
                        {
                            body = r.ReadToEnd();
                        }
                    }
                }
                catch (WebException wex)
                {
                    status = 0;
                    body = wex.Message;
                }
                catch (Exception ex)
                {
                    status = 0;
                    body = ex.Message;
                }
                int st = status;
                string bd = body;
                DispatchToUi(delegate { InvokeCallback(callback, new object[] { st, bd }); });
            });
        }

        // ----------------------------------------------------------------- tcp

        /// <summary>Socket TCP persistente identificado por «id» (lineas UTF-8
        /// delimitadas por \n, con \r\n tolerado). onLine(texto) por línea.</summary>
        public bool tcp(string id, string host, int port, object onLine)
        {
            if (id == null || id.Trim().Length == 0) return false;
            id = id.Trim();
            if (_dead) return false;
            TcpClose(id);   // reconnect sobre el mismo id
            TcpConn c = new TcpConn();
            c.OnLine = onLine;
            _tcp[id] = c;
            string h = host;
            int p = port;
            Thread t = new Thread((ThreadStart)delegate { TcpConnectAndLoop(id, c, h, p); });
            t.IsBackground = true;
            t.Name = "Lumina.JsLib.tcp." + id;
            t.Start();
            return true;
        }

        /// <summary>Envía texto (UTF-8, sin línea añadida — el protocolo lo
        /// define el dispositivo: mezcladores esperan su propio delimitador).</summary>
        public bool tcpSend(string id, string text)
        {
            if (id == null) return false;
            TcpConn c;
            if (!_tcp.TryGetValue(id.Trim(), out c) || c.Client == null || !c.Client.Connected)
            {
                _engine.LogLine("tcpSend: sin conexión «" + id + "»");
                return false;
            }
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text == null ? string.Empty : text);
                lock (c.SendSync)
                {
                    c.Stream.Write(bytes, 0, bytes.Length);
                    c.Stream.Flush();
                }
                return true;
            }
            catch (Exception ex)
            {
                _engine.LogLine("tcpSend «" + id + "» falló: " + ex.Message);
                return false;
            }
        }

        /// <summary>Cierra la conexión «id» (inexistente → no-op).</summary>
        public void tcpClose(string id)
        {
            if (id == null) return;
            TcpClose(id.Trim());
        }

        /// <summary>¿La conexión «id» está viva? (diagnóstico desde script).</summary>
        public bool tcpConnected(string id)
        {
            if (id == null) return false;
            TcpConn c;
            return _tcp.TryGetValue(id.Trim(), out c) && c.Client != null && c.Client.Connected;
        }

        // ------------------------------------------------------------------ ws

        /// <summary>WebSocket persistente identificado por «id» (frames de TEXTO;
        /// binarios ignorados con aviso). onMessage(texto).</summary>
        public bool ws(string id, string url, object onMessage)
        {
            if (id == null || id.Trim().Length == 0) return false;
            id = id.Trim();
            if (_dead) return false;
            WsClose(id);
            WsConn c = new WsConn();
            c.OnMessage = onMessage;
            _ws[id] = c;
            string u = url;
            Thread t = new Thread((ThreadStart)delegate { WsConnectAndLoop(id, c, u); });
            t.IsBackground = true;
            t.Name = "Lumina.JsLib.ws." + id;
            t.Start();
            return true;
        }

        /// <summary>Envía un frame de TEXTO UTF-8.</summary>
        public bool wsSend(string id, string text)
        {
            if (id == null) return false;
            WsConn c;
            if (!_ws.TryGetValue(id.Trim(), out c) || c.Socket == null ||
                c.Socket.State != WebSocketState.Open)
            {
                _engine.LogLine("wsSend: sin conexión «" + id + "»");
                return false;
            }
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(text == null ? string.Empty : text);
                lock (c.SendSync)
                {
                    c.Socket.SendAsync(new ArraySegment<byte>(bytes),
                        WebSocketMessageType.Text, true, CancellationToken.None).Wait(5000);
                }
                return true;
            }
            catch (Exception ex)
            {
                _engine.LogLine("wsSend «" + id + "» falló: " + ex.Message);
                return false;
            }
        }

        /// <summary>Cierra el WebSocket «id» (inexistente → no-op).</summary>
        public void wsClose(string id)
        {
            if (id == null) return;
            WsClose(id.Trim());
        }

        /// <summary>¿El WebSocket «id» está abierto? (diagnóstico desde script).</summary>
        public bool wsConnected(string id)
        {
            if (id == null) return false;
            WsConn c;
            return _ws.TryGetValue(id.Trim(), out c) && c.Socket != null &&
                   c.Socket.State == WebSocketState.Open;
        }

        // --------------------------------------------------------------- timer

        /// <summary>Temporizador de UN disparo (ms). Devuelve id &gt; 0.</summary>
        public int setTimeout(object ms, object callback)
        {
            if (callback == null) return 0;
            if (_dead) return 0;
            int delay = ToInt(ms, 0);
            if (delay < 0) delay = 0;
            int id = _timerIds.Alloc();
            object cb = callback;
            Timer t = new Timer(delegate
            {
                // Hilo del ThreadPool: SOLO despacha a la UI (el motor de scripts
                // es apartamento-hilo) y la limpieza de dicts ocurre allí.
                DispatchToUi(delegate
                {
                    Timer old;
                    if (_timers.TryGetValue(id, out old))
                    {
                        _timers.Remove(id);
                        try { old.Dispose(); } catch (Exception) { }
                        _timerIds.Release(id);
                    }
                    InvokeCallback(cb, null);
                });
            }, null, delay, Timeout.Infinite);
            _timers[id] = t;
            return id;
        }

        /// <summary>Cancela un setTimeout pendiente (id inválido → no-op).</summary>
        public void clearTimeout(object id)
        {
            int tid = ToInt(id, 0);
            Timer t;
            if (tid > 0 && _timers.TryGetValue(tid, out t))
            {
                _timers.Remove(tid);
                try { t.Dispose(); } catch (Exception) { }
                _timerIds.Release(tid);
            }
        }

        // ------------------------------------------------------------- acciones

        /// <summary>Comando al motor: 'next'|'prev'|'black'|'clear'|'show' (con
        /// índice). Devuelve el resultado legible (string).</summary>
        public string cmd(object action, [Optional][DefaultParameterValue(null)] object index)
        {
            if (_sink == null) return "(sin destino)";
            string a = ToStringValue(action).Trim();
            if (a.Length == 0) return "(cmd vacío)";
            int idx = ToInt(index, -1);
            try
            {
                string r = _sink.Command(a, idx);
                return r == null ? "ok" : r;
            }
            catch (Exception ex)
            {
                return "cmd «" + a + "» falló: " + ex.Message;
            }
        }

        /// <summary>Aviso en pantalla (lower third) con duración opcional.</summary>
        public void showText(object text, [Optional][DefaultParameterValue(null)] object seconds)
        {
            if (_sink == null) return;
            string t = ToStringValue(text);
            if (t.Length == 0) return;
            int secs = ToInt(seconds, 6);
            if (secs <= 0) secs = 6;
            try { _sink.ShowText(t, secs); }
            catch (Exception ex) { _engine.LogLine("showText falló: " + ex.Message); }
        }

        // =========================================================== interno ==

        private sealed class TcpConn
        {
            internal TcpClient Client;
            internal NetworkStream Stream;
            internal object OnLine;
            internal readonly object SendSync = new object();
            internal volatile bool Closed;
        }

        private sealed class WsConn
        {
            internal ClientWebSocket Socket;
            internal object OnMessage;
            internal readonly object SendSync = new object();
            internal volatile bool Closed;
        }

        private void TcpConnectAndLoop(string id, TcpConn c, string host, int port)
        {
            try
            {
                TcpClient client = new TcpClient();
                IAsyncResult ar = client.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(5000) || !client.Connected)
                {
                    client.Close();
                    _engine.LogLine("tcp «" + id + "»: no conectó " + host + ":" + port);
                    RemoveTcp(id, c);
                    return;
                }
                client.EndConnect(ar);
                c.Client = client;
                c.Stream = client.GetStream();
                _engine.LogLine("tcp «" + id + "»: conectado a " + host + ":" + port);
                TcpRxLoop(id, c);
            }
            catch (Exception ex)
            {
                _engine.LogLine("tcp «" + id + "»: " + ex.Message);
                RemoveTcp(id, c);
            }
        }

        private void TcpRxLoop(string id, TcpConn c)
        {
            byte[] buf = new byte[8192];
            List<byte> line = new List<byte>(256);
            try
            {
                while (!c.Closed && !_dead)
                {
                    int n = c.Stream.Read(buf, 0, buf.Length);
                    if (n <= 0) break;   // cierre remoto
                    for (int i = 0; i < n; i++)
                    {
                        if (buf[i] == (byte)'\n')
                        {
                            int len = line.Count;
                            if (len > 0 && line[len - 1] == (byte)'\r') line.RemoveAt(len - 1);
                            string text = new UTF8Encoding(false).GetString(line.ToArray());
                            line.Clear();
                            object cb = c.OnLine;
                            DispatchToUi(delegate { InvokeCallback(cb, new object[] { text }); });
                        }
                        else
                        {
                            line.Add(buf[i]);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Cierre local/remoto, timeout o reset: el finally registra y limpia.
            }
            finally
            {
                _engine.LogLine("tcp «" + id + "»: conexión cerrada");
                RemoveTcp(id, c);
            }
        }

        private void RemoveTcp(string id, TcpConn c)
        {
            try
            {
                if (c.Client != null) c.Client.Close();
            }
            catch (Exception) { }
            c.Closed = true;
            // Mutación de dict en el hilo de UI (contrato del host).
            DispatchToUi(delegate
            {
                TcpConn cur;
                if (_tcp.TryGetValue(id, out cur) && cur == c) _tcp.Remove(id);
            });
        }

        private void WsConnectAndLoop(string id, WsConn c, string url)
        {
            try
            {
                ClientWebSocket ws = new ClientWebSocket();
                using (CancellationTokenSource cts = new CancellationTokenSource(5000))
                {
                    ws.ConnectAsync(new Uri(url), cts.Token).Wait(6000);
                }
                c.Socket = ws;
                _engine.LogLine("ws «" + id + "»: conectado a " + url);
                WsRxLoop(id, c);
            }
            catch (Exception ex)
            {
                _engine.LogLine("ws «" + id + "»: " + ex.Message);
                RemoveWs(id, c);
            }
        }

        private void WsRxLoop(string id, WsConn c)
        {
            byte[] buf = new byte[16384];
            try
            {
                while (!c.Closed && !_dead)
                {
                    using (MemoryStream msg = new MemoryStream())
                    {
                        WebSocketReceiveResult res;
                        do
                        {
                            res = c.Socket.ReceiveAsync(
                                new ArraySegment<byte>(buf), CancellationToken.None).Result;
                            if (res.MessageType == WebSocketMessageType.Close)
                            {
                                c.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure,
                                    "adiós", CancellationToken.None).Wait(1000);
                                return;
                            }
                            if (res.Count > 0) msg.Write(buf, 0, res.Count);
                        } while (!res.EndOfMessage);
                        if (res.MessageType == WebSocketMessageType.Text)
                        {
                            string text = new UTF8Encoding(false).GetString(msg.ToArray());
                            object cb = c.OnMessage;
                            DispatchToUi(delegate { InvokeCallback(cb, new object[] { text }); });
                        }
                        else
                        {
                            _engine.LogLine("ws «" + id + "»: frame BINARIO ignorado");
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Cierre/aborto/fallo de red: el finally registra y limpia.
            }
            finally
            {
                _engine.LogLine("ws «" + id + "»: conexión cerrada");
                RemoveWs(id, c);
            }
        }

        private void RemoveWs(string id, WsConn c)
        {
            try
            {
                if (c.Socket != null) c.Socket.Abort();
            }
            catch (Exception) { }
            c.Closed = true;
            DispatchToUi(delegate
            {
                WsConn cur;
                if (_ws.TryGetValue(id, out cur) && cur == c) _ws.Remove(id);
            });
        }

        private void TcpClose(string id)
        {
            TcpConn c;
            if (_tcp.TryGetValue(id, out c))
            {
                _tcp.Remove(id);
                c.Closed = true;
                try { if (c.Client != null) c.Client.Close(); }
                catch (Exception) { }
            }
        }

        private void WsClose(string id)
        {
            WsConn c;
            if (_ws.TryGetValue(id, out c))
            {
                _ws.Remove(id);
                c.Closed = true;
                try { if (c.Socket != null) c.Socket.Abort(); }
                catch (Exception) { }
            }
        }

        private void DispatchToUi(Action what)
        {
            if (_dead) return;
            try { _ui.BeginInvoke(what); }
            catch (Exception) { }   // dispatcher apagándose (cierre de la app)
        }

        /// <summary>Invoca una función JScript (IDispatch tardío: DISPID_VALUE
        /// es la invocación de una función JScript). Siempre en el hilo de UI.</summary>
        internal void InvokeCallback(object callback, object[] args)
        {
            if (_dead || callback == null) return;
            try
            {
                callback.GetType().InvokeMember(string.Empty,
                    BindingFlags.InvokeMethod, null, callback,
                    args == null ? new object[0] : args, null);
            }
            catch (Exception ex)
            {
                _engine.LogLine("error en callback JS: " + ex.Message);
            }
        }

        private static string ToStringValue(object v)
        {
            if (v == null) return string.Empty;
            if (v is string) return (string)v;
            try
            {
                return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return v.GetType().Name;
            }
        }

        private static int ToInt(object v, int def)
        {
            if (v == null) return def;
            try
            {
                if (v is int) return (int)v;
                if (v is double)
                {
                    double d = (double)v;
                    if (double.IsNaN(d) || double.IsInfinity(d)) return def;
                    return (int)Math.Round(d);
                }
                int i;
                if (int.TryParse(Convert.ToString(v,
                        System.Globalization.CultureInfo.InvariantCulture), out i)) return i;
                double dd;
                if (double.TryParse(Convert.ToString(v,
                        System.Globalization.CultureInfo.InvariantCulture), out dd) &&
                    !double.IsNaN(dd) && !double.IsInfinity(dd))
                {
                    return (int)Math.Round(dd);
                }
            }
            catch (Exception) { }
            return def;
        }
    }
}
