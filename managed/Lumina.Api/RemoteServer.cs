// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  RemoteServer.cs : control remoto desde el MÓVIL por red local (requisito del
//  spec §3.4 «Cliente Remoto») — programa de escritorio, la página remota es
//  solo un mando a distancia servido por la propia app.
//
//  POR QUÉ TcpListener y NO HttpListener: enlazar 0.0.0.0 con http.sys
//  (HttpListener) exige urlacl de administrador o app elevada — prohibido por
//  el spec (§2.6: cero operaciones manuales/registro). Un socket TCP propio
//  escucha en cualquier interfaz como usuario normal (el firewall de Windows
//  solo pregunta una vez «Permitir acceso», UX estándar).
//
//  Rutas (HTTP/1.1 mínimo, GET/POST, Connection: close):
//    GET  /remote        → página HTML autocontenida (mando táctil en español)
//    GET  /              → redirige a /remote
//    GET  /api/state     → estado del motor (JSON)
//    GET  /api/catalog   → catálogo de slides para el mando (JSON)
//    GET  /api/live.txt  → texto en vivo (plano)
//    POST /api/cmd       → next|prev|show|black|clear|notice|ping
//
//  TOKEN: si está configurado, TODA ruta lo exige (?token= o cabecera
//  X-Lumina-Token). En LAN se recomienda SIEMPRE token (la UI lo avisa).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using lumina.bridge;
using lumina.core;

namespace lumina.api
{
    /// <summary>Aviso/mensaje desde el mando remoto (delegado propio: net35 EventHandler<T> exige EventArgs).</summary>
    public delegate void NoticeEventHandler(object sender, string text);

    public sealed class RemoteServer : IDisposable
    {
        private const int MaxBodyBytes = 256 * 1024;      // tope anti-abuso
        private const int MaxHeaderBytes = 32 * 1024;

        private readonly LuminaEngine _engine;
        private readonly int _port;
        private readonly string _token;
        private readonly object _sync = new object();

        private TcpListener _listener;
        private Thread _acceptThread;
        private volatile bool _running;

        private readonly List<SlideView> _slides = new List<SlideView>();

        /// <summary>Texto "en vivo" opcional (lo instala la UI; si es null, catálogo interno).</summary>
        public Func<string> LiveTextProvider;

        /// <summary>Aviso/mensaje personalizado desde el mando móvil → la UI lo proyecta.</summary>
        public event NoticeEventHandler NoticeReceived;

        public RemoteServer(LuminaEngine engine, int port, string token)
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
        public bool HasToken { get { return _token.Length > 0; } }

