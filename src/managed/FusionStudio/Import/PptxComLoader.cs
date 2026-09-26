// ============================================================================
//  Fusion-HP · Import/PptxComLoader.cs — proyección del PPTX ORIGINAL tal
// cual [encargo §5]: usa la automatización COM de PowerPoint (late-binding
// con reflexión, compatible net35/4.x, sin PIA) para abrir el archivo sin
// extraerlo ni convertirlo y presentarlo a pantalla completa en el monitor
// de salida. Si PowerPoint no está, la UI ofrece la alternativa OpenXML.
// ============================================================================
using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Fusion.Studio.Import
{
    public class PptxComLoader
    {
        object app;

        public bool PowerPointAvailable()
        {
            Type t = Type.GetTypeFromProgID("PowerPoint.Application");
            return t != null;
        }

        /// <summary>Abre el .pptx original y arranca el espectáculo en el monitor indicado.</summary>
        public void PresentOriginal(string path, int monitorIndex)
        {
            Type t = Type.GetTypeFromProgID("PowerPoint.Application");
            if (t == null)
                throw new InvalidOperationException("PowerPoint no está instalado en este equipo.");

            app = Activator.CreateInstance(t);
            // app.Visible = True (msoTrue)
            object msoTrue = 1;
            try { t.InvokeMember("Visible", BindingFlags.SetProperty, null, app, new object[] { msoTrue }); }
            catch { }

            object presentations = t.InvokeMember("Presentations", BindingFlags.GetProperty, null, app, null);
            try
            {
                // Presentations.Open(path, WithWindow:=msoFalse)
                object pres = presentations.GetType().InvokeMember("Open",
                    BindingFlags.InvokeMethod, null, presentations,
                    new object[] { path, 1 /* ReadOnly */, 0 /* Untitled */, 0 /* WithWindow=false: sin ventana de edición */ });

                object slideShowSettings = pres.GetType().InvokeMember("SlideShowSettings", BindingFlags.GetProperty, null, pres, null);
                // Posicionar la presentación en el monitor de salida
                try
                {
                    object startPos = slideShowSettings.GetType().InvokeMember("StartingPosition",
                        BindingFlags.GetProperty, null, slideShowSettings, null);
                    if (startPos != null)
                    {
                        startPos.GetType().InvokeMember("Left", BindingFlags.SetProperty, null, startPos,
                            new object[] { monitorIndex * 1280 }); // ppSlideShowPosition: se ajusta con la ventana activa
                    }
                }
                catch { /* StartingPosition no existe en versiones antiguas: cae al monitor primario */ }

                slideShowSettings.GetType().InvokeMember("Run", BindingFlags.InvokeMethod, null, slideShowSettings, null);
            }
            finally
            {
                Marshal.ReleaseComObject(presentations);
            }
        }

        /// <summary>Termina la presentación y cierra PowerPoint con orden.</summary>
        public void Stop()
        {
            if (app == null) return;
            try
            {
                Type t = app.GetType();
                try
                {
                    object pres = t.InvokeMember("ActivePresentation", BindingFlags.GetProperty, null, app, null);
                    if (pres != null)
                    {
                        pres.GetType().InvokeMember("Close", BindingFlags.InvokeMethod, null, pres, null);
                        Marshal.ReleaseComObject(pres);
                    }
                }
                catch { }
                t.InvokeMember("Quit", BindingFlags.InvokeMethod, null, app, null);
            }
            catch { }
            finally
            {
                try { Marshal.ReleaseComObject(app); } catch { }
                app = null;
            }
        }
    }
}
