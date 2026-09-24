// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ApiServer.cs : API local HTTP sobre HttpListener (disponible desde .NET 2.0;
//  sin async/await para mantener la compatibilidad net35). SOLO escucha en
//  127.0.0.1 (nevera expuesta: jamás +/*). Un hilo de aceptación + ThreadPool
//  por conexión.
//
//  Rutas:
//    GET  /api/state      → JSON de estado (lumina_state_json)
//    POST /api/cmd        → {"action":"next|prev|black|clear|show|load|ping",
//                            "index":N,"on":bool,"scenario":…,"msg":"…"}
//    GET  /api/live.txt   → texto plano del slide actual + título
//    POST /obs            → webhook OBS {"event":"START|STOP|SLIDE","payload":…}
//
//  Token opcional: si el token configurado no es "", /api/* exige ?token= o el
//  encabezado X-Lumina-Token (401 si falta). /obs queda abierto a propósito:
//  solo registra y reenvía (no controla el motor).
//
//  Apagado limpio: Stop() cierra el listener y espera (con tope) al hilo de
//  aceptación — nunca bloquea indefinidamente.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Threading;
using lumina.bridge;
using lumina.core;

namespace lumina.api
{
    /// <summary>Args del webhook OBS reenviado a los suscriptores gestionados.</summary>
    public sealed class ObsWebhookEventArgs : EventArgs
    {
        public string Event;        // "START" | "STOP" | "SLIDE" | …
        public string PayloadJson;  // crudo (objeto o escalar serializado)
        public DateTime ReceivedUtc;
        public string RemoteAddress;
    }

    public sealed class ApiServer : IDisposable
    {
        private const int MaxBodyBytes = 4 * 1024 * 1024;   // tope anti-OOM (4 MB)
        private const int MaxObsLog = 50;                   // bitácora circular del webhook

        private readonly LuminaEngine _engine;
        private readonly int _port;
        private readonly string _token;                     // "" = sin token
        private readonly object _sync = new object();

        private HttpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        private readonly List<string> _obsLog = new List<string>();   // últimas N entradas
        private readonly List<SlideView> _slides = new List<SlideView>(); // catálogo para live.txt

        /// <summary>Se dispara (hilo del ThreadPool) por cada webhook OBS recibido.</summary>
        public event EventHandler<ObsWebhookEventArgs> OnObsWebhook;

        /// <summary>
        /// Proveedor opcional del texto "en vivo" para /api/live.txt (lo instala la UI,
        /// que conoce las líneas de cada slide). Si es null se usa el catálogo interno.
        /// </summary>
        public Func<string> LiveTextProvider;

        public ApiServer(LuminaEngine engine, int port, string token)
        {
            if (engine == null) throw new ArgumentNullException("engine");
            if (port < 1024 || port > 65535) throw new ArgumentOutOfRangeException("port",
                "El puerto debe estar entre 1024 y 65535.");
            _engine = engine;
            _port = port;
            _token = token ?? string.Empty;
        }

        public int Port { get { return _port; } }
        public bool IsRunning { get { return _running; } }

        /// <summary>Últimas entradas del webhook OBS (para diagnóstico/UI).</summary>
        public List<string> ObsLogSnapshot()
        {
            lock (_sync)
            {
                return new List<string>(_obsLog);
            }
        }

        /// <summary>Actualiza el catálogo de slides usado por /api/live.txt (lo llama la UI tras cargar).</summary>
        public void SetSlideCatalog(IList<SlideView> slides)
        {
            lock (_sync)
            {
                _slides.Clear();
                if (slides != null) _slides.AddRange(slides);
            }
        }

        /* ------------------------------------------------------------ ciclo */

        public void Start()
        {
            lock (_sync)
            {
                if (_running) return;
                HttpListener l = new HttpListener();
                // Prefijo estrictamente local (loopback): nada externo alcanza la API.
                l.Prefixes.Add("http://127.0.0.1:" + _port + "/");
                try
                {
                    l.Start();
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "No se pudo abrir http://127.0.0.1:" + _port + "/ — " + ex.Message, ex);
                }
                _listener = l;
                _running = true;
                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "Lumina.Api.Accept";
                _acceptThread.Start();
            }
        }

