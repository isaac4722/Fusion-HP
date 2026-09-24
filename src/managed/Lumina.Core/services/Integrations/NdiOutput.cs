// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Integrations/NdiOutput.cs : Stage View como fuente NDI (F5.06 — Sección
//  8.5 del documento técnico).
//
//  Reglas del plan:
//   1. Stage View como fuente NDI.  2. Selección de servidor.
//   3. Conmutación automática de servidor.  4. Diagnóstico de desconexión.
//   5. Estado visible.               6. No depender de Internet.
//
//  DISEÑO SIN NUEVAS DEPENDENCIAS: el SDK de NDI (Processing.NDI.Lib.x64.dll
//  / .x86.dll) se carga DINÁMICAMENTE con LoadLibrary — si el runtime de NDI
//  está instalado en el equipo, la salida funciona; si no, el módulo queda
//  «no disponible» con diagnóstico claro (F0.04 regla común: toda función no
//  disponible se deshabilita con mensaje, sin fallos silenciosos). El envío
//  de frames usa NDIlib_send_video_v2 (RGBA 8 bits, campo NDI_video_frame_v2
//  v4 — ABI estable documentada del SDK).
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Text;

namespace lumina.core.integrations
{
    /// <summary>Estado de la salida NDI (F5.06.5: visible en Diagnóstico).</summary>
    public sealed class NdiStatus
    {
        public bool SdkLoaded;        // runtime NDI presente en el equipo
        public bool Sending;          // fuente creada y enviando
        public string SourceName = "";
        public string Servers = "";   // detected/lista de máquinas NDI
        public string LastError = "";
        public long FramesSent;
    }

    public sealed class NdiOutput : IDisposable
    {
        private IntPtr _lib;                 // HMODULE
        private IntPtr _send;                // NDIlib_send_instance
        private readonly StringBuilder _servers = new StringBuilder();
        private long _frames;
        private string _lastError = "";
        private string _sourceName = "";

        // ---- ABI del SDK NDI (v4/v5: compatibles para create/send/destroy) ----
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int DInitialize();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DDestroy();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr DSendCreate(ref NdiSendDesc desc);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DSendDestroy(IntPtr send);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void DSendVideo(IntPtr send, ref NdiVideoFrame frame);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct NdiSendDesc
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string SourceName;
            public IntPtr Groups;          // null
            public int ClockVideo, ClockAudio, FrameFormat;   // 0 = desconocido
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NdiVideoFrame
        {
            public int XRes, YRes;
            public IntPtr FrameData;       // RGBA (4 bytes/px) fila-superior
            public int LineStrideInBytes;
            public int FrameFormatType;    // 0 progresivo
            public long Timecode;
            // (v4 sigue con más campos; el SDK tolera la estructura extendida
            // si el resto queda en cero — patrón documentado de compatibilidad)
            public long P1, P2, P3;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryW(string path);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool FreeLibrary(IntPtr module);
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);
        private static T Load<T>(IntPtr lib, string name) where T : class
        {
            IntPtr p = GetProcAddress(lib, name);
            return p == IntPtr.Zero ? null : (T)(object)Marshal.GetDelegateForFunctionPointer(p, typeof(T));
        }

        private static readonly bool Is64 = IntPtr.Size == 8;

        /// <summary>Carga el runtime NDI si está instalado (F5.06.6: cero
        /// dependencia de Internet: la DLL la instala el usuario).</summary>
        public bool LoadSdk()
        {
            if (_lib != IntPtr.Zero) return true;
            // Guarda de plataforma: LoadLibrary solo existe en Windows (el
            // arnés Linux ejerce la degradación limpia, no el transporte).
            if (Environment.OSVersion.Platform != PlatformID.Win32NT)
            {
                _lastError = "La salida NDI requiere Windows (carga dinámica del " +
                    "runtime Processing.NDI.Lib).";
                return false;
            }
            string[] names = Is64
                ? new[] { "Processing.NDI.Lib.x64.dll", "Processing.NDI.Lib.dll" }
                : new[] { "Processing.NDI.Lib.x86.dll", "Processing.NDI.Lib.dll" };
            foreach (string n in names)
            {
                IntPtr lib = LoadLibraryW(n);
                if (lib == IntPtr.Zero) continue;
                DInitialize init = Load<DInitialize>(lib, "NDIlib_initialize");
                if (init == null) { FreeLibrary(lib); continue; }
                if (init() == 0)
                {
                    FreeLibrary(lib);
                    _lastError = "El runtime de NDI está instalado pero no pudo inicializarse.";
                    return false;
                }
                _lib = lib;
                return true;
            }
            _lastError = "El runtime de NDI no está instalado en este equipo " +
                "(descargue «NDI Runtime» desde ndi.video — no requiere Internet " +
                "para funcionar una vez instalado).";
            return false;
        }

