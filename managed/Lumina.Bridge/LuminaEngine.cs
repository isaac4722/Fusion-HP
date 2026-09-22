// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  LuminaEngine.cs : envoltura segura (IDisposable) del LuminaHandle nativo.
//
//  Eventos (nativo → C#):
//   * El motor dispara en un HILO DEDICADO propio. El delegado gestionado se
//     mantiene vivo con campo + GCHandle (nunca lo recolecta el GC) y se pasa
//     como puntero de función estabilizado (GetFunctionPointerForDelegate).
//   * LuminaEvent dispara EN EL HILO DEL MOTOR (contrato: NO llamar al motor
//     re-entrántemente desde ahí) y, si se capturó un SynchronizationContext
//     (UI), se encola además a ese hilo (UiEventReceived).
//   * Tras Dispose: los callbacks en vuelo se IGNORAN (flag volátil) y el orden
//     de apagado es lumina_destroy (join del hilo de eventos) ANTES de liberar
//     el GCHandle → nunca hay callback apuntando a memoria liberada.
//
//  Todo el texto cruza la frontera como byte[] UTF-8; las firmas sin len van
//  terminadas en '\0' manual (ver cada llamada).
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace lumina.bridge
{
    /// <summary>Evento del motor ya decodificado (payload crudo + texto UTF-8 si aplica).</summary>
    public sealed class LuminaEvent
    {
            public readonly int Code;        // LuminaEvents.*
        public readonly byte[] Payload;  // crudo (UTF-8 o PNG en PREVIEW); puede ser null

        internal LuminaEvent(int code, byte[] payload)
        {
            Code = code;
            Payload = payload;
        }

        /// <summary>Payload como texto UTF-8 (vacío para eventos binarios).</summary>
        public string Text
        {
            get
            {
                if (Payload == null || Payload.Length == 0 || Code == LuminaEvents.Preview)
                    return string.Empty;
                return new UTF8Encoding(false).GetString(Payload);
            }
        }

        public string CodeName { get { return LuminaEvents.Name(Code); } }
    }

    /// <summary>Manejador de eventos del motor. Puede ejecutarse en el hilo del motor.</summary>
    public delegate void LuminaEventHandler(object sender, LuminaEvent e);

    /// <summary>Configuración gestionada del motor (espejo de LuminaConfig de lumina.h).</summary>
    public sealed class LuminaConfig
    {
        /// <summary>1 = sin ventanas (tests/API/servicios). Proyección nativa solo Windows.</summary>
        public bool Headless = true;

        /// <summary>structSize lo completa el puente = Marshal.SizeOf del struct nativo.</summary>
        public int StructSize;   // informativo aquí; el valor real lo fusa NativeMethods
    }

    public sealed class LuminaEngine : IDisposable
    {
        private IntPtr _handle;                                  // LuminaHandle
        private NativeMethods.LuminaEventFn _callback;           // RAÍZ del delegado (GC)
        private GCHandle _callbackGuard;                         // segunda red (requerimiento)
        private volatile bool _disposed;
        private readonly SynchronizationContext _uiContext;      // capturado (UI), opcional
        private readonly object _createLock = new object();      // solo Create/Dispose

        /// <summary>Evento EN EL HILO DEL MOTOR (no llamar al motor desde aquí).</summary>
        public event LuminaEventHandler EventReceived;

        /// <summary>Evento encolado al SynchronizationContext capturado (hilo de UI), si se pasó uno.</summary>
        public event LuminaEventHandler UiEventReceived;

        private LuminaEngine(SynchronizationContext uiContext)
        {
            _uiContext = uiContext;
        }

        /* ------------------------------------------------------------- ciclo */

        /// <summary>Crea el motor headless con manejador de eventos (hilo del motor).</summary>
        public static LuminaEngine Create(bool headless, LuminaEventHandler onEvent)
        {
            return Create(headless, onEvent, null);
        }

        /// <summary>
        /// Crea el motor. <paramref name="onEvent"/> recibe los eventos en el hilo del
        /// motor; <paramref name="uiContext"/> (opcional) recibe los mismos eventos vía
        /// Post() (UiEventReceived) — usar el contexto de WinForms en la UI.
        /// </summary>
        public static LuminaEngine Create(bool headless, LuminaEventHandler onEvent,
                                          SynchronizationContext uiContext)
        {
            LuminaEngine engine = new LuminaEngine(uiContext);
            if (onEvent != null) engine.EventReceived += onEvent;
            engine.Start(headless);
            return engine;
        }

        private void Start(bool headless)
        {
            lock (_createLock)
            {
                if (_handle != IntPtr.Zero || _disposed)
                    throw new InvalidOperationException("LuminaEngine ya fue iniciado o liberado.");

                // 1) Delegado enraizado: campo + GCHandle (protección doble).
                _callback = new NativeMethods.LuminaEventFn(OnNativeEvent);
                _callbackGuard = GCHandle.Alloc(_callback);

                // 2) Config binaria: structSize = sizeof(LuminaConfig) — evolución ABI.
                NativeMethods.LuminaConfigNative cfg = new NativeMethods.LuminaConfigNative();
                cfg.StructSize = Marshal.SizeOf(typeof(NativeMethods.LuminaConfigNative));
                cfg.Headless = headless ? 1 : 0;
                cfg.OnEvent = Marshal.GetFunctionPointerForDelegate(_callback);
                cfg.User = IntPtr.Zero; // no usamos contexto nativo; el flag de instancia basta

                // 3) Creación. null del nativo → sin excepciones cruzando la frontera.
                //    Blindaje v4.0.0: los fallos de CARGA de la DLL (ausente,
                //    bitness incorrecto, entrada incompatible) se traducen a
                //    mensajes claros y accionables — nunca un crash críptico.
                IntPtr h;
                try
                {
                    h = NativeMethods.lumina_create(ref cfg);
                }
                catch (DllNotFoundException ex)
                {
                    FreeCallback();
                    throw new LuminaException(LuminaStatus.ErrArg,
                        "No se encontró LuminaCore.dll junto al ejecutable. " +
                        "Extraiga el ZIP completo en una carpeta propia y ejecute " +
                        "LuminaLauncher.exe desde ahí (no desde dentro del ZIP).", ex);
                }
                catch (BadImageFormatException ex)
                {
                    FreeCallback();
                    throw new LuminaException(LuminaStatus.ErrArg,
                        "LuminaCore.dll no corresponde a la arquitectura de este " +
                        "proceso (" + (IntPtr.Size == 4 ? "32" : "64") + " bits). " +
                        "Use el paquete que coincide con su sistema.", ex);
                }
                catch (EntryPointNotFoundException ex)
                {
                    FreeCallback();
                    throw new LuminaException(LuminaStatus.ErrArg,
                        "LuminaCore.dll presente pero incompatible (falta una " +
                        "entrada de la ABI). Mezcla de versiones en la carpeta: " +
                        "vuelva a extraer el paquete completo.", ex);
                }
                catch (TypeInitializationException ex)
                {
                    FreeCallback();
                    throw new LuminaException(LuminaStatus.ErrArg,
                        "No se pudo inicializar el puente nativo (TypeInitialization).",
                        ex);
                }
                if (h == IntPtr.Zero)
                {
                    FreeCallback();
                    throw new LuminaException(LuminaStatus.ErrArg,
                        "lumina_create devolvió null (¿structSize incompatible o sin memoria?).");
                }
                _handle = h;
            }
        }

        /// <summary>Handle nativo crudo (para verificaciones del PoC). Lanza si está liberado.</summary>
        public IntPtr NativeHandle
        {
            get { ThrowIfDisposed(); return _handle; }
        }

        /// <summary>
        /// Libera la raíz del delegado tras un arranque fallido (idempotente):
        /// sin handle no hay callbacks en vuelo posibles.
        /// </summary>
        private void FreeCallback()
        {
            if (_callbackGuard.IsAllocated) _callbackGuard.Free();
            _callback = null;
        }

        public bool IsDisposed { get { return _disposed; } }

        /// <summary>Versión del núcleo ("LuminaCore 3.0.0"). Estático: no necesita handle.</summary>
        public static string Version()
        {
            return BufferHelper.InvokeText(NativeMethods.lumina_version);
        }

        /* ------------------------------------------------ callback del motor */

        // Se ejecuta en el hilo de eventos del MOTOR. Contrato ABI: no llamar al
        // motor re-entrántemente aquí; nunca dejar escapar excepciones hacia C.
        private void OnNativeEvent(IntPtr user, int code, IntPtr payload, int payloadLen)
        {
            if (_disposed) return;                        // tras Dispose: ignorar todo
            LuminaEvent ev;
            try
            {
                byte[] data = null;
                if (payload != IntPtr.Zero && payloadLen > 0)
                {
                    data = new byte[payloadLen];
                    Marshal.Copy(payload, data, 0, payloadLen);
                }
                ev = new LuminaEvent(code, data);
            }
            catch (Exception)
            {
                return; // sin texto → sin evento; jamás propagar hacia el motor
            }

            try
            {
                LuminaEventHandler h = EventReceived;
                if (h != null) h(this, ev);
            }
            catch (Exception)
            {
                // Un manejador defectuoso no debe tumbar el motor.
            }

            SynchronizationContext ctx = _uiContext;
            if (ctx != null)
            {
                try
                {
                    ctx.Post(delegate
                    {
                        if (_disposed) return;            // ignora también tras Dispose
                        LuminaEventHandler h = UiEventReceived;
                        if (h != null) h(this, ev);
                    }, null);
                }
                catch (Exception)
                {
                    // Contexto muerto (form cerrado): silencioso.
                }
            }
        }

        /* ---------------------------------------------------- en vivo (live) */

        /// <summary>Carga un escenario JSON ({"name","theme","items":[…]}). 0 = OK.</summary>
        public int LoadScenario(string json)
        {
            ThrowIfDisposed();
            byte[] b = Utf8(json);
            return NativeMethods.lumina_load_scenario(_handle, b, b.Length);
        }

        public int ShowSlide(int index) { ThrowIfDisposed(); return NativeMethods.lumina_show_slide(_handle, index); }
        public int Next()               { ThrowIfDisposed(); return NativeMethods.lumina_next(_handle); }
        public int Prev()               { ThrowIfDisposed(); return NativeMethods.lumina_prev(_handle); }
        public int Black(bool on)       { ThrowIfDisposed(); return NativeMethods.lumina_black(_handle, on ? 1 : 0); }
        public int Clear()              { ThrowIfDisposed(); return NativeMethods.lumina_clear(_handle); }

        /// <summary>Aplica un tema JSON (fusión parcial con el actual). 0 = OK.</summary>
        public int SetTheme(string json)
        {
            ThrowIfDisposed();
            byte[] b = Utf8(json);
            return NativeMethods.lumina_set_theme(_handle, b, b.Length);
        }

        /// <summary>Estado completo actual (JSON).</summary>
        public string StateJson()
        {
            ThrowIfDisposed();
            IntPtr h = _handle;
            return BufferHelper.InvokeText((byte[] outBuf, int cap, out int needed) =>
                NativeMethods.lumina_state_json(h, outBuf, cap, out needed));
        }

        /// <summary>Latido/eco: dispara un evento PONG con el mensaje textual.</summary>
        public int Ping(string msg)
        {
            ThrowIfDisposed();
            byte[] b = Utf8(msg);
            return NativeMethods.lumina_ping(_handle, b, b.Length);
        }

        /* ------------------------------------------------------- proyección */

        public int ProjectorShow(int screenIndex, bool fullscreen)
        {
            ThrowIfDisposed();
            return NativeMethods.lumina_projector_show(_handle, screenIndex, fullscreen ? 1 : 0);
        }

        public int ProjectorHide() { ThrowIfDisposed(); return NativeMethods.lumina_projector_hide(_handle); }

        /// <summary>PNG de vista previa de la slide (null si el núcleo no puede renderizar, p. ej. headless).</summary>
        public byte[] RenderPreviewPng(int slideIndex)
        {
            ThrowIfDisposed();
            IntPtr h = _handle;
            try
            {
                return BufferHelper.InvokeBytes((byte[] outBuf, int cap, out int needed) =>
                    NativeMethods.lumina_render_preview_png(h, slideIndex, outBuf, cap, out needed));
            }
            catch (LuminaException)
            {
                return null;   // ERR_UNSUPPORTED (headless) o índice inválido → null, no excepción
            }
        }

        /* ---------------------------------------------------- almacenamiento */

        /// <summary>Abre/crea la BD portable (ruta UTF-8 terminada en \0). 0 = OK.</summary>
        public int DbOpen(string path)
        {
            ThrowIfDisposed();
            return NativeMethods.lumina_db_open(_handle, Utf8Z(path));
        }

        public int DbClose() { ThrowIfDisposed(); return NativeMethods.lumina_db_close(_handle); }

        /// <summary>
        /// Ejecuta {"sql":"…","params":[…]} → {"rows":[[…]…],"changes":N,"lastId":N}.
        /// CONTRATO DE EFECTOS: UNA sola llamada nativa (el patrón medir/llamar
        /// de BufferHelper EJECUTARÍA DOS VECES un INSERT/UPDATE/DELETE).
        /// Búfer de 1 MB; si el núcleo reporta ERR_LIMIT, solo se reintenta
        /// cuando la sentencia es de lectura (SELECT/WITH) — los writes nunca
        /// se re-ejecutan (su resultado {"changes","lastId"} es diminuto).
        /// </summary>
        public string DbExec(string sqlJson)
        {
            ThrowIfDisposed();
            IntPtr h = _handle;
            byte[] sql = Utf8Z(sqlJson);

            byte[] buf = new byte[1024 * 1024];
            int needed;
            int st = NativeMethods.lumina_db_exec(h, sql, buf, buf.Length, out needed);
            if (st == LuminaStatus.ErrLimit && IsReadOnlySql(sqlJson))
            {
                int retryCap = needed > 0 ? needed : buf.Length * 4;
                byte[] big = new byte[retryCap];
                st = NativeMethods.lumina_db_exec(h, sql, big, big.Length, out needed);
                if (st == LuminaStatus.Ok) return DecodeText(big, needed);
            }
            if (st != LuminaStatus.Ok)
                throw new LuminaException(st, "lumina_db_exec falló (" +
                    LuminaStatus.Name(st) + ").");
            return DecodeText(buf, needed);
        }

        /// <summary>Sentencia de solo lectura (segura de reintentar).</summary>
        private static bool IsReadOnlySql(string sqlJson)
        {
            string s = sqlJson == null ? string.Empty : sqlJson.TrimStart();
            // Heurística sobre el JSON {"sql":"…"}: localiza el inicio del SQL.
            int i = s.IndexOf("\"sql\"", StringComparison.Ordinal);
            if (i < 0) return false;
            int a = s.IndexOf('"', i + 5);
            if (a < 0 || a + 1 >= s.Length) return false;
            a++; // primer carácter del valor
            while (a < s.Length && (s[a] == ' ' || s[a] == '\t' || s[a] == '\r' || s[a] == '\n')) a++;
            string head = s.Substring(a, Math.Min(10, s.Length - a));
            string lower = head.ToLowerInvariant();
            return lower.StartsWith("select", StringComparison.Ordinal) ||
                   lower.StartsWith("with", StringComparison.Ordinal);
        }

        /// <summary>Decodifica UTF-8 de un búfer con NUL final (needed = len+1).</summary>
        private static string DecodeText(byte[] buf, int needed)
        {
            int len = needed >= 0 && needed <= buf.Length ? needed : buf.Length;
            while (len > 0 && buf[len - 1] == 0) len--;
            if (len == 0) return string.Empty;
            return new UTF8Encoding(false).GetString(buf, 0, len);
        }

        /* ------------------------------------------------- estáticos (sin handle) */

        /// <summary>Valida/normaliza canción JSON → {"ok":1,"slides":[…],"song":{…}}.</summary>
        public static string SongParse(string json)
        {
            byte[] b = Utf8(json);
            return BufferHelper.InvokeText((byte[] outBuf, int cap, out int needed) =>
                NativeMethods.lumina_song_parse(b, b.Length, outBuf, cap, out needed));
        }

        /// <summary>
        /// Analiza un .BIB en memoria. optionsJson: {"maxBytes":N,"encoding":"auto"}
        /// (opcional, NUL-terminado). Devuelve {"ok":1,"verses":N,"books":N,"version":"…",…}.
        /// </summary>
        public static string BibParse(byte[] data, string optionsJson)
        {
            if (data == null || data.Length == 0)
                throw new LuminaException(LuminaStatus.ErrArg, "Datos .BIB vacíos.");
            byte[] opts = string.IsNullOrEmpty(optionsJson) ? null : Utf8Z(optionsJson);
            return BufferHelper.InvokeText((byte[] outBuf, int cap, out int needed) =>
                NativeMethods.lumina_bib_parse(data, data.Length, opts, outBuf, cap, out needed));
        }

        /// <summary>Resuelve "Jn 3:16" → {"book":43,"chapter":3,"verse":16,"name":"Juan",…}.</summary>
        public static string BibleRefResolve(string reference)
        {
            return BufferHelper.InvokeText((byte[] outBuf, int cap, out int needed) =>
                NativeMethods.lumina_bible_ref_resolve(Utf8Z(reference), outBuf, cap, out needed));
        }

        /// <summary>Transpone una línea de acordes ({"line":…}; latin=1 → Do Re Mi).</summary>
        public static string ChordsTranspose(string line, int semitones, bool latin)
        {
            return BufferHelper.InvokeText((byte[] outBuf, int cap, out int needed) =>
                NativeMethods.lumina_chords_transpose(Utf8Z(line), semitones, latin ? 1 : 0,
                                                      outBuf, cap, out needed));
        }

        /* ----------------------------------------------------------- helpers */

        private static byte[] Utf8(string s)
        {
            return new UTF8Encoding(false).GetBytes(s ?? string.Empty);
        }

        private static byte[] Utf8Z(string s)
        {
            byte[] b = new UTF8Encoding(false).GetBytes(s ?? string.Empty);
            byte[] z = new byte[b.Length + 1];
            Array.Copy(b, z, b.Length);   // z[b.Length] = 0 → terminador '\0' manual
            return z;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException("LuminaEngine", "El motor ya fue liberado.");
        }

        /* ----------------------------------------------------------- apagado */

        /// <summary>
        /// Orden contractual de apagado:
        ///   1) flag volátil → los callbacks pasan a ignorarse;
        ///   2) lumina_destroy → el núcleo hace JOIN del hilo de eventos (no hay
        ///      callback en vuelo al volver);
        ///   3) liberar el GCHandle del delegado (ya nadie lo invocará).
        /// Idempotente y seguro ante usos concurrentes.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;                              // 1)
            IntPtr h;
            lock (_createLock)
            {
                h = _handle;
                _handle = IntPtr.Zero;
            }
            if (h != IntPtr.Zero) NativeMethods.lumina_destroy(h);   // 2)
            if (_callbackGuard.IsAllocated) _callbackGuard.Free();   // 3)
            _callback = null;                              // suelta la raíz extra
        }
    }
}
