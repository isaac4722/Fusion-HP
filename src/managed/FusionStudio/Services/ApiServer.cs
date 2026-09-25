// ============================================================================
//  Fusion-HP · FusionStudio/Services/ApiServer.cs — servidor API local
//  [SPEC §8.1]: HttpListener SOLO en red local, token obligatorio
//  (Authorization: Bearer), 6 endpoints normativos + control remoto móvil
//  servido como página ligera [SPEC §8.2]. Registro de cada petición [§8.1.4].
//  Corrección del prototipo: el modo API funciona (integración OBS y estado).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading;
using Fusion.Shared;
using Fusion.Shared.Model;

namespace Fusion.Studio.Services
{
    public class ApiServer
    {
        readonly LiveOrchestrator live;
        readonly AppSettings settings;
        HttpListener listener;
        Thread thread;
        volatile bool running;
        int activeClients;
        public event Action<string> Logged;

        public bool Running { get { return running; } }

        public ApiServer(LiveOrchestrator live, AppSettings settings)
        {
            this.live = live;
            this.settings = settings;
        }

        public void Start()
        {
            if (running) return;
            // Token obligatorio: si no existe, se genera [SPEC §8.1.1]
            if (string.IsNullOrEmpty(settings.ApiToken))
            {
                var rng = new Random(Environment.TickCount);
                var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
                var sb = new System.Text.StringBuilder();
                for (int i = 0; i < 24; i++) sb.Append(chars[rng.Next(chars.Length)]);
                settings.ApiToken = sb.ToString();
                settings.Save();
            }
            // Preferencia normativa: red local completa [SPEC §8.1.1] (http://+)
            // — pero el comodín fuerte exige ACL de URL (elevación prohibida [SPEC §11.4]):
            // sin permisos se degrada a localhost (OBS en la misma máquina funciona igual)
            // y se registra la limitación sin fallo silencioso.
            string[] attempts = new string[]
            {
                "http://+:" + settings.ApiPort + "/",
                "http://*:" + settings.ApiPort + "/",
                "http://localhost:" + settings.ApiPort + "/"
            };
            bool bound = false;
            string used = "";
            foreach (var prefix in attempts)
            {
                try
                {
                    listener = new HttpListener();
                    listener.Prefixes.Add(prefix);
                    listener.Start();
                    bound = true;
                    used = prefix;
                    break;
                }
                catch
                {
                    try { listener.Close(); } catch { }
                }
            }
            if (!bound)
            {
                Log("No se pudo activar la API: el puerto " + settings.ApiPort + " está ocupado o inaccesible.");
                running = false;
                return;
            }
            if (!used.StartsWith("http://+"))
                Log("API activa en " + used + " (solo este equipo). El control remoto desde el teléfono " +
                    "requiere el prefijo http://+, que Windows reserva a administradores; OBS en este equipo funciona igual.");
            else
                Log("API activa en la red local, puerto " + settings.ApiPort);
            running = true;
            thread = new Thread(AcceptLoop);
            thread.IsBackground = true;
            thread.Start();
        }

        public void Stop()
        {
            running = false;
            try { if (listener != null) listener.Stop(); } catch { }
            listener = null;
        }

        void AcceptLoop()
        {
            while (running)
            {
                try
                {
                    var ctx = listener.GetContext();
                    if (activeClients >= settings.MaxApiClients)
                    {
                        WriteJson(ctx, 503, JsonValue.Object().Set("error", JsonValue.Make("limite de clientes alcanzado")));
                        continue;
                    }
                    Interlocked.Increment(ref activeClients);
                    try { Handle(ctx); }
                    finally { Interlocked.Decrement(ref activeClients); }
                }
                catch
                {
                    if (!running) break;
                    Thread.Sleep(100);
                }
            }
        }

