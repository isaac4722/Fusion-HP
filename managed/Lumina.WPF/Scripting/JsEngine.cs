// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  JsEngine.cs (v6.1.0 «GUION») : el orquestador del motor de scripts JSLib
//  (adaptación del diseño original de docs/api/JsEngine.md a la arquitectura
//  real: IActiveScript/JScript de Windows hospedado en la UI WPF, no QJSEngine).
//
//  Ciclo de vida (misma filosofía documentada del diseño original):
//    * LoadModules(dir)  : escanea <data>/modules/*.js, evalúa prelude+modules
//      en un motor NUEVO (recargar destruye host+motor: los callbacks viejos
//      quedan huérfanos y las conexiones se cierran — intención documentada).
//    * FireEvent(name,json): retransmite eventos del presentador (los mismos
//      que los activadores) a los callbacks onEvent.
//    * CallGlobal(name,args): invoca una función global del script (acción de
//      activador «script» y gate del selfcheck).
//    * Dispose(): suelta callbacks/conexiones ANTES de cerrar el motor (orden
//      seguro — nunca invocar JS de un motor muerto).
//
//  El orden de arranque del motor es el canónico de IActiveScript:
//    SetScriptSite → InitNew → AddNamedItem(«jslib») → Started →
//    ParseScriptText(prelude + módulos) → Connected → GetScriptDispatch.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Threading;
using lumina.core;

namespace lumina.wpf.scripting
{
    /// <summary>Motor JSLib: hospedaje de IActiveScript/JScript + módulos .js.</summary>
    public sealed class JsEngine : IDisposable
    {
        private readonly Dispatcher _ui;                 // hilo del motor (STA)
        private readonly IJsLibSink _sink;               // cmd/showText/notify
        private readonly JsLogRing _log = new JsLogRing();   // persiste entre recargas

        // Estado POR GENERACIÓN (muere con cada recarga).
        private JsEventRegistry _events;
        private JsLibHost _host;
        private IActiveScript _com;
        // IActiveScriptParse por ARQUITECTURA (IID distinta en x86/x64 — ver
        // la lección del primer run del tag en ActiveScriptInterop.cs): solo
        // una de las dos queda viva (la que acepte el QueryInterface).
        private IActiveScriptParse32 _parse32;
        private IActiveScriptParse64 _parse64;
        private ActiveScriptSite _site;
        private object _globals;                        // IDispatch del global

        private readonly List<string> _loaded = new List<string>();
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _itemInfoTrace = new List<string>();   // diagnóstico
        private string _modulesDir;
        private string _currentFile;                    // atribución de OnScriptError
        private bool _pendingError;                     // OnScriptError ya reportó
        private bool _disposed;

        private JsEngine(Dispatcher ui, IJsLibSink sink)
        {
            _ui = ui;
            _sink = sink;
        }

        /// <summary>Crea el motor sin cargar módulos (LoadModules los trae).</summary>
        public static JsEngine Create(Dispatcher ui, IJsLibSink sink)
        {
            if (ui == null) throw new ArgumentNullException("ui");
            return new JsEngine(ui, sink);
        }

        // ------------------------------------------------------------ consultas

        /// <summary>Registro visible (ring de 60, thread-safe).</summary>
        public JsLogRing Log { get { return _log; } }

        /// <summary>¿Hay un motor vivo (generación actual)?</summary>
        public bool IsAlive { get { return _com != null && !_disposed; } }

        /// <summary>Nombres de archivo de los módulos cargados (copia).</summary>
        public List<string> LoadedModules()
        {
            lock (_loaded) { return new List<string>(_loaded); }
        }

        /// <summary>Errores «archivo:línea — mensaje» (copia).</summary>
        public List<string> Errors()
        {
            lock (_errors) { return new List<string>(_errors); }
        }

        /// <summary>Carpeta de módulos en uso.</summary>
        public string ModulesDir { get { return _modulesDir; } }

