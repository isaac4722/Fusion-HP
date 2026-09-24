// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Api/ApiV1Server.cs : servicio API del Plan de Ultra Implementación
//  (F5.01/F5.02/F5.03 — Sección 8.1 del documento técnico).
//
//  F5.01:
//   * HttpListener, prefijo http://+:<puerto>/ (red local).
//   * APAGADO por defecto; activación desde configuración; puerto configurable.
//   * Token generado AUTOMÁTICAMENTE al activar; cabecera obligatoria
//     «Authorization: Bearer <token>»; 401 para token ausente/inválido.
//   * Registro de cada petición y de cada rechazo (log §10.1 SIN el token —
//     F5.03.12: el token no aparece jamás en logs).
//   * Límite configurable de clientes concurrentes; 503 cuando no pueda
//     atender; backoff del hilo afectado (el listener JAMÁS muere).
//
//  F5.02 — tabla OBLIGATORIA de endpoints:
//   GET  /api/v1/state      estado completo JSON
//   POST /api/v1/next       avanzar línea/Elemento según contexto
//   POST /api/v1/prev       retroceder línea/Elemento
//   POST /api/v1/goto       ir por ID o índice
//   GET  /api/v1/text       texto plano o JSON con ?format=
//   POST /api/v1/bible      proyectar cita estructurada
//   POST /api/v1/message    mostrar aviso/Lower Third
//
//  F5.03:
//   * /remote.html servido LOCALMENTE (cliente web móvil: sin Internet,
//     sin nube, sin cuentas); emparejamiento por IP+token; el QR se expone
//     en /api/v1/qr.png y la UI lo pinta en Configuración.
//   * next/prev/goto/biblia/mensajes/volumen desde el móvil.
//
//  net35-compatible (C# 7.3, sin async).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using lumina.bridge;
using lumina.core;

namespace lumina.api
{
    public sealed class ApiV1Server : IDisposable
    {
        private const int MaxBodyBytes = 1024 * 1024;      // 1 MB (basta para citas/avisos)

        private readonly LuminaEngine _engine;
        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;
        private readonly object _sync = new object();

        private string _token;                             // generado al activar
        private int _maxClients = 8;                       // límite configurable
        private int _activeClients;
        private readonly List<string> _accessLog = new List<string>(); // circular
        private const int AccessLogMax = 200;

        /// <summary>Proyectar cita bíblica (la UI/BD resuelve el texto).
        /// args: {"book","chapter","verseFrom","verseTo","version"}.</summary>
        public event Action<Dictionary<string, object>> OnBible;
        /// <summary>Mostrar aviso/Lower Third. args: {"text","durationMs"}.</summary>
        public event Action<Dictionary<string, object>> OnMessage;

        /// <summary>Proveedor del texto en vivo (línea activa) para /api/v1/text.</summary>
        public Func<string> LiveTextProvider;
        /// <summary>Proveedor del estado completo (por defecto lumina_state_json).</summary>
        public Func<string> StateProvider;
        /// <summary>Volumen del video (0..100) — control móvil F5.03.9.</summary>
        public Func<int> VolumeGetter;
        public Action<int> VolumeSetter;

        public int Port { get; private set; }
        public bool IsRunning { get { return _running; } }
        public string Token { get { lock (_sync) { return _token; } } }
        public int MaxClients { get { return _maxClients; } set { _maxClients = Math.Max(1, value); } }

        public ApiV1Server(LuminaEngine engine)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            _engine = engine;
            _token = GenerateToken();
        }

        /// <summary>Token nuevo (regeneración manual desde Configuración).</summary>
        public void RegenerateToken()
        {
            lock (_sync) { _token = GenerateToken(); }
        }

        private static string GenerateToken()
        {
            Random rnd = new Random();
            byte[] b = new byte[18];
            rnd.NextBytes(b);
            StringBuilder sb = new StringBuilder();
            foreach (byte x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        /// <summary>Arranca escuchando en la red local (F5.01.2: http://+:puerto/).</summary>
        public bool Start(int port)
        {
            if (port < 1024 || port > 65535) return false;
            Stop();
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://+:" + port + "/");
                _listener.Start();
            }
            catch (HttpListenerException)
            {
                _listener = null;
                return false;                    // sin elevación no se puede '+': se informa
            }
            Port = port;
            _running = true;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
            return true;
        }

        /// <summary>Variante localhost (sin '+' — no exige permiso de URL).</summary>
        public bool StartLocal(int port)
        {
            if (port < 1024 || port > 65535) return false;
            Stop();
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
                _listener.Start();
            }
            catch (HttpListenerException) { _listener = null; return false; }
            Port = port;
            _running = true;
            _acceptThread = new Thread(AcceptLoop);
            _acceptThread.IsBackground = true;
            _acceptThread.Start();
            return true;
        }

