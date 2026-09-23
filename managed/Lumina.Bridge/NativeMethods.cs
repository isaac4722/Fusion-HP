// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  NativeMethods.cs : capa cruda de P/Invoke contra LuminaCore (ABI de
//  native/core/include/lumina/lumina.h — CONTRATO, no modificar aquí).
//
//  Reglas de ABI implementadas en esta capa:
//   * Convención Cdecl EN TODAS las entradas (crítico en x86: el limpiado de
//     pila lo hace el llamador; StdCall desalinearía la pila del motor).
//   * SetLastError=false (el núcleo no toca GetLastError) y ExactSpelling=true
//     (en Linux "LuminaCore" resuelve "libLuminaCore.so"; en Windows
//     "LuminaCore.dll" — sin juegos de sufijos).
//   * Todo el texto es UTF-8: SIEMPRE byte[] con Encoding.UTF8 (nunca
//     CharSet.Unicode). Sin 'string' en firmas nativas → cero marshaling
//     ANSI implícito.
//   * Firmas sin parámetro len: la cadena va terminada en '\0' manualmente.
//   * Búfer de salida: patrón uniforme (out, cap, needed) — ver BufferHelper.
//   * Callback: delegado con [UnmanagedFunctionPointer(Cdecl)] mantenido vivo
//     desde LuminaEngine (GCHandle) — jamás recolectado durante el vuelo.
// ============================================================================
using System;
using System.Runtime.InteropServices;

namespace lumina.bridge
{
    /// <summary>Códigos de estado de la ABI (lumina.h). Públicos para que la UI/API los interpreten.</summary>
    public static class LuminaStatus
    {
        public const int Ok            =  0;  // LUMINA_OK
        public const int ErrArg        = -1;  // LUMINA_ERR_ARG
        public const int ErrState      = -2;  // LUMINA_ERR_STATE
        public const int ErrParse      = -3;  // LUMINA_ERR_PARSE
        public const int ErrIo         = -4;  // LUMINA_ERR_IO
        public const int ErrLimit      = -5;  // LUMINA_ERR_LIMIT
        public const int ErrUnsupported= -6;  // LUMINA_ERR_UNSUPPORTED

        public static string Name(int status)
        {
            switch (status)
            {
                case Ok: return "OK";
                case ErrArg: return "ERR_ARG";
                case ErrState: return "ERR_STATE";
                case ErrParse: return "ERR_PARSE";
                case ErrIo: return "ERR_IO";
                case ErrLimit: return "ERR_LIMIT";
                case ErrUnsupported: return "ERR_UNSUPPORTED";
                default: return "ERR(" + status + ")";
            }
        }
    }

    /// <summary>Códigos de evento (lumina.h).</summary>
    public static class LuminaEvents
    {
        public const int Log          = 1;  // LUMINA_EV_LOG
        public const int State        = 2;  // LUMINA_EV_STATE
        public const int SlideChanged = 3;  // LUMINA_EV_SLIDE_CHANGED
        public const int ItemChanged  = 4;  // LUMINA_EV_ITEM_CHANGED
        public const int Error        = 5;  // LUMINA_EV_ERROR
        public const int Pong         = 6;  // LUMINA_EV_PONG
        public const int Preview      = 7;  // LUMINA_EV_PREVIEW

        public static string Name(int code)
        {
            switch (code)
            {
                case Log: return "LOG";
                case State: return "STATE";
                case SlideChanged: return "SLIDE_CHANGED";
                case ItemChanged: return "ITEM_CHANGED";
                case Error: return "ERROR";
                case Pong: return "PONG";
                case Preview: return "PREVIEW";
                default: return "EV(" + code + ")";
            }
        }
    }

    /// <summary>Excepción con el código de estado nativo asociado.</summary>
    [Serializable]
    public sealed class LuminaException : Exception
    {
        public readonly int Status;

        public LuminaException(int status, string message)
            : base(message)
        {
            Status = status;
        }

        public LuminaException(int status, string message, Exception inner)
            : base(message, inner)
        {
            Status = status;
        }

        public override string Message
        {
            get { return base.Message + " (estado " + LuminaStatus.Name(Status) + ")"; }
        }
    }

    /// <summary>
    /// Declaraciones crudas de la DLL. Internas: todo el consumo pasa por
    /// LuminaEngine / BufferHelper (patrón de búfer y encoding centralizados).
    /// </summary>
    internal static class NativeMethods
    {
        /// <summary>
        /// Nombre lógico de la biblioteca. Windows: LuminaCore.dll. Linux: el
        /// cargador prueba "libLuminaCore.so" (y "LuminaCore.so"). macOS: libre.
        /// </summary>
        internal const string LibraryName = "LuminaCore";