        /// <summary>Traza de las llamadas GetItemInfo del motor (nombre +
        /// máscara pedida — diagnóstico del interop, impresa por el selfcheck).</summary>
        public List<string> GetItemInfoTrace()
        {
            lock (_itemInfoTrace) { return new List<string>(_itemInfoTrace); }
        }

        // ------------------------------------------------------------- módulos

        /// <summary>
        /// Escanea dir/*.js y los evalúa en un motor NUEVO. Devuelve false si
        /// algún módulo falló (los válidos siguen cargados — spec §2.6). Todo
        /// ocurre en el hilo de UI (el motor COM es apartamento-hilo).
        /// </summary>
        public bool LoadModules(string dir)
        {
            if (_disposed) return false;
            if (dir != null) _modulesDir = dir;
            CheckThread();

            ShutdownGeneration();
            _loaded.Clear();
            _errors.Clear();

            try
            {
                _com = ActiveScriptInterop.CreateJScriptEngine();
                _parse32 = _com as IActiveScriptParse32;
                _parse64 = _com as IActiveScriptParse64;
                if (_parse32 == null && _parse64 == null)
                {
                    throw new InvalidOperationException(
                        "El motor JScript no expone IActiveScriptParse (x86/x64).");
                }
                _events = new JsEventRegistry();
                _host = new JsLibHost(this, _sink, _ui);
                _site = new ActiveScriptSite(this, _host);

                _com.SetScriptSite(_site);
                if (_parse32 != null) _parse32.InitNew(); else _parse64.InitNew();
                _com.SetScriptState(ScriptState.Started);
                // AddNamedItem EN STARTED + GLOBALMEMBERS (quinto experimento del
                // ciclo, ver jscript.c de Wine): el motor vincula el ítem
                // ANSIOOSAMENTE — GetItemInfo(IUNKNOWN)+QI(IDispatch) durante el
                // PROPIO AddNamedItem — y los miembros quedan como globales del
                // script además del nombre («jslib.xxx» / «xxx()»).
                _com.AddNamedItem("jslib",
                    ScriptItem.IsVisible | ScriptItem.GlobalMembers | ScriptItem.IsPersistent);

                // Prelude (jsonParse/lumina) — SIN referencias a ítems: se puede
                // ejecutar ya (estado STARTED).
                ParseText(JsPrelude.Text, "(prelude)");

                // LECCIÓN DEL TERCER RUN DEL TAG: JScript vincula los ítems con
                // nombre en la transición STARTED→CONNECTED (es cuando el motor
                // materializa el envoltorio llamando a GetItemInfo). El código de
                // nivel superior que referencia «jslib» ejecutado DURANTE el parse
                // en STARTED ve un ítem aún sin envolver (miembros no-función →
                // «Function expected»). Los módulos se parsean CONECTADOS — la
                // inyección dinámica post-conexión es un patrón legítimo de
                // IActiveScriptParse (igual que WSH ejecuta el código de nivel
                // superior después de conectar el motor).
                _com.SetScriptState(ScriptState.Connected);

                List<JsModuleFile> modules = JsModuleScanner.Scan(_modulesDir);
                foreach (JsModuleFile m in modules)
                {
                    if (!m.Ok)
                    {
                        _errors.Add(m.Name + " — " + m.Error);
                        continue;
                    }
                    ParseText(m.Code, m.Name);
                }

                // GetScriptDispatch con el NOMBRE DEL ÍTEM: el código se compila
                // dentro del ítem (pstrItemName) — sus funciones y variables
                // (incluida la «jslib» del preámbulo) viven en el espacio del
                // ítem, no en el global anónimo.
                object globals;
                _com.GetScriptDispatch("jslib", out globals);
                _globals = globals;

                _log.Add("módulos cargados: " + _loaded.Count
                    + (_errors.Count > 0 ? " · errores: " + _errors.Count : string.Empty));
                return _errors.Count == 0;
            }
            catch (Exception ex)
            {
                _errors.Add("(motor) " + ex.Message);
                _log.Add("falló el arranque del motor JSLib: " + ex.Message);
                ShutdownGeneration();
                return false;
            }
        }