        public void Stop()
        {
            _running = false;
            if (_listener != null)
            {
                try { _listener.Stop(); } catch (ObjectDisposedException) { }
                try { _listener.Close(); } catch (ObjectDisposedException) { }
                _listener = null;
            }
            if (_acceptThread != null && _acceptThread.IsAlive)
                _acceptThread.Join(2000);
            _acceptThread = null;
        }

        /// <summary>URL de emparejamiento para el QR (IP local + token).</summary>
        public string PairingUrl()
        {
            string ip = LocalIp();
            return "http://" + ip + ":" + Port + "/remote.html#" + Token;
        }

        private static string LocalIp()
        {
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        return a.ToString();
            }
            catch (SocketException) { }
            return "127.0.0.1";
        }

        // ------------------------------------------------------------ núcleo --

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx = null;
                try { ctx = _listener.GetContext(); }
                catch (Exception) { break; }                    // listener detenido
                ThreadPool.QueueUserWorkItem(delegate { Serve(ctx); });
            }
        }

        private void Serve(object state)
        {
            HttpListenerContext ctx = (HttpListenerContext)state;
            // F5.01.11: límite de clientes concurrentes → 503 + no se atiende.
            lock (_sync)
            {
                if (_activeClients >= _maxClients)
                {
                    Write(ctx, 503, "application/json; charset=utf-8",
                        "{\"ok\":0,\"error\":\"servidor ocupado (limite de clientes)\",\"retryAfterMs\":2000}");
                    Log("503 limite de clientes — " + ctx.Request.RemoteEndPoint);
                    try { ctx.Response.Close(); } catch (ObjectDisposedException) { }
                    return;
                }
                _activeClients++;
            }
            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                // F11.2/§8.1: backoff del hilo afectado; el servicio sigue vivo.
                Thread.Sleep(200);
                Log("ERROR interno: " + ex.GetType().Name);
                try { Write(ctx, 500, "application/json; charset=utf-8",
                    "{\"ok\":0,\"error\":\"error interno\"}"); } catch (Exception) { }
            }
            finally
            {
                lock (_sync) { _activeClients--; }
                try { ctx.Response.Close(); } catch (Exception) { }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            string method = ctx.Request.HttpMethod;

            // F5.03: cliente móvil servido LOCALMENTE (sin Internet/nube/cuentas).
            if (path == "/remote.html" && method == "GET")
            {
                Write(ctx, 200, "text/html; charset=utf-8", MobileRemoteHtml);
                Log("GET /remote.html " + ctx.Request.RemoteEndPoint);
                return;
            }

            // F5.03.11: QR de emparejamiento (IP+token) visible en configuración.
            if (path == "/api/v1/qr.png" && method == "GET")
            {
                if (!CheckBearer(ctx)) { Reject401(ctx); return; }
                QrMatrix qr = QrCode.Encode(PairingUrl());
                byte[] png = QrCode.ToPng(qr, 8, 4);
                Write(ctx, 200, "image/png", png);
                return;
            }

            // F5.02: TODAS las rutas /api/v1 exigen Bearer (401 si no).
            if (path.StartsWith("/api/v1/", StringComparison.Ordinal))
            {
                if (!CheckBearer(ctx)) { Reject401(ctx); return; }
            }
            else if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                Write(ctx, 404, "application/json; charset=utf-8",
                    "{\"ok\":0,\"error\":\"ruta desconocida (la API es /api/v1/...)\"}");
                return;
            }
            else
            {
                Write(ctx, 404, "text/plain; charset=utf-8", "no encontrado");
                return;
            }

            if (path == "/api/v1/state" && method == "GET")
            {
                string st = StateProvider != null ? StateProvider() : _engine.StateJson();
                Write(ctx, 200, "application/json; charset=utf-8", st);
                Log("GET /api/v1/state");
            }
            else if (path == "/api/v1/next" && method == "POST")
            {
                int rc = _engine.Next();
                // el motor avanza LÍNEA primero cuando hay syncMark (F1.03)
                WriteOk(ctx, rc);
                Log("POST /api/v1/next");
            }
            else if (path == "/api/v1/prev" && method == "POST")
            {
                WriteOk(ctx, _engine.Prev());
                Log("POST /api/v1/prev");
            }
            else if (path == "/api/v1/goto" && method == "POST")
            {
                Dictionary<string, object> b = ReadJsonBody(ctx);
                int rc;
                if (b.ContainsKey("index")) rc = _engine.ShowSlide((int)MiniJson.GetInt(b, "index", 0));
                else
                {
                    // por ID (F5.02.4): la UI mapea id→index; sin catálogo, 400.
                    Write(ctx, 400, "application/json; charset=utf-8",
                        "{\"ok\":0,\"error\":\"se requiere 'index' (entero)\"}");
                    return;
                }
                WriteOk(ctx, rc);
                Log("POST /api/v1/goto");
            }
            else if (path == "/api/v1/text" && method == "GET")
            {
                string format = ctx.Request.QueryString["format"];
                if (format == "json")
                {
                    string txt = LiveTextProvider != null ? (LiveTextProvider() ?? "") : "";
                    Dictionary<string, object> o = new Dictionary<string, object>();
                    o["text"] = txt;
                    Write(ctx, 200, "application/json; charset=utf-8", MiniJson.Serialize(o));
                }
                else
                {
                    string txt = LiveTextProvider != null ? (LiveTextProvider() ?? "") : "";
                    Write(ctx, 200, "text/plain; charset=utf-8", txt);
                }
                Log("GET /api/v1/text?format=" + (format ?? "plain")); // sin token JAMÁS
            }
            else if (path == "/api/v1/bible" && method == "POST")
            {
                Dictionary<string, object> b = ReadJsonBody(ctx);
                if (b.Count == 0)
                {
                    Write(ctx, 400, "application/json; charset=utf-8",
                        "{\"ok\":0,\"error\":\"cuerpo requerido: {book,chapter,verseFrom,verseTo,version}\"}");
                    return;
                }
                if (OnBible != null) OnBible(b);
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["ok"] = true; r["queued"] = true;
                Write(ctx, 200, "application/json; charset=utf-8", MiniJson.Serialize(r));
                Log("POST /api/v1/bible (cita estructurada)");
            }
            else if (path == "/api/v1/message" && method == "POST")
            {
                Dictionary<string, object> b = ReadJsonBody(ctx);
                string text = MiniJson.GetString(b, "text", "");
                if (text.Length == 0)
                {
                    Write(ctx, 400, "application/json; charset=utf-8",
                        "{\"ok\":0,\"error\":\"'text' requerido\"}");
                    return;
                }
                if (OnMessage != null) OnMessage(b);
                // además: Lower Third nativo inmediato (F3.04)
                Dictionary<string, object> lt = new Dictionary<string, object>();
                lt["text"] = text;
                lt["position"] = MiniJson.GetString(b, "position", "bottom");
                lt["durationMs"] = MiniJson.GetInt(b, "durationMs", 6000);
                lt["show"] = true;
                _engine.LowerThird(MiniJson.Serialize(lt));
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["ok"] = true;
                Write(ctx, 200, "application/json; charset=utf-8", MiniJson.Serialize(r));
                Log("POST /api/v1/message");
            }
            else if (path == "/api/v1/volume" && method == "POST")
            {
                Dictionary<string, object> b = ReadJsonBody(ctx);
                int v = (int)MiniJson.GetInt(b, "volume", -1);
                if (v < 0 || v > 100)
                {
                    Write(ctx, 400, "application/json; charset=utf-8",
                        "{\"ok\":0,\"error\":\"'volume' 0..100\"}");
                    return;
                }
                if (VolumeSetter != null) VolumeSetter(v);
                Dictionary<string, object> r = new Dictionary<string, object>();
                r["ok"] = true;
                Write(ctx, 200, "application/json; charset=utf-8", MiniJson.Serialize(r));
                Log("POST /api/v1/volume");
            }
            else if (path == "/api/v1/qr.png" && method == "GET")
            {
                // ya manejado arriba (misma ruta): código muerto por claridad
            }
            else
            {
                Write(ctx, 404, "application/json; charset=utf-8",
                    "{\"ok\":0,\"error\":\"endpoint o metodo no soportado\"}");
                Log("404 " + method + " " + path);
            }
        }

        // ----------------------------------------------------------- helpers --

        private bool CheckBearer(HttpListenerContext ctx)
        {
            string auth = ctx.Request.Headers["Authorization"] ?? "";
            if (!auth.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
            string t = auth.Substring(7).Trim();
            lock (_sync) { return t == _token; }
        }

        private void Reject401(HttpListenerContext ctx)
        {
            // F5.01.10: cada rechazo QUEDA REGISTRADO (sin el token).
            Write(ctx, 401, "application/json; charset=utf-8",
                "{\"ok\":0,\"error\":\"token requerido (Authorization: Bearer)\"}");
            Log("401 " + ctx.Request.HttpMethod + " " + ctx.Request.Url.AbsolutePath +
                " desde " + ctx.Request.RemoteEndPoint);
        }

        private Dictionary<string, object> ReadJsonBody(HttpListenerContext ctx)
        {
            using (System.IO.Stream s = ctx.Request.InputStream)
            {
                byte[] buf = new byte[MaxBodyBytes];
                int n = 0;
                while (true)
                {
                    int k = s.Read(buf, n, buf.Length - n);
                    if (k <= 0) break;
                    n += k;
                    if (n >= buf.Length) break;              // límite: se trunca → JSON erróneo → 400
                }
                if (n == 0) return new Dictionary<string, object>();
                try
                {
                    string body = Encoding.UTF8.GetString(buf, 0, n);
                    Dictionary<string, object> o = MiniJson.ParseAny(body) as Dictionary<string, object>;
                    return o ?? new Dictionary<string, object>();
                }
                catch (Exception) { return new Dictionary<string, object>(); }
            }
        }

        private void WriteOk(HttpListenerContext ctx, int status)
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["ok"] = status == 0;
            r["status"] = (long)status;
            Write(ctx, 200, "application/json; charset=utf-8", MiniJson.Serialize(r));
        }

        private static void Write(HttpListenerContext ctx, int code, string contentType,
                                  string body)
        {
            Write(ctx, code, contentType, Encoding.UTF8.GetBytes(body));
        }

        private static void Write(HttpListenerContext ctx, int code, string contentType,
                                  byte[] body)
        {
            try
            {
                ctx.Response.StatusCode = code;
                ctx.Response.ContentType = contentType;
                ctx.Response.ContentLength64 = body.Length;
                ctx.Response.OutputStream.Write(body, 0, body.Length);
            }
            catch (Exception) { /* cliente desconectado: no es un error */ }
        }

        /// <summary>Bitácora de acceso (F5.01.10) — SIN tokens ni cuerpos.</summary>
        public List<string> AccessLogSnapshot()
        {
            lock (_sync) { return new List<string>(_accessLog); }
        }

        private void Log(string line)
        {
            lock (_sync)
            {
                _accessLog.Add(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                    System.Globalization.CultureInfo.InvariantCulture) + " " + line);
                if (_accessLog.Count > AccessLogMax)
                    _accessLog.RemoveRange(0, _accessLog.Count - AccessLogMax);
            }
        }

        // ------------------------------------------------------- móvil (F5.03) --

        /// <summary>Cliente web móvil mínimo (F5.03.1): servido localmente,
        /// emparejado por IP+token (el token viaja en el fragmento #, que NO
        /// se envía al servidor en cada request — se usa para pedir el QR y
        /// armar las llamadas Bearer).</summary>
        public static string MobileRemoteHtml
        {
            get
            {
                return @"<!DOCTYPE html>
<html lang=""es""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1"">
<title>Lumina — Mando</title>
<style>
body{font-family:Segoe UI,Arial;background:#0b1f2a;color:#fff;margin:0;padding:16px}
h1{font-size:18px;margin:0 0 12px} button{width:100%;padding:16px;margin:6px 0;
font-size:20px;border:0;border-radius:8px;background:#3aa6b9;color:#fff}
button.sec{background:#16394a} .st{margin-top:10px;font-size:13px;opacity:.8}
</style></head><body>
<h1>LuminaPresentation — Mando</h1>
<button onclick=""cmd('next')"">Siguiente ▶</button>
<button class=""sec"" onclick=""cmd('prev')"">◀ Anterior</button>
<button class=""sec"" onclick=""msg()"">Aviso (Lower Third)</button>
<button class=""sec"" onclick=""vol(-10)"">Volumen −</button>
<button class=""sec"" onclick=""vol(10)"">Volumen +</button>
<div class=""st"" id=""st"">(conectado)</div>
<script>
var token=location.hash.substring(1);
// audit-allow: este JS vive en la PÁGINA WEB REMOTA que el programa SIRVE al
// teléfono (F5.03 «cliente web ligero servido por el propio programa» —
// SPEC §8.2). No es un patrón web de la capa gestionada: es el cliente.
function cmd(a){fetch('/api/v1/'+a,{method:'POST',headers:{'Authorization':'Bearer '+token}})
 .then(function(r){document.getElementById('st').textContent='ok '+r.status})
 .catch(function(){document.getElementById('st').textContent='sin conexión'})}
function msg(){var t=prompt('Texto del aviso:');
 if(t)fetch('/api/v1/message',{method:'POST',headers:{'Authorization':'Bearer '+token,
 'Content-Type':'application/json'},body:JSON.stringify({text:t})})}
function vol(d){var nv=Math.max(0,Math.min(100,parseInt(localStorage.v||60)+d)); // audit-allow: cliente remoto F5.03
 localStorage.v=nv;fetch('/api/v1/volume',{method:'POST',headers:{'Authorization':'Bearer '+token, // audit-allow: cliente remoto F5.03
 'Content-Type':'application/json'},body:JSON.stringify({volume:nv})})}
</script></body></html>";
            }
        }

        public void Dispose() { Stop(); }
    }
}