        void Handle(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            string method = ctx.Request.HttpMethod;

            // Control remoto móvil [SPEC §8.2] y página de texto para OBS [SPEC §8.4.3]
            if (path == "/remote" || path == "/" || path == "/index.html")
            {
                string token = ctx.Request.QueryString["token"];
                if (!string.IsNullOrEmpty(token) && token == settings.ApiToken)
                {
                    ctx.Response.ContentType = "text/html; charset=utf-8";
                    WriteBytes(ctx, 200, Encoding.UTF8.GetBytes(RemoteHtml));
                    return;
                }
                if (path == "/" || path == "/index.html" && string.IsNullOrEmpty(token))
                {
                    // raíz informativa (sin exponer nada)
                    WriteJson(ctx, 200, JsonValue.Object()
                        .Set("service", JsonValue.Make("Fusion HP API"))
                        .Set("version", JsonValue.Make("1.0")));
                    return;
                }
            }

            // Endpoints normativos /api/v1/* — token obligatorio [SPEC §8.1]
            if (path.StartsWith("/api/", StringComparison.Ordinal))
            {
                string auth = ctx.Request.Headers["Authorization"] ?? "";
                string bearer = auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? auth.Substring(7).Trim() : "";
                if (!string.IsNullOrEmpty(ctx.Request.QueryString["token"])) bearer = ctx.Request.QueryString["token"];
                if (bearer != settings.ApiToken)
                {
                    Log("401 " + path + " (token invalido o ausente)");
                    WriteJson(ctx, 401, JsonValue.Object().Set("error", JsonValue.Make("token requerido")));
                    return;
                }
            }
            else
            {
                WriteJson(ctx, 404, JsonValue.Object().Set("error", JsonValue.Make("ruta desconocida")));
                return;
            }

            Log(method + " " + path);

            // ---- GET /api/v1/state [HOLY] ----
            if (path == "/api/v1/state" && method == "GET")
            {
                var j = StateJson();
                WriteJson(ctx, 200, j);
                return;
            }

            // ---- GET /api/v1/text?format=plain|json (para OBS) [SPEC §8.4.3] ----
            if (path == "/api/v1/text" && method == "GET")
            {
                string fmt = ctx.Request.QueryString["format"] ?? "plain";
                var cur = live.State.Current;
                string text = "";
                if (cur != null && cur.Lines.Count > 0)
                {
                    text = cur.Lines[Math.Max(0, Math.Min(live.State.LineIndex, cur.Lines.Count - 1))];
                }
                if (fmt == "json")
                {
                    var j = JsonValue.Object();
                    j.Set("text", JsonValue.Make(text));
                    j.Set("line", JsonValue.Make(live.State.LineIndex));
                    j.Set("count", JsonValue.Make(cur != null ? cur.Lines.Count : 0));
                    j.Set("reference", JsonValue.Make(cur != null ? cur.Reference : ""));
                    j.Set("blank", JsonValue.Make(live.State.IsBlank));
                    WriteJson(ctx, 200, j);
                }
                else
                {
                    ctx.Response.ContentType = "text/plain; charset=utf-8";
                    WriteBytes(ctx, 200, Encoding.UTF8.GetBytes(text));
                }
                return;
            }

            // ---- POST /api/v1/next | /api/v1/prev ----
            if ((path == "/api/v1/next" || path == "/api/v1/prev") && method == "POST")
            {
                if (path.EndsWith("/next")) live.NextLine(); else live.PrevLine();
                WriteJson(ctx, 200, StateJson());
                return;
            }

            // ---- POST /api/v1/goto {scenario|element|line} ----
            if (path == "/api/v1/goto" && method == "POST")
            {
                var body = ReadBody(ctx);
                int scn = body.GetInt("scenario", -1);
                int el = body.GetInt("element", 0);
                int line = body.GetInt("line", 0);
                if (scn < 0)
                {
                    // por título
                    string title = body.GetStr("title", "");
                    if (live.Project != null)
                        for (int i = 0; i < live.Project.Scenarios.Count; i++)
                            if (string.Equals(live.Project.Scenarios[i].Title, title, StringComparison.OrdinalIgnoreCase)) { scn = i; break; }
                }
                if (scn >= 0 && live.Project != null && scn < live.Project.Scenarios.Count)
                {
                    live.Select(scn, el, line);
                    WriteJson(ctx, 200, StateJson());
                }
                else WriteJson(ctx, 400, JsonValue.Object().Set("error", JsonValue.Make("escenario no encontrado")));
                return;
            }

            // ---- POST /api/v1/bible {reference, translation} [HOLY] ----
            if (path == "/api/v1/bible" && method == "POST")
            {
                var body = ReadBody(ctx);
                string reference = body.GetStr("reference", "");
                var refr = Shared.Bible.BibleReference.Parse(reference);
                if (!refr.Valid)
                {
                    WriteJson(ctx, 400, JsonValue.Object().Set("error", JsonValue.Make("cita invalida (ej: Juan 3:16)")));
                    return;
                }
                var bible = live.Bibles.List();
                if (bible.Count == 0)
                {
                    WriteJson(ctx, 404, JsonValue.Object().Set("error", JsonValue.Make("no hay biblias instaladas")));
                    return;
                }
                var verses = live.Bibles.GetPassage(bible[0], refr);
                if (verses.Count == 0)
                {
                    WriteJson(ctx, 404, JsonValue.Object().Set("error", JsonValue.Make("pasaje no encontrado")));
                    return;
                }
                var scn = new Scenario { Title = refr.ToString() };
                for (int i = 0; i < verses.Count; i += 3)
                {
                    var e2 = new Element { Kind = ElementKind.Verse, Reference = refr.ToString() };
                    for (int k = i; k < i + 3 && k < verses.Count; k++) e2.Lines.Add(verses[k]);
                    scn.Elements.Add(e2);
                }
                live.SendToLive(scn);
                WriteJson(ctx, 200, StateJson());
                return;
            }

            // ---- POST /api/v1/message {text} [HOLY] ----
            if (path == "/api/v1/message" && method == "POST")
            {
                var body = ReadBody(ctx);
                string text = body.GetStr("text", "");
                if (text.Length == 0)
                {
                    WriteJson(ctx, 400, JsonValue.Object().Set("error", JsonValue.Make("texto vacio")));
                    return;
                }
                var scn = new Scenario { Title = "Aviso remoto" };
                scn.Elements.Add(new Element { Kind = ElementKind.LowerThird, OverlayText = text, Lines = { text } });
                live.SendToLive(scn);
                WriteJson(ctx, 200, StateJson());
                return;
            }

            WriteJson(ctx, 404, JsonValue.Object().Set("error", JsonValue.Make("endpoint desconocido")));
        }