        /// <summary>Recarga (motor nuevo, conexiones cerradas por diseño).</summary>
        public bool Reload()
        {
            return LoadModules(_modulesDir);
        }

        private void ParseText(string code, string file)
        {
            _currentFile = file;
            _pendingError = false;
            try
            {
                // pstrItemName = "jslib": el código compila DENTRO del ítem — el
                // motor resuelve el ítem al parsear (GetItemInfo+QI, ver
                // ParseScriptText→lookup_named_item de jscript.c de Wine) y el
                // «this» de nivel superior del código es el objeto host. El
                // preámbulo fija «var jslib = this;» y los módulos llaman
                // jslib.log(...) sobre ese valor.
                // flags 0: el texto se EJECUTA durante la llamada.
                if (_parse32 != null)
                {
                    _parse32.ParseScriptText(code, "jslib", IntPtr.Zero, null,
                        UIntPtr.Zero, 1, 0, IntPtr.Zero, IntPtr.Zero);
                }
                else
                {
                    _parse64.ParseScriptText(code, "jslib", IntPtr.Zero, null,
                        UIntPtr.Zero, 1, 0, IntPtr.Zero, IntPtr.Zero);
                }
                if (file.IndexOf("(prelude)", StringComparison.Ordinal) < 0)
                {
                    lock (_loaded) { _loaded.Add(file); }
                }
            }
            catch (Exception ex)
            {
                // OnScriptError ya reportó con archivo:línea; esto cubre motores
                // que no llamen al sitio (fallback con la misma forma).
                if (!_pendingError)
                {
                    lock (_errors)
                    {
                        _errors.Add(file + " — " + ex.GetType().Name + ": " + ex.Message);
                    }
                }
            }
            finally
            {
                _currentFile = null;
                _pendingError = false;
            }
        }

        // ------------------------------------------------------------- eventos

        /// <summary>
        /// Retransmite un evento a los callbacks onEvent. Thread-safe: si el
        /// llamador no está en el hilo de UI, se despacha allí (el motor COM
        /// es apartamento-hilo). payloadJson: null → "{}".
        /// </summary>
        public void FireEvent(string name, string payloadJson)
        {
            if (_disposed || _events == null || !_events.HasSubscribers(name)) return;
            string payload = payloadJson == null ? "{}" : payloadJson;
            if (_ui == null) return;
            if (!_ui.CheckAccess())
            {
                try
                {
                    _ui.BeginInvoke((Action)(delegate { FireEvent(name, payload); }));
                }
                catch (Exception) { }
                return;
            }
            CheckThread();
            foreach (object cb in _events.Subscribers(name))
            {
                if (_host != null) _host.InvokeCallback(cb, new object[] { payload });
            }
        }

        /// <summary>Invoca una función global del script (null si no existe o
        /// el script falló — el error queda en el registro).</summary>
        public object CallGlobal(string name, params object[] args)
        {
            if (_disposed || _globals == null || string.IsNullOrEmpty(name)) return null;
            CheckThread();
            try
            {
                return _globals.GetType().InvokeMember(name,
                    BindingFlags.InvokeMethod, null, _globals,
                    args == null ? new object[0] : args, null);
            }
            catch (COMException ex)
            {
                // 0x80020006 DISP_E_UNKNOWNNAME: función no definida — esperable.
                const int DispUnknownName = unchecked((int)0x80020006);
                if (ex.HResult != DispUnknownName && (ex.ErrorCode != DispUnknownName))
                {
                    _log.Add("script «" + name + "» falló: " + ex.Message);
                }
                return null;
            }
            catch (Exception ex)
            {
                _log.Add("script «" + name + "» falló: " + ex.Message);
                return null;
            }
        }

        // ------------------------------------------------------------ interno

        /// <summary>Escribe en el registro (host + site).</summary>
        internal void LogLine(string line)
        {
            if (line == null) return;
            _log.Add(line);
        }

