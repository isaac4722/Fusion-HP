// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Integrations/PlanningCenterClient.cs : cliente de Planning Center Online
//  (F5.08 — Sección 8.6).
//
//  Reglas del plan:
//   1. Descarga de programas (planes) y 2. de listas.
//   3. Estructura del planificador expuesta (Plan → Items).
//   4. Conversión a Escenarios ahp.v1.
//   5. Manejo de errores EN LENGUAJE HUMANO (jamás stack/HRESULT crudo).
//   6. Funcionamiento core OFFLINE después de importar.
//   7. Sin exigir sincronización cloud para usar el servicio local.
//
//  Autenticación: PAT «app_id:secret» por HTTP Basic (API v2, JSON:API).
//  net35: HttpWebRequest síncrono con timeouts; SIN dependencias nuevas.
//  Base URL inyectable → el arnés prueba contra un servidor local simulado.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using lumina.core;
using lumina.core.project;

// net8 marca HttpWebRequest como obsoleta (SYSLIB0014) pero es LA vía
// portable para net35/net48/net8 con una sola base de código (regla del
// repo: sin dependencias nuevas; HttpClient no existe en net35).
#pragma warning disable SYSLIB0014
namespace lumina.core.integrations
{
    public sealed class PcoPlan
    {
        public string Id = "";
        public string Title = "";
        public DateTime DateUtc;
        public List<PcoItem> Items = new List<PcoItem>();
    }

    public sealed class PcoItem
    {
        public string Id = "";
        public string Title = "";
        public string Kind = "";          // song | item | header | media
        public string Description = "";
        public string SongTitle = "";     // título de la canción (si kind=song)
        public string Arrangement = "";
    }

    /// <summary>Errores humanos (F5.08.5): SIEMPRE accionables, sin tecnicismos.</summary>
    public sealed class PcoException : Exception
    {
        public PcoException(string humanMessage) : base(humanMessage) { }
    }

    public sealed class PlanningCenterClient : IDisposable
    {
        private readonly string _appId;
        private readonly string _secret;
        private readonly string _baseUrl;     // https://api.planningcenteronline.com
        private int _timeoutMs = 15000;

