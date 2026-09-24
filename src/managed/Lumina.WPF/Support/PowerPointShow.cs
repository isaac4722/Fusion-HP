// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PowerPointShow.cs : v7.1.0 «OPERADOR» (feedback #6).
//  Proyecta el archivo PPTX ORIGINAL — SIN extracción — delegando la
//  reproducción a PowerPoint real vía automatización COM con LATE BINDING
//  (Type.GetTypeFromProgID + InvokeMember: CERO referencias nuevas en el
//  ensamblado — la restricción del paquete queda intacta).
//
//  Modelo (como Holyrics con presentaciones):
//    · Play(path, monitor): abre la presentación OCULTA, lanza el modo
//      KIOSCO (fullscreen sin controles) y recoloca la ventana del show
//      EXACTAMENTE sobre el monitor del proyector (SetWindowPos + TOPMOST).
//    · El operador navega dentro de PowerPoint (la ventana es el show);
//      NextSlide/PrevSlide reenvían View.Next/Previous si se quiere desde
//      la app. Al salir del ítem (cambio de slide en vivo) → Stop().
//    · PowerPoint no instalado → false y el mensaje claro al operador
//      (la slide del motor muestra el nombre del archivo como marcador).
// ============================================================================
using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace lumina.wpf
{
    internal sealed class PowerPointShow : IDisposable
    {
        private object _app;        // PowerPoint.Application (late-bound)
        private object _pres;       // Presentation activa
        private object _window;     // SlideShowWindow activo
        private IntPtr _hwnd;       // HWND del show (para recolocar/traer al frente)
        private string _currentPath = string.Empty;
        private bool _disposed;

        /// <summary>¿Hay un show de PowerPoint en pantalla?</summary>
        public bool IsPlaying
        {
            get { return _window != null && !_disposed; }
        }

        /// <summary>Ruta del PPTX en reproducción (vacía si ninguna).</summary>
        public string CurrentPath
        {
            get { return _currentPath; }
        }

        /// <summary>¿PowerPoint está instalado en este equipo?</summary>
        public static bool IsInstalled()
        {
            try { return Type.GetTypeFromProgID("PowerPoint.Application") != null; }
            catch (Exception) { return false; }
        }

        /* ------------------------------------------------------------ COM -- */

        private object Invoke(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.InvokeMethod |
                System.Reflection.BindingFlags.GetProperty |
                System.Reflection.BindingFlags.GetField,
                null, target, args,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private object Get(object target, string name)
        {
            return target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.GetProperty |
                System.Reflection.BindingFlags.GetField,
                null, target, null,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private void Set(object target, string name, object value)
        {
            target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.SetProperty |
                System.Reflection.BindingFlags.SetField,
                null, target, new object[] { value },
                System.Globalization.CultureInfo.InvariantCulture);
        }

        private object Call(object target, string name, params object[] args)
        {
            return target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.InvokeMethod,
                null, target, args,
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /* ----------------------------------------------------------- ciclo -- */

        /// <summary>
        /// Lanza el show del PPTX ORIGINAL sobre el monitor indicado.
        /// Devuelve false (con mensaje) si PowerPoint falta o falla.
        /// </summary>
        public bool Play(string pptxPath, System.Drawing.Rectangle screenBounds, out string error)
        {
            error = string.Empty;
            if (_disposed) { error = "el reproductor ya fue liberado"; return false; }
            if (string.IsNullOrEmpty(pptxPath) || !System.IO.File.Exists(pptxPath))
            {
                error = "el archivo no existe: " + pptxPath;
                return false;
            }
            Type t = Type.GetTypeFromProgID("PowerPoint.Application");
            if (t == null)
            {
                error = "PowerPoint no está instalado (se requiere para proyectar .pptx)";
                return false;
            }
            try
            {
                // App reutilizada entre shows (arranque más ágil); oculta.
                if (_app == null)
                {
                    _app = Activator.CreateInstance(t);
                    try { Set(_app, "Visible", 0 /*msoFalse*/); }
                    catch (Exception) { /* algunas versiones la ignoran */ }
                }
                Stop();

                // Presentations.Open(FileName, ReadOnly:=msoTrue, Untitled:=msoFalse, WithWindow:=msoFalse)
                object presentations = Get(_app, "Presentations");
                _pres = Call(presentations, "Open", pptxPath, -1 /*msoTrue*/, 0, 0);

                // Modo KIOSCO: fullscreen sin controles (ShowType=3, ppShowTypeKiosk).
                object settings = Get(_pres, "SlideShowSettings");
                try { Set(settings, "ShowType", 3); }
                catch (Exception) { /* default: speaker show (también fullscreen) */ }

                // Run() → SlideShowWindow (la proyección empieza).
                _window = Call(settings, "Run");
                _currentPath = pptxPath;

                // Recolocar el show sobre el MONITOR DEL PROYECTOR y traerlo al
                // frente (kiosco arranca en el monitor que PowerPoint decide).
                try
                {
                    _hwnd = (IntPtr)(int)Get(_window, "HWND");
                    if (_hwnd != IntPtr.Zero)
                    {
                        SetWindowPos(_hwnd, HWND_TOPMOST,
                            screenBounds.Left, screenBounds.Top,
                            screenBounds.Width, screenBounds.Height,
                            SWP_SHOWWINDOW | SWP_NOCOPYBITS);
                    }
                }
                catch (Exception) { /* posición best-effort */ }
                return true;
            }
            catch (Exception ex)
            {
                error = "PowerPoint falló al abrir la presentación: " + ex.Message;
                Stop();
                return false;
            }
        }

        /// <summary>Cierra el show y la presentación (la app queda para reuso).</summary>
        public void Stop()
        {
            _hwnd = IntPtr.Zero;
            if (_window != null)
            {
                try
                {
                    // Cerrar SOLO el show de la presentación activa.
                    Call(_window, "Exit");      // SlideShowWindow no tiene Exit en
                }               // todas las versiones → el Close del pres basta
                catch (Exception) { }
                try { Marshal.ReleaseComObject(_window); } catch (Exception) { }
                _window = null;
            }
            if (_pres != null)
            {
                try { Call(_pres, "Close"); }
                catch (Exception) { }
                try { Marshal.ReleaseComObject(_pres); } catch (Exception) { }
                _pres = null;
            }
            _currentPath = string.Empty;
        }

        /// <summary>Reenvía «siguiente» al show (si está en pantalla).</summary>
        public bool NextSlide()
        {
            if (!IsPlaying) return false;
            try { Call(Get(_window, "View"), "Next"); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Reenvía «anterior» al show (si está en pantalla).</summary>
        public bool PrevSlide()
        {
            if (!IsPlaying) return false;
            try { Call(Get(_window, "View"), "Previous"); return true; }
            catch (Exception) { return false; }
        }

        /// <summary>Trae el show al frente (p. ej. al reactivar el ítem).</summary>
        public void BringToFront()
        {
            if (_hwnd != IntPtr.Zero && IsPlaying)
            {
                try { SetWindowPos(_hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_SHOWWINDOW); }
                catch (Exception) { }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Stop();
            if (_app != null)
            {
                try { Call(_app, "Quit"); }
                catch (Exception) { }
                try { Marshal.ReleaseComObject(_app); } catch (Exception) { }
                _app = null;
            }
        }

        /* ------------------------------------------------------- Win32 ---- */
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_NOCOPYBITS = 0x0100;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
            int x, int y, int cx, int cy, uint flags);
    }
}