        /// <summary>Suscribe un callback (host.onEvent → registro de generación).</summary>
        internal void SubscribeEvent(string name, object callbackToken)
        {
            if (_disposed || _events == null) return;
            _events.Subscribe(name, callbackToken);
        }

        /// <summary>Reporte del sitio COM: error de sintaxis/ejecución con
        /// archivo y línea (llamado por el motor en el hilo de UI).</summary>
        internal void OnScriptErrorReported(string file, uint line, string description)
        {
            _pendingError = true;
            string where = string.IsNullOrEmpty(file) ? "(en ejecución)" : file;
            lock (_errors)
            {
                _errors.Add(where + ":" + line + " — " + description);
            }
            _log.Add("error de script en " + where + ":" + line + " — " + description);
        }

        /// <summary>Traza del sitio: el motor pidió el ítem «name» con máscara
        /// «mask» (SCRIPTINFO_IUNKNOWN=1 · SCRIPTINFO_ITYPEINFO=2).</summary>
        internal void OnGetItemInfoTrace(string name, uint mask)
        {
            lock (_itemInfoTrace)
            {
                _itemInfoTrace.Add(name + ":0x" + mask.ToString("X",
                    System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        /// <summary>ITypeInfo del host para GetItemInfo(SCRIPTINFO_ITYPEINFO):
        /// se obtiene del IDispatch del CCW (AutoDual garantiza que el CCW
        /// publica TypeInfo — es lo que JScript necesita para vincular los
        /// miembros; sin esto el ítem queda «unknown»/opaco).</summary>
        internal static IntPtr GetHostTypeInfo(JsLibHost host)
        {
            IntPtr disp = Marshal.GetIDispatchForObject(host);
            try
            {
                IDispatchInfo d = (IDispatchInfo)Marshal.GetObjectForIUnknown(disp);
                IntPtr ti;
                d.GetTypeInfo(0, 0, out ti);
                if (ti == IntPtr.Zero)
                    throw new COMException("el CCW no publicó ITypeInfo",
                        unchecked((int)0x8002802B));
                return ti;   // AddRef hecho por GetTypeInfo: el motor la libera
            }
            finally
            {
                Marshal.Release(disp);
            }
        }

        /// <summary>Cierra la generación actual (host+motor) en orden seguro:
        /// primero conexiones/callbacks (huérfanos), después el motor COM.</summary>
        private void ShutdownGeneration()
        {
            if (_host != null)
            {
                try { _host.Shutdown(); }
                catch (Exception) { }
            }
            if (_events != null) _events.Clear();
            _globals = null;
            _site = null;
            if (_com != null)
            {
                try { _com.SetScriptState(ScriptState.Disconnected); }
                catch (Exception) { }
                try { _com.Close(); }
                catch (Exception) { }
            }
            _com = null;
            _parse32 = null;
            _parse64 = null;
            _host = null;
        }

        /// <summary>Dispose final: cierra la generación y marca el motor muerto.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ui == null || _ui.CheckAccess())
            {
                ShutdownGeneration();
            }
            else
            {
                try { _ui.BeginInvoke((Action)(delegate { ShutdownGeneration(); })); }
                catch (Exception) { ShutdownGeneration(); }
            }
        }

        /// <summary>El motor COM debe usarse desde SU hilo (apartamento): las
        /// violaciones se detectan ruidosamente en el gate, no en silencio.</summary>
        private void CheckThread()
        {
            if (_ui != null && !_ui.CheckAccess())
            {
                throw new InvalidOperationException(
                    "JsEngine usado desde un hilo ajeno al motor (apartamento-hilo).");
            }
        }

        // =============================================================== sitio

        /// <summary>
        /// IActiveScriptSite del motor: entrega el host «jslib» por GetItemInfo
        /// y reporta los errores con posición. Implementación explícita de la
        /// interfaz (los métodos NO quedan visibles en la clase administrada).
        /// </summary>
        [ComVisible(true)]
        private sealed class ActiveScriptSite : IActiveScriptSite
        {
            private readonly JsEngine _owner;
            private readonly JsLibHost _host;

            internal ActiveScriptSite(JsEngine owner, JsLibHost host)
            {
                _owner = owner;
                _host = host;
            }

            void IActiveScriptSite.GetLCID(out uint lcid)
            {
                lcid = 0;
                // E_NOTIMPL documentado: el motor usa el locale del sistema.
                throw new COMException("GetLCID no implementado", unchecked((int)0x80004001));
            }

            void IActiveScriptSite.GetItemInfo(string name, uint mask, out IntPtr item, out IntPtr typeInfo)
            {
                item = IntPtr.Zero;
                typeInfo = IntPtr.Zero;
                _owner.OnGetItemInfoTrace(name, mask);
                if (string.Equals(name, "jslib", StringComparison.Ordinal))
                {
                    // IUNKNOWN → IDispatch del CCW (AddRef'd; el motor libera).
                    if ((mask & ScriptInfo.IUnknown) != 0)
                    {
                        item = Marshal.GetIDispatchForObject(_host);
                    }
                    // ITYPEINFO → ITypeInfo del CCW: JScript es un compilador
                    // ESTÁTICO — sin TypeInfo el ítem queda «unknown» y sus
                    // miembros no son funciones (cuarto run del tag).
                    if ((mask & ScriptInfo.ITypeInfo) != 0)
                    {
                        try { typeInfo = JsEngine.GetHostTypeInfo(_host); }
                        catch (Exception) { /* sin typeinfo: el ítem sigue entregándose */ }
                    }
                    if (item != IntPtr.Zero || typeInfo != IntPtr.Zero) return;
                }
                throw new COMException("ítem no disponible: " + name,
                    unchecked((int)0x8002802B));   // TYPE_E_ELEMENTNOTFOUND
            }

            void IActiveScriptSite.GetDocVersionString(out string version)
            {
                version = null;
                throw new COMException("GetDocVersionString no implementado",
                    unchecked((int)0x80004001));
            }

            void IActiveScriptSite.OnScriptTerminate(IntPtr varResult, IntPtr excepInfo)
            {
            }

            void IActiveScriptSite.OnStateChange(ScriptState state)
            {
                // Informativo (el ciclo lo lleva JsEngine); registrado a nivel
                // debug en el ring no sería útil: genera ruido por recarga.
            }

            void IActiveScriptSite.OnScriptError(IActiveScriptError scriptError)
            {
                string description = "(sin descripción)";
                uint line = 0;
                try
                {
                    ExcepInfo info = new ExcepInfo();
                    scriptError.GetExceptionInfo(ref info);
                    description = BstrToString(info.bstrDescription);
                    FreeBstr(info.bstrSource);
                    FreeBstr(info.bstrDescription);
                    FreeBstr(info.bstrHelpFile);
                    if (description.Length == 0 && info.scode != 0)
                    {
                        description = "código 0x" + info.scode.ToString("X8",
                            System.Globalization.CultureInfo.InvariantCulture);
                    }
                }
                catch (Exception) { }
                try
                {
                    uint cookie;
                    int charPos;
                    scriptError.GetSourcePosition(out cookie, out line, out charPos);
                }
                catch (Exception) { }
                _owner.OnScriptErrorReported(_owner._currentFile, line, description);
            }

            void IActiveScriptSite.OnEnterScript() { }
            void IActiveScriptSite.OnLeaveScript() { }

            private static string BstrToString(IntPtr bstr)
            {
                if (bstr == IntPtr.Zero) return string.Empty;
                try { return Marshal.PtrToStringBSTR(bstr); }
                catch (Exception) { return string.Empty; }
            }

            private static void FreeBstr(IntPtr bstr)
            {
                if (bstr != IntPtr.Zero)
                {
                    try { Marshal.FreeBSTR(bstr); } catch (Exception) { }
                }
            }
        }
    }
}