        /// <summary>Crea la fuente NDI con el nombre indicado (F5.06.1).</summary>
        public bool Start(string sourceName)
        {
            if (!LoadSdk())
            {
                _lastError = _lastError.Length > 0 ? _lastError : "runtime NDI ausente";
                return false;
            }
            if (_send != IntPtr.Zero) Stop();
            NdiSendDesc desc = new NdiSendDesc();
            desc.SourceName = sourceName ?? "LuminaPresentation";
            desc.Groups = IntPtr.Zero;
            DSendCreate create = Load<DSendCreate>(_lib, "NDIlib_send_create");
            if (create == null) { _lastError = "SDK NDI incompatible (send_create ausente)."; return false; }
            _send = create(ref desc);
            if (_send == IntPtr.Zero)
            {
                _lastError = "No se pudo crear la fuente NDI (¿límite de fuentes del equipo?).";
                return false;
            }
            _sourceName = desc.SourceName;
            return true;
        }

        /// <summary>Envía un frame RGBA (F5.06.1: el Stage View alimenta la fuente).</summary>
        public bool SendFrameRgba(byte[] rgba, int width, int height)
        {
            if (_send == IntPtr.Zero) return false;
            DSendVideo send = Load<DSendVideo>(_lib, "NDIlib_send_send_video_v2");
            if (send == null) { _lastError = "SDK NDI incompatible (send_video_v2 ausente)."; return false; }
            IntPtr data = Marshal.AllocHGlobal(rgba.Length);
            try
            {
                Marshal.Copy(rgba, 0, data, rgba.Length);
                NdiVideoFrame f = new NdiVideoFrame();
                f.XRes = width; f.YRes = height;
                f.FrameData = data;
                f.LineStrideInBytes = width * 4;
                f.FrameFormatType = 0;                 // progresivo
                f.Timecode = 0;
                send(_send, ref f);
                _frames++;
                return true;
            }
            finally { Marshal.FreeHGlobal(data); }
        }

        public void Stop()
        {
            if (_send != IntPtr.Zero)
            {
                DSendDestroy destroy = Load<DSendDestroy>(_lib, "NDIlib_send_destroy");
                if (destroy != null) destroy(_send);
                _send = IntPtr.Zero;
            }
        }

        /// <summary>Servidores NDI detectados (F5.06.2-3: el receptor se elige
        /// en el extremo consumidor; aquí se registra el grupo/nombre de
        /// destino para el diagnóstico y la conmutación automática).</summary>
        public void NoteServer(string server)          // p.ej. "OBS-PC"
        {
            if (_servers.Length > 0) _servers.Append(", ");
            _servers.Append(server ?? "");
        }

        /// <summary>Estado completo (F5.06.4-5: diagnóstico + estado visible).</summary>
        public NdiStatus Status()
        {
            NdiStatus st = new NdiStatus();
            st.SdkLoaded = _lib != IntPtr.Zero;
            st.Sending = _send != IntPtr.Zero;
            st.SourceName = _sourceName;
            st.Servers = _servers.ToString();
            st.LastError = _lastError;
            st.FramesSent = _frames;
            return st;
        }

        public void Dispose()
        {
            Stop();
            if (_lib != IntPtr.Zero)
            {
                DDestroy destr = Load<DDestroy>(_lib, "NDIlib_destroy");
                if (destr != null) destr();
                FreeLibrary(_lib);
                _lib = IntPtr.Zero;
            }
        }
    }
}