        public PlanningCenterClient(string appId, string appSecret,
                                    string baseUrl = null)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(appSecret))
                throw new PcoException(
                    "Falta el token de acceso a Planning Center. Genere un token personal " +
                    "en su cuenta (id:secret) y configúrelo en Comunicación.");
            _appId = appId;
            _secret = appSecret;
            _baseUrl = string.IsNullOrEmpty(baseUrl)
                ? "https://api.planningcenteronline.com" : baseUrl.TrimEnd('/');
        }

        public int TimeoutMs { get { return _timeoutMs; } set { _timeoutMs = value; } }

        /// <summary>Lista de planes futuros de un tipo de servicio.</summary>
        public List<PcoPlan> GetPlans(string serviceTypeId)
        {
            string json = Get("/services/v2/service_types/" + serviceTypeId +
                              "/plans?order=sort_date&per_page=50");
            return ParsePlans(json);
        }

        /// <summary>Detalle (items) de un plan.</summary>
        public PcoPlan GetPlanItems(string serviceTypeId, string planId)
        {
            string json = Get("/services/v2/service_types/" + serviceTypeId +
                              "/plans/" + planId + "?include=items");
            string items = Get("/services/v2/service_types/" + serviceTypeId +
                               "/plans/" + planId + "/items?per_page=200");
            List<PcoPlan> plans = ParsePlans(json);
            PcoPlan p = plans.Count > 0 ? plans[0] : new PcoPlan { Id = planId };
            p.Items.AddRange(ParseItems(items));
            return p;
        }

        // ------------------------------------------------------------ HTTP --
        private string Get(string path)
        {
            HttpWebRequest rq;
            try
            {
                rq = (HttpWebRequest)WebRequest.Create(_baseUrl + path);
            }
            catch (UriFormatException)
            {
                throw new PcoException("La dirección de Planning Center no es válida.");
            }
            // PAT por HTTP Basic (API v2).
            string cred = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(_appId + ":" + _secret));
            rq.Headers["Authorization"] = "Basic " + cred;
            rq.Timeout = _timeoutMs;
            rq.Accept = "application/json";
            try
            {
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                    return sr.ReadToEnd();
            }
            catch (WebException we)
            {
                HttpWebResponse rs = we.Response as HttpWebResponse;
                if (rs != null && (int)rs.StatusCode == 401)
                    throw new PcoException(
                        "Planning Center rechazó el token. Verifique que el token personal " +
                        "esté activo y tenga permiso sobre Services.");
                if (rs != null && (int)rs.StatusCode == 404)
                    throw new PcoException(
                        "No se encontró el plan o el tipo de servicio en Planning Center.");
                if (we.Status == WebExceptionStatus.Timeout)
                    throw new PcoException(
                        "Planning Center no respondió a tiempo. Verifique su conexión " +
                        "e inténtelo de nuevo.");
                throw new PcoException(
                    "No se pudo conectar con Planning Center. Verifique su conexión de red.");
            }
        }

        // --------------------------------------------------------- parsing --
        private static List<PcoPlan> ParsePlans(string json)
        {
            List<PcoPlan> plans = new List<PcoPlan>();
            Dictionary<string, object> root = MiniJson.Parse(json);
            foreach (object d in MiniJson.GetArray(root, "data"))
            {
                Dictionary<string, object> m = d as Dictionary<string, object>;
                if (m == null) continue;
                PcoPlan p = new PcoPlan();
                p.Id = MiniJson.GetString(m, "id", "");
                Dictionary<string, object> attr = MiniJson.GetObject(m, "attributes");
                if (attr != null)
                {
                    p.Title = MiniJson.GetString(attr, "title", "(sin título)");
                    string dt = MiniJson.GetString(attr, "sorts_at", "");
                    if (dt.Length == 0) dt = MiniJson.GetString(attr, "last_date_at", "");
                    DateTime parsed;
                    if (DateTime.TryParse(dt, null, System.Globalization.DateTimeStyles.RoundtripKind,
                        out parsed)) p.DateUtc = parsed.ToUniversalTime();
                }
                plans.Add(p);
            }
            return plans;
        }

        private static List<PcoItem> ParseItems(string json)
        {
            List<PcoItem> items = new List<PcoItem>();
            Dictionary<string, object> root = MiniJson.Parse(json);
            foreach (object d in MiniJson.GetArray(root, "data"))
            {
                Dictionary<string, object> m = d as Dictionary<string, object>;
                if (m == null) continue;
                PcoItem it = new PcoItem();
                it.Id = MiniJson.GetString(m, "id", "");
                Dictionary<string, object> attr = MiniJson.GetObject(m, "attributes");
                if (attr != null)
                {
                    it.Title = MiniJson.GetString(attr, "title", "");
                    it.Kind = MiniJson.GetString(attr, "item_type", "item");
                    it.Description = MiniJson.GetString(attr, "description", "");
                }
                items.Add(it);
            }
            return items;
        }

        // ------------------------------------------------- conversión ahp --
        /// <summary>F5.08.4: plan → escenario ahp.v1 (offline desde entonces).</summary>
        public static AhpScenario ToAhpScenario(PcoPlan plan, AhpProject project,
                                                Func<string, AhpElement> songMatcher)
        {
            AhpScenario sc = project.NewScenario(
                string.IsNullOrEmpty(plan.Title) ? "Plan PCO" : plan.Title);
            foreach (PcoItem it in plan.Items)
            {
                if (it.Kind == "header") continue;                 // sección visual
                if (it.Kind == "song" && songMatcher != null)
                {
                    AhpElement e = songMatcher(
                        string.IsNullOrEmpty(it.Title) ? it.SongTitle : it.Title);
                    if (e != null) { sc.Elements.Add(e); continue; }
                }
                // ítem no musical → texto con notas (visible al operador).
                AhpElement t = project.NewElement(AhpElementKind.Text);
                t.Title = it.Title;
                StringBuilder sb = new StringBuilder();
                if (!string.IsNullOrEmpty(it.Description)) sb.Append(it.Description);
                if (sb.Length == 0) sb.Append(it.Title);
                t.Lines.Add(new AhpLine { Text = sb.ToString() });
                sc.Elements.Add(t);
            }
            return sc;
        }

        public void Dispose() { }
    }
}

#pragma warning restore SYSLIB0014
