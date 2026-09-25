// ============================================================================
//  Fusion-HP · FusionStudio/Services/ApiServer.cs — servidor API local
//  [SPEC §8.1]: HttpListener SOLO en red local, token obligatorio
//  (Authorization: Bearer), 6 endpoints normativos + control remoto móvil
//  servido como página ligera [SPEC §8.2]. Registro de cada petición [§8.1.4].
//  Corrección del prototipo: el modo API funciona (control remoto y estado).
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
            // sin permisos se degrada a localhost (el mando en la misma máquina funciona igual)
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
                    "requiere el prefijo http://+, que Windows reserva a administradores; el mando en este equipo funciona igual.");
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

            // Control remoto móvil [SPEC §8.2] y página de texto plano [SPEC §8.4.3]
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

            // ---- GET /api/v1/text?format=plain|json (texto activo) [SPEC §8.4.3] ----
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

            // ---- POST /api/v1/blank {mode} (B/C/L del mando, referencia web) ----
            if (path == "/api/v1/blank" && method == "POST")
            {
                var body = ReadBody(ctx);
                string mode = body.GetStr("mode", "none");
                if (mode != "none" && mode != "black" && mode != "logo" && mode != "clear")
                    mode = "none";
                live.Blank(mode);
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
            if (cur != null)
            {
                var lines = JsonValue.Array();
                lines.AddStrings(cur.Lines);
                j.Set("lines", lines);
            }
            if (live.Project != null)
            {
                var prog = JsonValue.Array();
                foreach (var sc in live.Project.Scenarios) prog.Add(JsonValue.Make(sc.Title));
                j.Set("program", prog);
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
<title>Fusion HP — Mando</title><style>
body{font-family:system-ui;margin:0;background:#e6e6e6;color:#201f1e}
.phone{max-width:420px;margin:12px auto;background:#fff;border:1px solid #c8c6c4;border-radius:10px;overflow:hidden;box-shadow:0 8px 28px rgba(0,0,0,.18)}
header{display:flex;align-items:center;justify-content:space-between;padding:10px 14px;border-bottom:1px solid #edebe9}
header .t{text-align:center}header .t b{font-size:13px;display:block}header .t span{font-size:11px;color:#605e5c}
.prev{background:#f3f2f1;border:0;border-radius:6px;padding:6px 10px;font-size:16px;cursor:pointer}
#stage{aspect-ratio:16/9;background:#000;margin:12px;position:relative;border:1px solid #e1dfdd;border-radius:4px;overflow:hidden}
#stage .lyr{position:absolute;inset:0;display:flex;flex-direction:column;justify-content:center;padding:6% 8%;text-align:center}
#stage .lyr div{font-size:min(3.4vh,18px);line-height:1.35;color:#fff;text-shadow:0 2px 10px rgba(0,0,0,.6)}
#stage .lyr div.act{color:#e8c872}
#meta{padding:0 16px}#meta .lbl{font-size:11px;color:#605e5c}#meta .cur{font-size:17px;font-weight:600;min-height:22px}
.grid2{display:grid;grid-template-columns:1fr 1fr;gap:8px;padding:12px 16px}
.grid3{display:grid;grid-template-columns:1fr 1fr 1fr;gap:8px;padding:0 16px 12px}
button.ctl{font-size:15px;padding:13px 8px;border:1px solid #e1dfdd;border-radius:6px;background:#fff;cursor:pointer;text-align:left}
button.ctl small{display:block;font-size:10px;opacity:.65}
button.pri{background:#C43E1C;color:#fff;border-color:#C43E1C;font-weight:600}
button.on{background:#fde7e9;border-color:#a4262c;color:#a4262c}
#list{max-height:34vh;overflow:auto;padding:0 8px 12px}
#list button{display:block;width:100%;text-align:left;margin:2px 0;padding:8px 10px;border:0;border-radius:6px;background:transparent;font-size:14px;cursor:pointer}
#list button.sel{background:#f3e9e4;color:#8a1f11;font-weight:600}
#list button i{font-style:normal;color:#6f6e6d;font-size:11px;display:inline-block;width:22px}
#tk{margin:8px 16px 4px;width:calc(100% - 32px);font-size:15px;padding:7px;border:1px solid #c8c6c4;border-radius:5px}
h6{margin:6px 16px;color:#605e5c;font-size:11px;text-transform:uppercase}
</style></head><body>
<div class=""phone"">
<header>
  <button class=""prev"" onclick=""exit()"">&#8249;</button>
  <div class=""t""><b>Mando</b><span>Sincronizado con este equipo</span></div>
  <span style=""width:34px""></span>
</header>
<div id=""stage""><div class=""lyr"" id=""lyr""></div></div>
<div id=""meta""><div class=""lbl"" id=""item"">—</div><div class=""cur"" id=""cur""></div></div>
<div class=""grid2"">
  <button class=""ctl"" onclick=""cmd('prev')"">Anterior<small>&larr;</small></button>
  <button class=""ctl pri"" onclick=""cmd('next')"">Siguiente<small>&rarr; / Espacio</small></button>
</div>
<div class=""grid3"">
  <button class=""ctl"" id=""bb"" onclick=""blank('black')"">Negro<small>B</small></button>
  <button class=""ctl"" id=""bc"" onclick=""blank('clear')"">Limpiar<small>C</small></button>
  <button class=""ctl"" id=""bl"" onclick=""blank('logo')"">Logo<small>L</small></button>
</div>
<h6>Programa</h6>
<div id=""list""></div>
<input id=""tk"" placeholder=""Token de emparejamiento"">
</div>
<script>
var TK=location.search.split('token=')[1]||localStorage.getItem('fhp_tk')||'';
document.getElementById('tk').value=TK;
if(TK)localStorage.setItem('fhp_tk',TK);
function H(){return{'Authorization':'Bearer '+document.getElementById('tk').value}}
function exit(){location.href='/remote';}
function cmd(a){post(a=='next'?'/api/v1/next':'/api/v1/prev');}
function blank(m){
  var el=document.getElementById(m=='black'?'bb':m=='clear'?'bc':'bl');
  post('/api/v1/blank', el.dataset.on=='1' ? 'none' : m);
}
function post(url,body){
  var o={method:'POST',headers:Object.assign({'Content-Type':'application/json'},H())};
  if(body)o.body=JSON.stringify({mode:body});
  fetch(url,o).then(function(r){if(r.ok)refresh();else document.getElementById('cur').textContent='Token no válido';}).catch(function(){});
}
function esc(t){return (t||'').replace(/&/g,'&amp;').replace(/</g,'&lt;');}
function refresh(){
  fetch('/api/v1/state',{headers:H()}).then(function(r){return r.ok?r.json():null}).then(function(s){
    if(!s)return;
    var lyr=document.getElementById('lyr');
    lyr.innerHTML=(s.lines&&s.lines.length?s.lines.map(function(l,i){return '<div'+(i==s.line?' class=""act""':'')+'>'+esc(l)+'</div>';}).join(''):'<div>'+(s.blank?'(pantalla en reposo)':'—')+'</div>');
    document.getElementById('item').textContent=s.scenarioTitle||'—';
    document.getElementById('cur').textContent=s.text||'';
    document.getElementById('bb').dataset.on=s.blankMode=='black'?1:0;
    document.getElementById('bc').dataset.on=s.blankMode=='clear'?1:0;
    document.getElementById('bl').dataset.on=s.blankMode=='logo'?1:0;
    document.getElementById('bb').className='ctl'+(s.blankMode=='black'?' on':'');
    document.getElementById('bc').className='ctl'+(s.blankMode=='clear'?' on':'');
    document.getElementById('bl').className='ctl'+(s.blankMode=='logo'?' on':'');
    var list=document.getElementById('list');
    if(s.program&&s.program.length){
      list.innerHTML=s.program.map(function(p,i){return '<button class=""'+(i==s.scenarioIndex?'sel':'')+'"" onclick=""goto('+i+')""><i>'+(i+1)+'</i>'+esc(p)+'</button>';}).join('');
    } else list.innerHTML='';
  }).catch(function(){});
  fetch('/api/v1/text?format=json',{headers:H()}).then(function(r){return r.ok?r.json():null}).then(function(t){
    if(t&&t.text)document.getElementById('cur').textContent=t.text;
  }).catch(function(){});
}
function goto(i){fetch('/api/v1/goto',{method:'POST',headers:Object.assign({'Content-Type':'application/json'},H()),body:JSON.stringify({scenario:i,element:0,line:0})}).then(refresh);}
setInterval(refresh,1500);refresh();
</script></body></html>
";
    }
}