        /// <summary>Convención de llamada de TODA la ABI (x86 exige Cdecl).</summary>
        internal const CallingConvention CallConv = CallingConvention.Cdecl;

        /// <summary>Firma del callback de eventos: void (*)(void* user, int32_t code, const char* payload, int32_t len).</summary>
        [UnmanagedFunctionPointer(CallConv)]
        internal delegate void LuminaEventFn(IntPtr user, int code, IntPtr payload, int payloadLen);

        /// <summary>Espejo binario exacto de LuminaConfig (lumina.h): 2×int32 + 2×puntero.</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct LuminaConfigNative
        {
            public int  StructSize;  // structSize = sizeof(LuminaConfig) (evolución ABI)
            public int  Headless;    // 1 = sin ventanas (tests/API/servicios)
            public IntPtr OnEvent;   // callback opcional (hilo dedicado del motor)
            public IntPtr User;      // contexto del cliente (usamos GCHandle)
        }

        /* ---------------------------------------------------------------- ciclo */

        [DllImport(LibraryName, EntryPoint = "lumina_create", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern IntPtr lumina_create(ref LuminaConfigNative cfg);

        [DllImport(LibraryName, EntryPoint = "lumina_destroy", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern void lumina_destroy(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "lumina_version", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_version(byte[] outBuf, int cap, out int needed);

        /* ------------------------------------------------- escenario / en vivo */

        [DllImport(LibraryName, EntryPoint = "lumina_load_scenario", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_load_scenario(IntPtr handle, byte[] json, int len);

        [DllImport(LibraryName, EntryPoint = "lumina_show_slide", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_show_slide(IntPtr handle, int index);

        [DllImport(LibraryName, EntryPoint = "lumina_next", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_next(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "lumina_prev", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_prev(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "lumina_black", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_black(IntPtr handle, int on);

        [DllImport(LibraryName, EntryPoint = "lumina_clear", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_clear(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "lumina_set_theme", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_set_theme(IntPtr handle, byte[] json, int len);

        [DllImport(LibraryName, EntryPoint = "lumina_state_json", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_state_json(IntPtr handle, byte[] outBuf, int cap, out int needed);

        [DllImport(LibraryName, EntryPoint = "lumina_ping", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_ping(IntPtr handle, byte[] msg, int len);

        /* -------------------------------------------- proyección (no headless) */

        [DllImport(LibraryName, EntryPoint = "lumina_projector_show", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_projector_show(IntPtr handle, int screenIndex, int fullscreen);

        [DllImport(LibraryName, EntryPoint = "lumina_projector_hide", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_projector_hide(IntPtr handle);

        // v6.0.0 «HORIZONTE»: transición entre slides (0=corte, 1=fundido).
        [DllImport(LibraryName, EntryPoint = "lumina_set_transition", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_set_transition(IntPtr handle, int mode, int durationMs);

        [DllImport(LibraryName, EntryPoint = "lumina_render_preview_png", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_render_preview_png(IntPtr handle, int slideIndex, byte[] outBuf, int cap, out int needed);

        /* -------------------------------------------------- canciones / biblia */

        [DllImport(LibraryName, EntryPoint = "lumina_song_parse", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_song_parse(byte[] json, int len, byte[] outBuf, int cap, out int needed);

        [DllImport(LibraryName, EntryPoint = "lumina_bib_parse", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_bib_parse(byte[] data, int len, byte[] optionsJson, byte[] outBuf, int cap, out int needed);

        [DllImport(LibraryName, EntryPoint = "lumina_bible_ref_resolve", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_bible_ref_resolve(byte[] refUtf8, byte[] outBuf, int cap, out int needed);

        [DllImport(LibraryName, EntryPoint = "lumina_chords_transpose", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_chords_transpose(byte[] line, int semitones, int latin, byte[] outBuf, int cap, out int needed);

        /* -------------------------------------------------------- almacenamiento */

        [DllImport(LibraryName, EntryPoint = "lumina_db_open", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_db_open(IntPtr handle, byte[] pathUtf8);

        [DllImport(LibraryName, EntryPoint = "lumina_db_close", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_db_close(IntPtr handle);

        [DllImport(LibraryName, EntryPoint = "lumina_db_exec", CallingConvention = CallConv, SetLastError = false, ExactSpelling = true)]
        internal static extern int lumina_db_exec(IntPtr handle, byte[] sqlJson, byte[] outBuf, int cap, out int needed);
    }
}