        JsonValue StateJson()
        {
            var j = JsonValue.Object();
            var st = live.State;
            j.Set("blank", JsonValue.Make(st.IsBlank));
            j.Set("blankMode", JsonValue.Make(st.BlankMode));
            var scn = live.CurrentScenario;
            j.Set("scenarioIndex", JsonValue.Make(st.ScenarioIndex));
            j.Set("scenarioTitle", JsonValue.Make(scn != null ? scn.Title : ""));
            j.Set("elementIndex", JsonValue.Make(st.ElementIndex));
            j.Set("line", JsonValue.Make(st.LineIndex));
            var cur = st.Current;
            if (cur != null)
            {
                j.Set("kind", JsonValue.Make(cur.Kind));
                j.Set("lineCount", JsonValue.Make(cur.Lines.Count));
                j.Set("reference", JsonValue.Make(cur.Reference ?? ""));
                string text = cur.Lines.Count > 0 && st.LineIndex < cur.Lines.Count ? cur.Lines[st.LineIndex] : "";
                j.Set("text", JsonValue.Make(text));
            }
            j.Set("project", JsonValue.Make(live.Project != null ? live.Project.Name : ""));
            j.Set("theme", JsonValue.Make(live.Project != null && live.Project.ActiveTheme() != null ? live.Project.ActiveTheme().Name : ""));
            return j;
        }

