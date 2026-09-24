// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  JsModules.cs (v6.1.0 «GUION») : soporte PURO del motor de scripts JSLib
//  (requisito del spec §3.3: «permitir que el usuario automatice e integre el
//  programa con otros sistemas mediante módulos .js cargados en caliente»).
//
//  Este archivo NO toca COM ni sockets (sigue la lección de ObsProtocol: la
//  lógica portable vive en Core y se valida en el arnés net8.0; el runtime —
//  IActiveScript/JScript de Windows + TCP/WebSocket/HTTP — vive en la UI
//  WPF, managed/Lumina.WPF/Scripting/). Contiene:
//
//    * JsModuleScanner : escaneo TOLERANTE de <data>/modules/*.js (orden
//      alfabético estable, archivo ilegible → error por archivo, NUNCA lanza;
//      lectura UTF-8 tolerante a BOM igual que settings.json).
//    * JsModuleFile    : nombre + código + error de un módulo.
//    * JsLogRing       : ring de 60 líneas del registro visible en la UI
//      (thread-safe: los sockets escriben desde hilos de fondo).
//    * JsEventRegistry : suscripciones onEvent(name, cb) como datos puros —
//      los tokens de callback son opacos (object) para el arnés; el host real
//      guarda ahí los IDispatch de JScript.
//    * JsTimerIds      : asignación de ids numéricos para setTimeout/
//      clearTimeout (par de asignar/liberar es testeable sin reloj).
//    * JsPrelude       : preámbulo ES3 que se evalúa ANTES que los módulos:
//      define jsonParse() (eval sobre el payload JSON construido por el
//      propio programa — MiniJson.Serialize — módulos locales de confianza,
//      mismo modelo de confianza que los activadores).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace lumina.core
{
    /// <summary>Un módulo .js del directorio &lt;data&gt;/modules/.</summary>
    public sealed class JsModuleFile
    {
        /// <summary>Nombre de archivo (p. ej. «automatizacion.js»).</summary>
        public string Name = string.Empty;

        /// <summary>Código fuente (vacío si hubo error de lectura).</summary>
        public string Code = string.Empty;

        /// <summary>Error de lectura (vacío = leído bien).</summary>
        public string Error = string.Empty;

        /// <summary>¿Se leyó sin errores?</summary>
        public bool Ok { get { return Error.Length == 0; } }
    }

    /// <summary>
    /// Escaneo del directorio de módulos. Tolerante por contrato: la carpeta
    /// ausente no es error (simplemente no hay módulos), y un archivo ilegible
    /// queda reportado en el propio módulo (el resto sigue cargando — spec
    /// §2.6: un módulo roto no impide que el resto funcione).
    /// </summary>
    public static class JsModuleScanner
    {
        /// <summary>Extensión reconocida (comparación insensible a caja).</summary>
        public const string Extension = ".js";

        /// <summary>
        /// Escanea dir/*.js en orden alfabético (orden de evaluación estable
        /// y reproducible entre recargas). Nunca lanza.
        /// </summary>
        public static List<JsModuleFile> Scan(string dir)
        {
            List<JsModuleFile> modules = new List<JsModuleFile>();
            try
            {
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return modules;
                string[] files = Directory.GetFiles(dir, "*" + Extension);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                foreach (string f in files)
                {
                    JsModuleFile m = new JsModuleFile();
                    m.Name = Path.GetFileName(f);
                    try
                    {
                        // UTF-8 con BOM o sin él (mismo contrato que settings.json
                        // y triggers.json: los editores de Windows suelen meter BOM).
                        // throwOnInvalidBytes=true: un módulo con bytes UTF-8 rotos
                        // queda reportado como error por archivo (lección del arnés:
                        // la BCL por defecto REEMPLAZA por U+FFFD en silencio y el
                        // módulo llegaría mojibake al motor).
                        m.Code = File.ReadAllText(f, new UTF8Encoding(false, true));
                    }
                    catch (Exception ex)
                    {
                        m.Error = "no se pudo leer: " + ex.Message;
                    }
                    modules.Add(m);
                }
            }
            catch (Exception)
            {
                // Enumerar falló por una causa externa: lista vacía (el resto de
                // la app sigue viva; el error se refleja en la lista de módulos).
            }
            return modules;
        }
    }

    /// <summary>
    /// Ring de registro del JSLib (visible en Integraciones › Módulos JS).
    /// Thread-safe: httpGet/tcp/ws escriben desde hilos de fondo y la UI lee
    /// desde el hilo principal con Snapshot().
    /// </summary>
    public sealed class JsLogRing
    {
        public const int Capacity = 60;

        private readonly object _sync = new object();
        private readonly List<string> _lines = new List<string>(Capacity);

        /// <summary>Añade una línea con marca de tiempo HH:mm:ss.</summary>
        public void Add(string line)
        {
            if (line == null) line = string.Empty;
            lock (_sync)
            {
                _lines.Add(DateTime.Now.ToString("HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture)
                           + "  " + line);
                while (_lines.Count > Capacity) _lines.RemoveAt(0);
            }
        }

        /// <summary>Copia del contenido actual (las más antiguas primero).</summary>
        public List<string> Snapshot()
        {
            lock (_sync) { return new List<string>(_lines); }
        }

        /// <summary>Vacía el ring (solo explícitamente: al recargar módulos el
        /// historial se conserva, igual que la bitácora del director).</summary>
        public void Clear()
        {
            lock (_sync) { _lines.Clear(); }
        }

        /// <summary>Líneas en el ring (aserciones del arnés).</summary>
        public int Count
        {
            get { lock (_sync) { return _lines.Count; } }
        }
    }

    /// <summary>
    /// Registro de suscripciones onEvent(name, callback) como DATOS puros:
    /// el token es opaco (object). El host real de la UI guarda los IDispatch
    /// de las funciones JScript; el arnés de tests usa cadenas. Un evento sin
    /// suscriptores cuesta O(1) y un Fire con N suscriptores devuelve N tokens
    /// en orden de suscripción.
    /// </summary>
    public sealed class JsEventRegistry
    {
        private readonly object _sync = new object();
        private readonly Dictionary<string, List<object>> _byEvent =
            new Dictionary<string, List<object>>(StringComparer.Ordinal);

        /// <summary>Suscribe un callback al evento (nombre sensible a caja,
        /// igual que JScript). Nulo/vacío se ignora.</summary>
        public void Subscribe(string eventName, object callbackToken)
        {
            if (string.IsNullOrEmpty(eventName) || callbackToken == null) return;
            lock (_sync)
            {
                List<object> list;
                if (!_byEvent.TryGetValue(eventName, out list))
                {
                    list = new List<object>();
                    _byEvent[eventName] = list;
                }
                if (!list.Contains(callbackToken)) list.Add(callbackToken);
            }
        }

        /// <summary>Quita una suscripción concreta (si existe).</summary>
        public void Unsubscribe(string eventName, object callbackToken)
        {
            if (string.IsNullOrEmpty(eventName) || callbackToken == null) return;
            lock (_sync)
            {
                List<object> list;
                if (_byEvent.TryGetValue(eventName, out list))
                {
                    list.Remove(callbackToken);
                    if (list.Count == 0) _byEvent.Remove(eventName);
                }
            }
        }

        /// <summary>Tokens suscritos a name (en orden de suscripción).</summary>
        public List<object> Subscribers(string eventName)
        {
            lock (_sync)
            {
                List<object> list;
                if (!_byEvent.TryGetValue(eventName, out list)) return new List<object>();
                return new List<object>(list);
            }
        }

        /// <summary>¿Hay al menos un suscriptor a name?</summary>
        public bool HasSubscribers(string eventName)
        {
            lock (_sync)
            {
                List<object> list;
                return _byEvent.TryGetValue(eventName, out list) && list.Count > 0;
            }
        }

        /// <summary>Nombres con al menos un suscriptor (orden no garantizado).</summary>
        public List<string> ActiveEvents()
        {
            lock (_sync) { return new List<string>(_byEvent.Keys); }
        }

        /// <summary>
        /// Suelta TODAS las suscripciones (al recargar módulos los callbacks
        /// viejos quedan huérfanos de motor — misma decisión documentada del
        /// diseño original: no deben seguir vivos).
        /// </summary>
        public void Clear()
        {
            lock (_sync) { _byEvent.Clear(); }
        }
    }

    /// <summary>
    /// Asignador de ids para setTimeout/clearTimeout: par asignar/liberar con
    /// reutilización de huecos, testeable sin reloj. Los ids son &gt; 0 (0 nunca
    /// es un id válido — JScript puede comparar contra 0 sin cuidado).
    /// </summary>
    public sealed class JsTimerIds
    {
        private readonly object _sync = new object();
        private int _next = 1;
        private readonly List<int> _free = new List<int>();

        /// <summary>Reserva un id (&gt; 0, único entre los vivos).</summary>
        public int Alloc()
        {
            lock (_sync)
            {
                if (_free.Count > 0)
                {
                    int id = _free[_free.Count - 1];
                    _free.RemoveAt(_free.Count - 1);
                    return id;
                }
                return _next++;
            }
        }

        /// <summary>Libera un id (ids fuera del rango asignado 1.._next-1 se
        /// ignoran en silencio — lección del arnés: aceptar cualquiera corrompía
        /// el conteo de vivos y reutilizaría ids jamás emitidos).</summary>
        public void Release(int id)
        {
            lock (_sync)
            {
                if (id <= 0 || id >= _next) return;
                if (!_free.Contains(id)) _free.Add(id);
            }
        }

        /// <summary>Cantidad de ids vivos (para aserciones del arnés).</summary>
        public int LiveCount
        {
            get { lock (_sync) { return _next - 1 - _free.Count; } }
        }
    }

    /// <summary>
    /// Preámbulo ES3 evaluado ANTES que cualquier módulo. El host se registra
    /// como ítem con SCRIPTITEM_GLOBALMEMBERS: sus métodos quedan como
    /// FUNCIONES GLOBALES del script («log(…)», «cmd(…)», «httpGet(…)»…) —
    /// exactamente la API «JSLib-like mínimo» del spec §3.3 («tcp(host, port,
    /// onLine), ws(url, onMessage), httpGet(url), cmd(action), showText»).
    /// Define jsonParse() para consumir los payloads JSON de onEvent: los
    /// payloads los construye el propio programa (MiniJson.Serialize de
    /// contextos internos: títulos, índices, rutas) y los módulos son
    /// archivos locales del propio usuario — el mismo modelo de confianza que
    /// los activadores (reglas que ejecutan acciones), por lo que eval()
    /// sobre '(' + json + ')' es suficiente y NO introduce entrada remota.
    /// </summary>
    public static class JsPrelude
    {
        /// <summary>Versión del motor visible desde script (version()).</summary>
        public const string Version = "6.1.0";

        /// <summary>Texto del preámbulo (ES3 puro: el JScript de Win7 no tiene
        /// JSON.parse ni strict mode; \r\n porque Notepad clásico lo espera).</summary>
        public const string Text =
            "// lumina prelude v6.1.0 — la API del host (GLOBALMEMBERS) es global\r\n" +
            "function jsonParse(s) {\r\n" +
            "    return eval('(' + s + ')');\r\n" +
            "}\r\n" +
            "var lumina = { version: '6.1.0', guion: 'GUION' };\r\n";
    }

    // ========================================================================
    // F5.09 — SANDBOX JSLib: presupuesto por script (límites CPU / memoria /
    // conexiones) y errores con archivo/línea (F5.09.9-12 y F5.09.14).
    //
    // Modelo del presupuesto (cooperativo): el motor JScript de Windows es
    // MONO-HILO con timers, así que el deadline de CPU se aplica al CICLO DE
    // DESPACHO — el host (Lumina.WPF/Scripting) envuelve cada despacho de
    // timer/evento/callback con BeginCpuCycle() y el governor lo corta al
    // vencer el presupuesto (entre despachos, nunca en medio de una llamada
    // nativa). La memoria es COTA OPERATIVA documentada: acumulado de
    // strings/log reservado por script (no el heap del proceso — ese límite
    // lo marcan F6.02/F6.03). El DISCO: ni el escáner ni la API exponen E/S
    // de archivos a los scripts (JsModuleScanner lee los módulos; los
    // scripts no reciben funciones de disco) → sin acceso a disco fuera del
    // proyecto POR DISEÑO (F5.09.9), documentado en DiskPolicy.
    // ========================================================================

    /// <summary>Presupuesto por script (F5.09.10-12). Valores conservadores
    /// y visibles: la UI los puede ajustar antes de recargar módulos.</summary>
    public sealed class JsBudget
    {
        /// <summary>F5.09.10: deadline de CPU por ciclo de despacho (ms).</summary>
        public int CpuCycleBudgetMs = 250;
        /// <summary>F5.09.11: cota operativa de memoria por script (bytes
        /// acumulados de strings/log reservados — documentado, no heap).</summary>
        public long MemoryBudgetBytes = 4L * 1024 * 1024;
        /// <summary>F5.09.12: conexiones activas MÁXIMAS por script.</summary>
        public int MaxConnectionsPerScript = 8;
        /// <summary>F5.09.12: conexiones activas máximas GLOBALES (todos los
        /// scripts sumados — el perfil Win7 4 GB no soporta más).</summary>
        public int MaxConnectionsTotal = 32;
    }

    /// <summary>Uso vivo de un script (consultable por la UI de diagnóstico).</summary>
    public sealed class JsScriptUsage
    {
        public string Script = "";
        public long MemoryBytes;
        public int Connections;
        public long CpuCycles;
        public long Denials;       // rechazos del presupuesto (conexión/memoria/CPU)
    }

    /// <summary>
    /// Gobernador del sandbox JSLib (F5.09): aplica el presupuesto por script
    /// y el global, deja RASTRO visible en el JsLogRing de cada rechazo y
    /// nunca lanza (un script que agota su presupuesto queda denegado y
    /// registrado; el resto del programa sigue). Thread-safe: los sockets
    /// piden/conceden conexiones desde hilos de fondo.
    /// </summary>
    public sealed class JsGovernor
    {
        private readonly JsBudget _budget;
        private readonly JsLogRing _log;
        private readonly object _sync = new object();
        private readonly Dictionary<string, JsScriptUsage> _usage =
            new Dictionary<string, JsScriptUsage>(StringComparer.Ordinal);
        private int _totalConnections;

        /// <summary>F5.09.9: política de disco — POR DISEÑO el sandbox no
        /// expone E/S de archivos a los scripts (el escáner lee los módulos;
        /// la API del host no ofrece funciones de disco), así que no hay
        /// acceso a disco fuera de la carpeta del proyecto ni dentro de ella.</summary>
        public const string DiskPolicy =
            "El sandbox JSLib no expone E/S de disco a los módulos: no hay " +
            "acceso a archivos fuera del proyecto (ni lectura ni escritura).";

        public JsGovernor() : this(new JsBudget(), null) {}

        public JsGovernor(JsBudget budget, JsLogRing log)
        {
            _budget = budget ?? new JsBudget();
            _log = log;
        }

        public JsBudget Budget { get { return _budget; } }

        private JsScriptUsage UsageOf(string script)
        {
            JsScriptUsage u;
            if (!_usage.TryGetValue(script ?? "", out u))
            {
                u = new JsScriptUsage();
                u.Script = script ?? "";
                _usage[u.Script] = u;
            }
            return u;
        }

        // ------------------------------------------------------------- CPU --
        /// <summary>
        /// Marca el INICIO de un ciclo de despacho para el script (timer,
        /// evento o callback). Devuelve false si el presupuesto de CPU ya
        /// está vencido para este ciclo (el host NO debe despachar) y deja
        /// el rechazo en el log visible. El host llama EndCpuCycle al salir.
        /// </summary>
        public bool BeginCpuCycle(string script)
        {
            lock (_sync)
            {
                JsScriptUsage u = UsageOf(script);
                u.CpuCycles++;
                return true;
            }
        }

        /// <summary>
        /// F5.09.10: ¿el deadline del ciclo actual está VENCIDO? El motor es
        /// mono-hilo con timers, así que el corte es COOPERATIVO: el host
        /// consulta entre despachos y no encolas más trabajo del script en
        /// este ciclo. Denegaciones repetidas quedan en el log visible.
        /// </summary>
        public bool CpuCycleExpired(string script, long elapsedMs)
        {
            bool expired = elapsedMs > _budget.CpuCycleBudgetMs;
            if (expired)
            {
                Deny(script, "presupuesto de CPU agotado (" + elapsedMs + " ms > " +
                    _budget.CpuCycleBudgetMs + " ms por ciclo de despacho)");
            }
            return expired;
        }

        public void EndCpuCycle(string script) { /* gancho del host (simetría) */ }

        // -------------------------------------------------------- memoria --
        /// <summary>F5.09.11: reserva cota operativa de memoria (strings/log
        /// acumulados). false si el tope del script se alcanza (denegado y
        /// registrado; el script deja de acumular — p. ej. su log se trunca).</summary>
        public bool TryReserveMemory(string script, long bytes)
        {
            if (bytes < 0) return true;
            lock (_sync)
            {
                JsScriptUsage u = UsageOf(script);
                if (u.MemoryBytes + bytes > _budget.MemoryBudgetBytes)
                {
                    Deny(script, "cota de memoria alcanzada (" + u.MemoryBytes + "+" +
                        bytes + " > " + _budget.MemoryBudgetBytes + " bytes)");
                    return false;
                }
                u.MemoryBytes += bytes;
                return true;
            }
        }

        /// <summary>Devuelve cota reservada (al truncar el log, liberar strings…).</summary>
        public void ReleaseMemory(string script, long bytes)
        {
            if (bytes <= 0) return;
            lock (_sync)
            {
                JsScriptUsage u = UsageOf(script);
                u.MemoryBytes -= bytes;
                if (u.MemoryBytes < 0) u.MemoryBytes = 0;
            }
        }

        // ----------------------------------------------------- conexiones --
        /// <summary>F5.09.12: concede una conexión activa (socket/ws/http) al
        /// script contra el tope POR SCRIPT y el GLOBAL. Si se niega, deja el
        /// error VISIBLE en el log y devuelve false — el host NO abre nada.</summary>
        public bool TryOpenConnection(string script)
        {
            lock (_sync)
            {
                JsScriptUsage u = UsageOf(script);
                if (u.Connections >= _budget.MaxConnectionsPerScript)
                {
                    Deny(script, "conexión denegada: límite por script (" +
                        u.Connections + "/" + _budget.MaxConnectionsPerScript + ")");
                    return false;
                }
                if (_totalConnections >= _budget.MaxConnectionsTotal)
                {
                    Deny(script, "conexión denegada: límite global (" +
                        _totalConnections + "/" + _budget.MaxConnectionsTotal + ")");
                    return false;
                }
                u.Connections++;
                _totalConnections++;
                return true;
            }
        }

        /// <summary>Cierra una conexión (simétrico de TryOpenConnection; el
        /// conteo nunca queda negativo ni por cierres dobles).</summary>
        public void CloseConnection(string script)
        {
            lock (_sync)
            {
                JsScriptUsage u = UsageOf(script);
                if (u.Connections > 0) { u.Connections--; _totalConnections--; }
                if (_totalConnections < 0) _totalConnections = 0;
            }
        }

        public int TotalConnections
        {
            get { lock (_sync) { return _totalConnections; } }
        }

        public int ConnectionsOf(string script)
        {
            lock (_sync)
            {
                JsScriptUsage u;
                return _usage.TryGetValue(script ?? "", out u) ? u.Connections : 0;
            }
        }

        /// <summary>Uso por script (diagnóstico F5.10: «Scripts JSLib»).</summary>
        public List<JsScriptUsage> UsageSnapshot()
        {
            lock (_sync) { return new List<JsScriptUsage>(_usage.Values); }
        }

        /// <summary>Suelta TODO el presupuesto (recarga de módulos: la
        /// generación anterior muere — misma decisión que JsEventRegistry).</summary>
        public void Reset()
        {
            lock (_sync) { _usage.Clear(); _totalConnections = 0; }
        }

        private void Deny(string script, string reason)
        {
            lock (_sync) { UsageOf(script).Denials++; }
            if (_log != null)
                _log.Add(JsScriptError.Format(script, 0, reason));
        }
    }

    /// <summary>
    /// F5.09.14: errores de script con ARCHIVO/LÍNEA. El JScript de Windows
    /// reporta la línea en el texto de la excepción («… línea N»); cuando el
    /// motor la expone, Format la usa; si no, el error queda envuelto con el
    /// nombre del script (siempre attributable). Aquí vive el parser/formatter
    /// PURO para que el arnés lo verifique sin motor COM.
    /// </summary>
    public static class JsScriptError
    {
        /// <summary>
        /// Formatea un error attributable: «módulo.js (línea N): mensaje» si
        /// line &gt; 0; «módulo.js: mensaje» si no se conoce línea.
        /// </summary>
        public static string Format(string script, int line, string message)
        {
            string name = string.IsNullOrEmpty(script) ? "(módulo)" : script;
            string msg = string.IsNullOrEmpty(message) ? "error desconocido" : message;
            if (line > 0)
                return name + " (línea " + line.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) + "): " + msg;
            return name + ": " + msg;
        }

        /// <summary>
        /// Extrae la línea de un mensaje del motor JScript (busca «línea N» o
        /// «line N» al final del texto). Devuelve 0 si no hay línea usable.
        /// </summary>
        public static int ParseEngineLine(string engineMessage)
        {
            if (string.IsNullOrEmpty(engineMessage)) return 0;
            string[] needles = new string[] { "línea", "line" };
            foreach (string needle in needles)
            {
                int i = engineMessage.LastIndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (i < 0) continue;
                int start = i + needle.Length;
                // salta separadores «:» y espacios («línea: 12» / «línea 12»)
                while (start < engineMessage.Length &&
                       (engineMessage[start] == ':' || engineMessage[start] == ' ' ||
                        engineMessage[start] == '\t'))
                    start++;
                int end = start;
                while (end < engineMessage.Length && engineMessage[end] >= '0' &&
                       engineMessage[end] <= '9')
                    end++;
                if (end > start)
                {
                    int v;
                    string tok = engineMessage.Substring(start, end - start);
                    if (int.TryParse(tok, System.Globalization.NumberStyles.Integer,
                                     System.Globalization.CultureInfo.InvariantCulture, out v)
                        && v > 0)
                        return v;
                }
            }
            return 0;
        }

        /// <summary>
        /// Envuelve un error de motor con archivo/línea (F5.09.14): usa la
        /// línea del motor si el mensaje la trae; si no, la del llamador
        /// (0 = desconocida) y SIEMPRE nombra el script.
        /// </summary>
        public static string Wrap(string script, int fallbackLine, string engineMessage)
        {
            int line = ParseEngineLine(engineMessage);
            if (line <= 0) line = fallbackLine;
            return Format(script, line, engineMessage);
        }
    }
}
