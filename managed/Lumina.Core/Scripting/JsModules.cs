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
    /// Preámbulo ES3 evaluado ANTES que cualquier módulo. Los módulos se
    /// parsean con contexto de ítem («jslib»): el código compila DENTRO del
    /// ítem y su «this» de nivel superior es el objeto host — el preámbulo lo
    /// fija como global «jslib» (patrón del contexto de ítem de WSH). Define
    /// jsonParse() para consumir los payloads JSON de onEvent: los payloads
    /// los construye el propio programa (MiniJson.Serialize de contextos
    /// internos: títulos, índices, rutas) y los módulos son archivos locales
    /// del propio usuario — el mismo modelo de confianza que los activadores
    /// (reglas que ejecutan acciones), por lo que eval() sobre '(' + json +
    /// ')' es suficiente y NO introduce entrada remota.
    /// </summary>
    public static class JsPrelude
    {
        /// <summary>Versión del motor visible desde script (lumina.version).</summary>
        public const string Version = "6.1.0";

        /// <summary>Texto del preámbulo (ES3 puro: el JScript de Win7 no tiene
        /// JSON.parse ni strict mode; \r\n porque Notepad clásico lo espera).</summary>
        public const string Text =
            "// lumina prelude v6.1.0 — el host entra como «this» del contexto del ítem\r\n" +
            "var jslib = this;\r\n" +
            "function jsonParse(s) {\r\n" +
            "    return eval('(' + s + ')');\r\n"
            +
            "}\r\n" +
            "var lumina = { version: '6.1.0', guion: 'GUION' };\r\n";
    }
}