        static JsonValue ReadBody(HttpListenerContext ctx)
        {
            try
            {
                using (var r = new System.IO.StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
                {
                    string s = r.ReadToEnd();
                    if (string.IsNullOrEmpty(s)) return JsonValue.Object();
                    return JsonValue.Parse(s);
                }
            }
            catch { return JsonValue.Object(); }
        }

        static void WriteJson(HttpListenerContext ctx, int code, JsonValue j)
        {
            byte[] b = Encoding.UTF8.GetBytes(j.ToJsonString());
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.StatusCode = code;
            WriteBytes(ctx, code, b);
        }

        static void WriteBytes(HttpListenerContext ctx, int code, byte[] b)
        {
            try
            {
                ctx.Response.StatusCode = code;
                ctx.Response.ContentLength64 = b.Length;
                ctx.Response.OutputStream.Write(b, 0, b.Length);
            }
            catch { }
            finally
            {
                try { ctx.Response.OutputStream.Close(); } catch { }
            }
        }

        void Log(string m)
        {
            var h = Logged;
            if (h != null) h(m);
        }

        // ---------------------------------------------------------------- /remote
        /// <summary>Página móvil ligera servida por el propio programa [SPEC §8.2].</summary>
        const string RemoteHtml = @"<!doctype html>
<html lang=""es""><head><meta charset=""utf-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>Fusion HP — Remoto</title><style>
body{font-family:system-ui;margin:0;background:#f3f2f1;color:#201f1e}
header{background:#C43E1C;color:#fff;padding:12px 16px;font-weight:600}
main{padding:16px;max-width:640px;margin:auto}
button{font-size:18px;padding:14px;border:0;border-radius:8px;background:#fff;color:#201f1e;
box-shadow:0 1px 3px rgba(0,0,0,.15);margin:4px;min-width:64px}
.big{display:flex;flex-wrap:wrap;gap:6px}
#st{background:#fff;border-radius:8px;padding:12px;margin-bottom:12px;box-shadow:0 1px 3px rgba(0,0,0,.15);min-height:44px}
.acc{background:#C43E1C;color:#fff} .ok{color:#107c10;font-weight:600} .off{color:#605e5c}
</style></head><body>
<header>Fusion HP · Control remoto</header><main>
<div id=""st"">Conectando…</div>
<div class=""big"">
<button onclick=""cmd('prev')"">◀</button><button onclick=""cmd('next')"">▶</button>
<button onclick=""cmd('black')"">Negro</button><button onclick=""cmd('show')"">Mostrar</button>
</div>
<p>Token: <input id=""tk"" style=""font-size:16px;padding:6px;width:200px""></p>
<script>
var TK=location.search.split('token=')[1]||localStorage.getItem('fhp_tk')||'';
if(TK)localStorage.setItem('fhp_tk',TK);
document.getElementById('tk').value=TK;
function cmd(action){
  if(!document.getElementById('tk').value){alert('Escribe el token');return;}
  localStorage.setItem('fhp_tk',document.getElementById('tk').value);
  var url,opts={method:'POST',headers:{'Authorization':'Bearer '+document.getElementById('tk').value}};
  if(action=='next')url='/api/v1/next';
  else if(action=='prev')url='/api/v1/prev';
  else if(action=='black'){fetch('/api/v1/state',{headers:opts.headers}).then(r=>r.json()).then(s=>{
      fetch('/api/v1/goto',{method:'POST',headers:{'Authorization':'Bearer '+document.getElementById('tk').value,
      'Content-Type':'application/json'},body:JSON.stringify({scenario:s.scenarioIndex,element:s.elementIndex,line:s.line})});});return;}
  else url='/api/v1/next';
  fetch(url,opts).then(refresh);
}
function refresh(){
  fetch('/api/v1/state',{headers:{'Authorization':'Bearer '+document.getElementById('tk').value}})
   .then(r=>r.ok?r.json():null).then(s=>{
     var el=document.getElementById('st');
     if(!s){el.innerHTML='<span class=off>Sin token válido</span>';return;}
     el.innerHTML=(s.blank?'<b class=off>(pantalla en reposo)</b>':'')+
       '<b>'+(s.scenarioTitle||'—')+'</b><br>'+
       (s.text?s.text.replace(/</g,'&lt;'):'')+
       ' <span class=off>['+(s.line+1)+'/'+(s.lineCount||1)+']</span>';
   }).catch(()=>{document.getElementById('st').textContent='Sin conexión con el estudio';});
}
setInterval(refresh,1500);refresh();
</script></main></body></html>";
    }
}