        /// <summary>Detiene el servidor sin bloquear indefinidamente (join con tope de 3 s).</summary>
        public void Stop()
        {
            HttpListener l;
            Thread t;
            lock (_sync)
            {
                if (!_running) return;
                _running = false;
                l = _listener;
                t = _acceptThread;
                _listener = null;
                _acceptThread = null;
            }
            if (l != null)
            {
                try { l.Stop(); } catch (Exception) { }   // desbloquea GetContext()
            }
            if (t != null && t.IsAlive)
            {
                try { t.Join(3000); } catch (Exception) { }
            }
            if (l != null)
            {
                try { l.Close(); } catch (Exception) { }
            }
        }

        public void Dispose() { Stop(); }

        private void AcceptLoop()
        {
            while (_running)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = _listener.GetContext();   // bloqueante; Stop() lo desbloquea
                }
                catch (Exception)
                {
                    if (!_running) break;           // apagado normal
                    continue;                        // error transitorio del socket
                }
                HttpContextSafe ctxState = new HttpContextSafe(ctx);
                ThreadPool.QueueUserWorkItem(delegate { HandleSafe(ctxState); });
            }
        }

        /// <summary>Envoltorio para capturar el contexto en el closure de net35.</summary>
        private sealed class HttpContextSafe
        {
            public readonly HttpListenerContext Ctx;
            public HttpContextSafe(HttpListenerContext ctx) { Ctx = ctx; }
        }

        /* --------------------------------------------------------- routing */

        private void HandleSafe(HttpContextSafe state)
        {
            HttpListenerContext ctx = state.Ctx;
            try
            {
                Handle(ctx);
            }
            catch (Exception ex)
            {
                try
                {
                    WriteJson(ctx, 500, "{\"ok\":0,\"error\":\"interno\"}");
                    Trace.WriteLine("Lumina.Api: error no manejado: " + ex.Message);
                }
                catch (Exception) { /* el cliente se fue: nada que hacer */ }
            }
            finally
            {
                try { ctx.Response.Close(); } catch (Exception) { }
            }
        }

        private void Handle(HttpListenerContext ctx)
        {
            HttpListenerRequest req = ctx.Request;
            string path = req.Url != null ? req.Url.AbsolutePath : "/";
            string method = req.HttpMethod ?? "GET";

            // Doble defensa: aunque el prefijo es loopback, se rechaza explícitamente
            // cualquier par remoto que no sea 127.0.0.1/::1.
            try
            {
                string ip = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : "";
                if (ip != "127.0.0.1" && ip != "::1") { WriteJson(ctx, 403, "{\"ok\":0,\"error\":\"solo localhost\"}"); return; }
            }
            catch (Exception) { }

            if (path == "/api/state" && method == "GET")
            {
                if (!CheckToken(ctx)) { WriteJson(ctx, 401, "{\"ok\":0,\"error\":\"token requerido\"}"); return; }
                string state = _engine.StateJson();
                WriteText(ctx, 200, "application/json; charset=utf-8", state);
                return;
            }

            if (path == "/api/cmd" && method == "POST")
            {
                if (!CheckToken(ctx)) { WriteJson(ctx, 401, "{\"ok\":0,\"error\":\"token requerido\"}"); return; }
                string body = ReadBody(req);
                HandleCmd(ctx, body);
                return;
            }

            if (path == "/api/live.txt" && method == "GET")
            {
                if (!CheckToken(ctx)) { WriteText(ctx, 401, "text/plain; charset=utf-8", "token requerido"); return; }
                WriteText(ctx, 200, "text/plain; charset=utf-8", BuildLiveText());
                return;
            }

            if (path == "/obs" && method == "POST")
            {
                HandleObs(ctx, req);
                return;
            }

            WriteJson(ctx, 404, "{\"ok\":0,\"error\":\"not found\"}");
        }

        /* --------------------------------------------------------- /api/cmd */

        private void HandleCmd(HttpListenerContext ctx, string body)
        {
            Dictionary<string, object> req;
            try { req = MiniJson.Parse(body); }
            catch (FormatException)
            {
                WriteJson(ctx, 400, "{\"ok\":0,\"error\":\"json invalido\"}");
                return;
            }

            string action = MiniJson.GetString(req, "action", string.Empty);
            int status;
            string extra = "";

            switch (action)
            {
                case "next":
                    status = _engine.Next();
                    break;
                case "prev":
                    status = _engine.Prev();
                    break;
                case "clear":
                    status = _engine.Clear();
                    break;
                case "black":
                    status = _engine.Black(MiniJson.GetBool(req, "on", true));
                    break;
                case "show":
                    status = _engine.ShowSlide((int)MiniJson.GetInt(req, "index", -1));
                    break;
                case "load":
                    status = HandleLoad(req, ref extra);
                    break;
                case "ping":
                    status = _engine.Ping(MiniJson.GetString(req, "msg", "ping"));
                    break;
                default:
                    WriteJson(ctx, 400, "{\"ok\":0,\"error\":\"accion desconocida\"}");
                    return;
            }

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"ok\":").Append(status == LuminaStatus.Ok ? 1 : 0);
            sb.Append(",\"status\":").Append(status.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (extra.Length > 0) { sb.Append(',').Append(extra); }
            sb.Append('}');
            WriteJson(ctx, status == LuminaStatus.Ok ? 200 : 400, sb.ToString());
        }

        /// <summary>load: "scenario" como string JSON o como objeto; actualiza el catálogo live.</summary>
        private int HandleLoad(Dictionary<string, object> req, ref string extra)
        {
            object sc;
            string scenarioJson;
            if (!req.TryGetValue("scenario", out sc) || sc == null) return LuminaStatus.ErrArg;
            if (sc is string)
                scenarioJson = (string)sc;
            else
                scenarioJson = MiniJson.Serialize(sc);

            int status = _engine.LoadScenario(scenarioJson);
            if (status == LuminaStatus.Ok)
            {
                List<SlideView> views = ScenarioBuilder.FlattenScenario(scenarioJson,
                    delegate(string sj) { return LuminaEngine.SongParse(sj); });
                lock (_sync) { _slides.Clear(); _slides.AddRange(views); }
                extra = "\"slideCount\":" + views.Count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            return status;
        }

        /* ------------------------------------------------------------ /obs */

        private void HandleObs(HttpListenerContext ctx, HttpListenerRequest req)
        {
            string body = ReadBody(req);
            string evName;
            string payloadJson;
            try
            {
                Dictionary<string, object> o = MiniJson.Parse(body);
                evName = MiniJson.GetString(o, "event", string.Empty).Trim();
                object payload;
                if (!o.TryGetValue("payload", out payload) || payload == null) payload = "";
                payloadJson = payload is string
                    ? (string)payload
                    : MiniJson.Serialize(payload);
            }
            catch (FormatException)
            {
                WriteJson(ctx, 400, "{\"ok\":0,\"error\":\"json invalido\"}");
                return;
            }

            if (evName.Length == 0)
            {
                WriteJson(ctx, 400, "{\"ok\":0,\"error\":\"falta event\"}");
                return;
            }

            string remote = "";
            try { remote = req.RemoteEndPoint != null ? req.RemoteEndPoint.Address.ToString() : ""; }
            catch (Exception) { }

            ObsWebhookEventArgs args = new ObsWebhookEventArgs();
            args.Event = evName;
            args.PayloadJson = payloadJson;
            args.ReceivedUtc = DateTime.UtcNow;
            args.RemoteAddress = remote;

            string line = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss",
                System.Globalization.CultureInfo.InvariantCulture) + " " + evName + " " + remote +
                (payloadJson.Length > 0 ? " " + payloadJson : "");
            lock (_sync)
            {
                _obsLog.Add(line);
                if (_obsLog.Count > MaxObsLog) _obsLog.RemoveAt(0);
            }

            EventHandler<ObsWebhookEventArgs> h = OnObsWebhook;
            if (h != null)
            {
                try { h(this, args); }
                catch (Exception) { /* los suscriptores no rompen el webhook */ }
            }

            WriteJson(ctx, 200, "{\"ok\":1}");
        }

        /* -------------------------------------------------------- live.txt */

        private string BuildLiveText()
        {
            Func<string> provider = LiveTextProvider;
            if (provider != null)
            {
                try
                {
                    string s = provider();
                    if (s != null) return s;
                }
                catch (Exception) { /* cae al catálogo interno */ }
            }

            // Respaldo: catálogo interno + estado del motor.
            string stateJson;
            int current = -1; bool black = false; bool cleared = false;
            try
            {
                stateJson = _engine.StateJson();
                Dictionary<string, object> st = MiniJson.Parse(stateJson);
                current = (int)MiniJson.GetInt(st, "current", -1);
                black = MiniJson.GetBool(st, "black", false);
                cleared = MiniJson.GetBool(st, "cleared", false);
            }
            catch (Exception) { return "estado no disponible\n"; }

            StringBuilder sb = new StringBuilder();
            if (black) sb.Append("[NEGRO]\n");
            else if (cleared) sb.Append("[LIMPIO]\n");

            SlideView slide = null;
            lock (_sync)
            {
                if (current >= 0 && current < _slides.Count) slide = _slides[current];
            }
            if (slide != null)
            {
                if (slide.Title.Length > 0) sb.Append(slide.Title).Append('\n');
                if (slide.RefLabel.Length > 0) sb.Append(slide.RefLabel).Append('\n');
                foreach (string l in slide.Lines)
                {
                    if (l == null) continue;
                    string t = l.Trim();
                    if (t.Length > 0) sb.Append(t).Append('\n');
                }
            }
            else
            {
                sb.Append("(sin slide)").Append('\n');
            }
            return sb.ToString();
        }

        /* ----------------------------------------------------------- varios */

        /// <summary>Token en ?token= o X-Lumina-Token (solo /api/*; "" = deshabilitado).</summary>
        private bool CheckToken(HttpListenerContext ctx)
        {
            if (_token.Length == 0) return true;
            string qs = ctx.Request.QueryString["token"];
            if (qs != null && qs == _token) return true;
            string hdr = ctx.Request.Headers["X-Lumina-Token"];
            return hdr != null && hdr == _token;
        }

        private static string ReadBody(HttpListenerRequest req)
        {
            long len = req.ContentLength64;
            if (len <= 0) return string.Empty;
            if (len > MaxBodyBytes) throw new InvalidOperationException("cuerpo demasiado grande");
            byte[] buf = new byte[(int)len];
            using (System.IO.Stream s = req.InputStream)
            {
                int read = 0;
                while (read < buf.Length)
                {
                    int n = s.Read(buf, read, buf.Length - read);
                    if (n <= 0) break;
                    read += n;
                }
            }
            return MiniJson.Utf8BytesToString(buf);
        }

        private static void WriteJson(HttpListenerContext ctx, int code, string json)
        {
            WriteText(ctx, code, "application/json; charset=utf-8", json);
        }

        private static void WriteText(HttpListenerContext ctx, int code, string contentType, string text)
        {
            HttpListenerResponse res = ctx.Response;
            byte[] body = new UTF8Encoding(false).GetBytes(text ?? string.Empty);
            res.StatusCode = code;
            res.ContentType = contentType;
            res.ContentLength64 = body.Length;
            try
            {
                using (System.IO.Stream os = res.OutputStream) os.Write(body, 0, body.Length);
            }
            catch (Exception) { /* cliente desconectado */ }
        }
    }
}