        /// <summary>Direcciones IPv4 locales para mostrar en la UI (http://IP:puerto/remote).</summary>
        public static List<string> LocalAddresses()
        {
            List<string> result = new List<string>();
            try
            {
                string host = Dns.GetHostName();
                foreach (IPAddress a in Dns.GetHostAddresses(host))
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a))
                        result.Add(a.ToString());
                }
            }
            catch (Exception) { }
            if (result.Count == 0) result.Add("127.0.0.1");
            return result;
        }

        /// <summary>URL para abrir el mando (ya con token si existe).</summary>
        public string RemoteUrl()
        {
            string ip = LocalAddresses()[0];
            string u = "http://" + ip + ":" + _port + "/remote";
            if (_token.Length > 0) u += "?token=" + Uri.EscapeDataString(_token);
            return u;
        }

        /// <summary>Actualiza el catálogo de slides del mando (lo llama la UI tras cargar escenario).</summary>
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
                TcpListener l = new TcpListener(IPAddress.Any, _port);
                try
                {
                    l.Start(16);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        "No se pudo escuchar en el puerto " + _port + " (control remoto) — " + ex.Message, ex);
                }
                _listener = l;
                _running = true;
                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "Lumina.Remote.Accept";
                _acceptThread.Start();
            }
        }

        public void Stop()
        {
            TcpListener l;
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
                try { l.Stop(); } catch (Exception) { }
            }
            if (t != null && t.IsAlive)
            {
                try { t.Join(3000); } catch (Exception) { }
            }
        }

        public void Dispose() { Stop(); }

        private void AcceptLoop()
        {
            while (_running)
            {
                TcpClient client;
                try
                {
                    client = _listener.AcceptTcpClient();
                }
                catch (Exception)
                {
                    if (!_running) break;
                    continue;
                }
                ThreadPool.QueueUserWorkItem(delegate(object o)
                {
                    TcpClient c = o as TcpClient;
                    if (c != null) HandleClientSafe(c);
                }, client);
            }
        }

        private void HandleClientSafe(TcpClient client)
        {
            try
            {
                client.ReceiveTimeout = 10000;
                client.SendTimeout = 10000;
                using (NetworkStream ns = client.GetStream())
                {
                    HttpRequest req = ReadRequest(ns);
                    if (req != null)
                    {
                        byte[] resp = Route(req);
                        ns.Write(resp, 0, resp.Length);
                        ns.Flush();
                    }
                }
            }
            catch (Exception)
            {
                // Cliente desconectado a mitad: nada que hacer.
            }
            finally
            {
                try { client.Close(); } catch (Exception) { }
            }
        }

        /* ------------------------------------------------------------ HTTP */

        private sealed class HttpRequest
        {
            public string Method = "GET";
            public string Path = "/";
            public string Query = string.Empty;   // sin '?'
            public Dictionary<string, string> Headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public byte[] Body = new byte[0];
        }

        private static HttpRequest ReadRequest(NetworkStream ns)
        {
            // lee cabeceras (hasta \r\n\r\n) con tope; luego body por Content-Length
            byte[] buf = new byte[MaxHeaderBytes];
            int total = 0;
            int headerEnd = -1;
            while (total < buf.Length)
            {
                int n = ns.Read(buf, total, buf.Length - total);
                if (n <= 0) break;
                total += n;
                headerEnd = FindHeaderEnd(buf, total);
                if (headerEnd >= 0) break;
            }
            if (headerEnd < 0) return null;

            string headerText = Encoding.ASCII.GetString(buf, 0, headerEnd);
            string[] lines = headerText.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return null;

            HttpRequest req = new HttpRequest();
            string[] parts = lines[0].Split(' ');
            if (parts.Length >= 2)
            {
                req.Method = parts[0].ToUpperInvariant();
                string target = parts[1];
                int q = target.IndexOf('?');
                if (q >= 0)
                {
                    req.Path = target.Substring(0, q);
                    req.Query = target.Substring(q + 1);
                }
                else req.Path = target;
            }
            for (int i = 1; i < lines.Length; i++)
            {
                int c = lines[i].IndexOf(':');
                if (c > 0)
                    req.Headers[lines[i].Substring(0, c).Trim()] = lines[i].Substring(c + 1).Trim();
            }

            // body
            string lenStr;
            int contentLen = 0;
            if (req.Headers.TryGetValue("Content-Length", out lenStr))
            {
                int.TryParse(lenStr, out contentLen);
            }
            if (contentLen > 0 && contentLen <= MaxBodyBytes)
            {
                byte[] body = new byte[contentLen];
                int already = total - (headerEnd + 4);
                if (already > 0) Buffer.BlockCopy(buf, headerEnd + 4, body, 0, Math.Min(already, contentLen));
                int read = Math.Min(already, contentLen);
                while (read < contentLen)
                {
                    int n = ns.Read(body, read, contentLen - read);
                    if (n <= 0) break;
                    read += n;
                }
                req.Body = body;
            }
            return req;
        }

        private static int FindHeaderEnd(byte[] buf, int len)
        {
            for (int i = 0; i + 3 < len; i++)
            {
                if (buf[i] == 13 && buf[i + 1] == 10 && buf[i + 2] == 13 && buf[i + 3] == 10) return i;
            }
            return -1;
        }

        private static byte[] Response(int code, string contentType, byte[] body, bool headOnly)
        {
            string reason = code == 200 ? "OK" : (code == 301 ? "Moved Permanently" :
                (code == 400 ? "Bad Request" : (code == 401 ? "Unauthorized" : "Not Found")));
            StringBuilder head = new StringBuilder();
            head.Append("HTTP/1.1 ").Append(code).Append(' ').Append(reason).Append("\r\n");
            head.Append("Content-Type: ").Append(contentType).Append("\r\n");
            head.Append("Content-Length: ").Append(body.Length).Append("\r\n");
            head.Append("Cache-Control: no-store\r\n");
            head.Append("Connection: close\r\n\r\n");
            byte[] h = Encoding.ASCII.GetBytes(head.ToString());
            if (headOnly) return h;
            byte[] resp = new byte[h.Length + body.Length];
            Buffer.BlockCopy(h, 0, resp, 0, h.Length);
            Buffer.BlockCopy(body, 0, resp, h.Length, body.Length);
            return resp;
        }

        private static byte[] TextResponse(int code, string contentType, string text)
        {
            return Response(code, contentType, new UTF8Encoding(false).GetBytes(text ?? string.Empty), false);
        }

        /* ------------------------------------------------------------ rutas */

        private byte[] Route(HttpRequest req)
        {
            string path = req.Path;
            string method = req.Method;

            if (path == "/" || path == "/remote" || path == "/index.html")
            {
                if (method != "GET" && method != "HEAD")
                    return TextResponse(400, "application/json", "{\"ok\":0,\"error\":\"GET\"}");
                // redirección de / a /remote (mismo host, sin estado)
                if (path == "/")
                {
                    string loc = "/remote" + (req.Query.Length > 0 ? "?" + req.Query : string.Empty);
                    byte[] h = Encoding.ASCII.GetBytes(
                        "HTTP/1.1 301 Moved Permanently\r\nLocation: " + loc +
                        "\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                    return h;
                }
                return TextResponse(200, "text/html; charset=utf-8", RemotePage.Html);
            }

            if (path == "/api/state" && method == "GET")
            {
                if (!Authorized(req)) return TextResponse(401, "application/json", "{\"ok\":0,\"error\":\"token\"}");
                return TextResponse(200, "application/json; charset=utf-8", SafeState());
            }

            if (path == "/api/catalog" && method == "GET")
            {
                if (!Authorized(req)) return TextResponse(401, "application/json", "{\"ok\":0,\"error\":\"token\"}");
                return TextResponse(200, "application/json; charset=utf-8", BuildCatalog());
            }

            if (path == "/api/live.txt" && method == "GET")
            {
                if (!Authorized(req)) return TextResponse(401, "text/plain; charset=utf-8", "token");
                return TextResponse(200, "text/plain; charset=utf-8", BuildLiveText());
            }

            if (path == "/api/cmd" && method == "POST")
            {
                if (!Authorized(req)) return TextResponse(401, "application/json", "{\"ok\":0,\"error\":\"token\"}");
                return HandleCmd(req);
            }

            return TextResponse(404, "application/json", "{\"ok\":0,\"error\":\"not found\"}");
        }

        private bool Authorized(HttpRequest req)
        {
            if (_token.Length == 0) return true;
            // ?token=… (o token= dentro del query) o cabecera X-Lumina-Token
            foreach (string kv in req.Query.Split('&'))
            {
                if (kv.StartsWith("token=", StringComparison.OrdinalIgnoreCase))
                {
                    if (Uri.UnescapeDataString(kv.Substring(6)) == _token) return true;
                }
            }
            string h;
            if (req.Headers.TryGetValue("X-Lumina-Token", out h) && h == _token) return true;
            return false;
        }

        private string SafeState()
        {
            try { return _engine.StateJson(); }
            catch (Exception) { return "{\"ok\":0,\"error\":\"motor\"}"; }
        }

        private string BuildCatalog()
        {
            StringBuilder sb = new StringBuilder(2048);
            sb.Append("{\"ok\":1,\"slides\":[");
            lock (_sync)
            {
                for (int i = 0; i < _slides.Count; i++)
                {
                    SlideView v = _slides[i];
                    if (i > 0) sb.Append(',');
                    sb.Append("{\"i\":").Append(i.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    sb.Append(",\"t\":\"").Append(JsonStr(v.FirstLine)).Append('"');
                    sb.Append(",\"r\":\"").Append(JsonStr(v.RefLabel)).Append('"');
                    sb.Append('}');
                }
            }
            sb.Append("]}");
            return sb.ToString();
        }

        private string BuildLiveText()
        {
            Func<string> p = LiveTextProvider;
            if (p != null)
            {
                try { return p(); } catch (Exception) { }
            }
            return string.Empty;
        }

        private byte[] HandleCmd(HttpRequest req)
        {
            string body = new UTF8Encoding(false).GetString(req.Body);
            Dictionary<string, object> o;
            try { o = MiniJson.Parse(body); }
            catch (FormatException)
            {
                return TextResponse(400, "application/json", "{\"ok\":0,\"error\":\"json\"}");
            }
            string action = MiniJson.GetString(o, "action", string.Empty);
            int status;
            switch (action)
            {
                case "next": status = _engine.Next(); break;
                case "prev": status = _engine.Prev(); break;
                case "clear": status = _engine.Clear(); break;
                case "black": status = _engine.Black(MiniJson.GetBool(o, "on", true)); break;
                case "show": status = _engine.ShowSlide((int)MiniJson.GetInt(o, "index", -1)); break;
                case "ping": status = _engine.Ping("remote"); break;
                case "notice":
                    {
                        string text = MiniJson.GetString(o, "text", string.Empty);
                        NoticeEventHandler h = NoticeReceived;
                        if (h != null && text.Length > 0)
                        {
                            try { h(this, text); } catch (Exception) { }
                        }
                        status = LuminaStatus.Ok;
                        break;
                    }
                default:
                    return TextResponse(400, "application/json", "{\"ok\":0,\"error\":\"accion\"}");
            }
            return TextResponse(status == LuminaStatus.Ok ? 200 : 400, "application/json",
                "{\"ok\":" + (status == LuminaStatus.Ok ? 1 : 0) + ",\"status\":" + status + "}");
        }

        private static string JsonStr(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", " ").Replace("\n", " ");
        }

        // ================================================================ página

        /// <summary>Página del mando (autocontenida: HTML+CSS+JS en un solo archivo).</summary>
        private static class RemotePage
        {
            public const string Html =
@"<!DOCTYPE html>
<html lang=""es"">
<head>
<meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1, maximum-scale=1, user-scalable=no"">
<title>LuminaPresentation · Mando</title>
<style>
*{box-sizing:border-box;-webkit-tap-highlight-color:transparent}
body{margin:0;background:#12141a;color:#e8eaf0;font-family:'Segoe UI',system-ui,sans-serif;
     padding:10px 10px calc(10px + env(safe-area-inset-bottom));max-width:640px;margin-left:auto;margin-right:auto}
h1{font-size:17px;margin:6px 0 2px;color:#f0a93b;letter-spacing:.5px}
.sub{font-size:12px;color:#7d8496;margin-bottom:10px}
.card{background:#1b1f28;border:1px solid #262b36;border-radius:12px;padding:12px;margin-bottom:10px}
.now{font-size:15px;line-height:1.35;min-height:40px;white-space:pre-wrap}
.now b{color:#f0a93b}
.row{display:flex;gap:10px;margin-top:8px}
button{flex:1;border:0;border-radius:12px;padding:16px 6px;font-size:20px;font-weight:600;
       background:#2f3646;color:#e8eaf0;outline:none;touch-action:manipulation}
button:active{background:#3d4658;transform:scale(.97)}
button.primary{background:#f0a93b;color:#191b22}
button.primary:active{background:#d9952f}
button.warn{background:#8a3b3b;color:#ffe9e9}
button.small{padding:10px 6px;font-size:14px}
.slidel{max-height:38vh;overflow:auto}
.sl{display:block;width:100%;text-align:left;background:#171a21;border:1px solid #262b36;
     color:#c6cbd8;border-radius:8px;padding:9px 10px;font-size:13px;margin-bottom:6px}
.sl.cur{background:#2f3646;color:#fff;border-color:#f0a93b}
.num{color:#7d8496;font-size:11px;margin-right:6px}
#notice{width:100%;background:#10131a;color:#e8eaf0;border:1px solid #262b36;border-radius:8px;
        padding:10px;font-size:14px;margin-top:8px}
.foot{color:#5a6172;font-size:11px;text-align:center;margin-top:6px}
</style>
</head>
<body>
<h1>◉ LuminaPresentation · Mando</h1>
<div class=""sub"">Control remoto por red local — la proyección sigue en el PC</div>
<div class=""card"">
  <div class=""now"" id=""now"">…</div>
</div>
<div class=""row"">
  <button id=""bPrev"">◀</button>
  <button class=""primary"" id=""bNext"">▶</button>
</div>
<div class=""row"">
  <button class=""small"" id=""bBlack"">⬛ Pantalla negra</button>
  <button class=""small"" id=""bClear"">◻ Limpiar</button>
</div>
<div class=""card"" style=""margin-top:10px"">
  <b style=""font-size:13px"">Diapositivas</b>
  <div class=""slidel"" id=""list"" style=""margin-top:8px""></div>
</div>
<div class=""card"">
  <b style=""font-size:13px"">Aviso en pantalla</b>
  <input id=""notice"" placeholder=""Escribe el aviso y pulsa Enviar…"">
  <div class=""row""><button class=""small primary"" id=""bNotice"">Enviar aviso</button></div>
</div>
<div class=""foot"">LuminaPresentation Suite · modo local (sin nube)</div>
<script>
var TOK = location.search.match(/token=([^&]+)/);
TOK = TOK ? decodeURIComponent(TOK[1]) : '';
function u(p){ return p + (TOK ? '?token='+encodeURIComponent(TOK) : ''); }
function req(method, url, body, cb){
  var x = new XMLHttpRequest();
  x.open(method, url, true);
  x.onreadystatechange = function(){
    if (x.readyState == 4 && cb) { try { cb(JSON.parse(x.responseText)); } catch(e) { cb(null); } }
  };
  if (body){ x.setRequestHeader('Content-Type','application/json'); x.send(body); }
  else x.send();
}
function cmd(action, extra, cb){
  var o = {action:action};
  if (extra) for (var k in extra) o[k] = extra[k];
  req('POST', u('/api/cmd'), JSON.stringify(o), cb);
}
var catalog = [], cur = -1, black = false;
function refresh(){
  req('GET', u('/api/state'), null, function(s){
    if (!s || s.error){ return; }
    cur = s.current; black = !!s.black;
    document.getElementById('bBlack').style.background = black ? '#f0a93b' : '';
    document.getElementById('bBlack').style.color = black ? '#191b22' : '';
  });
  req('GET', u('/api/live.txt'), null, function(){});
  req('GET', u('/api/catalog'), null, function(c){
    if (c && c.slides){ catalog = c.slides; renderList(); }
  });
}
function renderList(){
  var el = document.getElementById('list');
  el.innerHTML = '';
  for (var i = 0; i < catalog.length; i++){
    (function(i){
      var b = document.createElement('button');
      b.className = 'sl' + (i == cur ? ' cur' : '');
      b.innerHTML = '<span class=""num"">' + (i+1) + '</span>' +
        (catalog[i].r ? '<b>' + esc(catalog[i].r) + '</b> · ' : '') + esc(catalog[i].t);
      b.onclick = function(){ cmd('show', {index:i}); };
      el.appendChild(b);
    })(i);
  }
  var now = document.getElementById('now');
  if (cur >= 0 && catalog[cur]){
    now.innerHTML = (catalog[cur].r ? '<b>' + esc(catalog[cur].r) + '</b><br>' : '') + esc(catalog[cur].t);
  } else { now.textContent = 'Sin proyección activa'; }
}
function esc(s){ return (s||'').replace(/&/g,'&amp;').replace(/</g,'&lt;'); }
document.getElementById('bNext').onclick = function(){ cmd('next'); };
document.getElementById('bPrev').onclick = function(){ cmd('prev'); };
document.getElementById('bBlack').onclick = function(){ cmd('black', {on: !black}); };
document.getElementById('bClear').onclick = function(){ cmd('clear'); };
document.getElementById('bNotice').onclick = function(){
  var t = document.getElementById('notice').value;
  if (t) cmd('notice', {text:t});
};
refresh(); setInterval(refresh, 800);
</script>
</body>
</html>";
        }
    }
}
