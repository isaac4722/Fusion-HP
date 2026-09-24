// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  VideoPlayerForm.cs : reproducción de VIDEO en la salida (requisito spec §3.2)
//  con Windows Media Player vía ActiveX ENLAZADO EN TIEMPO DE EJECUCIÓN
//  (AxHost + GetOcx + reflexión):
//    * CERO referencias COM en tiempo de compilación → compilla en cualquier
//      entorno (CI incluido) sin aximp ni registros.
//    * WMP viene de fábrica en Windows 7…11 (ediciones N/KN: Media Feature
//      Pack) → el usuario no instala nada.
//    * Si WPM no está (COMException), la ventana lo comunica y degrada
//      limpiamente (nunca un crash — principio «la app siempre abre»).
//  Ventana: bordeless, TopMost, sobre la pantalla del proyector; control
//  "uiMode=none" (salida limpia). MediaEnded detectado por sondeo (timer)
//  del playState — más robusto que IConnectionPoint con enlace tardío.
// ============================================================================
using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

namespace lumina.ui
{
    public sealed class VideoPlayerForm : Form
    {
        // CLSID de Windows Media Player 7+ (presente Win7→Win11)
        private const string WmpClsid = "6bf52a52-394a-11d3-b153-00c04f79faa6";

        /// <summary>
        /// AxHost tiene el constructor PROTEGIDO: se deriva (única forma legal
        /// de hospedar el OCX sin aximp ni referencias COM en compilación).
        /// </summary>
        private sealed class WmpHost : AxHost
        {
            public WmpHost() : base(WmpClsid) { }
        }

        private WmpHost _host;
        private object _player;                 // OCX crudo (late binding)
        private System.Windows.Forms.Timer _poll;
        private bool _wasPlaying;
        private int _loop;                      // 0 = sin loop · 1 = loop
        private string _path;

        /// <summary>Se dispara cuando el video terminó (y no hay loop).</summary>
        public event EventHandler MediaEnded;

        public bool WmpAvailable { get; private set; }

        public VideoPlayerForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            BackColor = Color.Black;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) StopAndHide();
            };

            try
            {
                _host = new WmpHost();
                _host.Dock = DockStyle.Fill;
                Controls.Add(_host);
                WmpAvailable = true;
            }
            catch (Exception)
            {
                WmpAvailable = false;   // sin WMP: ventana negra + mensaje
            }

            _poll = new System.Windows.Forms.Timer();
            _poll.Interval = 250;
            _poll.Tick += delegate { PollPlayState(); };
        }

        /// <summary>Reproduce un video a pantalla completa sobre la pantalla indicada.</summary>
        public bool Play(string path, Rectangle screenBounds, bool loop, int volume0to100)
        {
            if (!WmpAvailable) return false;
            if (_player == null)
            {
                _player = _host.GetOcx();
                if (_player == null) { WmpAvailable = false; return false; }
                try
                {
                    SetProp("uiMode", "none");            // salida limpia (sin controles)
                    SetProp("stretchToFit", true);
                    SetProp("enableContextMenu", false);
                    SetProp("windowlessVideo", false);
                }
                catch (Exception) { }
            }

            _path = path;
            _loop = loop ? 1 : 0;
            try
            {
                SetVolume(volume0to100);
                object settings = GetProp("settings");
                if (settings != null)
                    InvokeMember(settings, "setMode", new object[] { "loop", loop });

                SetProp("URL", path);
            }
            catch (Exception)
            {
                return false;
            }

            Bounds = screenBounds;
            if (!Visible) Show();
            WindowState = FormWindowState.Normal;   // tras Show: bounds exactos
            Bounds = screenBounds;
            Activate();
            _wasPlaying = false;
            _poll.Start();
            return true;
        }

        public void StopAndHide()
        {
            _poll.Stop();
            try
            {
                if (_player != null)
                {
                    object ctl = GetProp("Ctlcontrols");
                    InvokeMember(ctl != null ? ctl : _player, "stop", null);
                    SetProp("URL", string.Empty);
                }
            }
            catch (Exception) { }
            _wasPlaying = false;
            if (Visible) Hide();
        }

        public void SetVolume(int volume0to100)
        {
            try
            {
                object settings = GetProp("settings");
                if (settings != null)
                    InvokeMember(settings, "volume", new object[] { Math.Max(0, Math.Min(100, volume0to100)) }, true);
            }
            catch (Exception) { }
        }

        /// <summary>
        /// Reproduce SOLO audio sin mostrar la ventana (acción «play_audio» de
        /// los activadores): mismo OCX WMP, sin fondo ni sondeo de fin.
        /// </summary>
        public bool PlayAudio(string path, int volume0to100)
        {
            if (!WmpAvailable) return false;
            if (!IsHandleCreated)
            {
                // AxHost crea el OCX al crear el handle: forzarlo sin mostrar
                // la ventana (CreateControl + tocar Handle).
                CreateControl();
                IntPtr dummy = Handle;
            }
            if (_player == null)
            {
                _player = _host.GetOcx();
                if (_player == null) { WmpAvailable = false; return false; }
                try
                {
                    SetProp("uiMode", "invisible");
                    SetProp("enableContextMenu", false);
                }
                catch (Exception) { }
            }
            try
            {
                SetVolume(volume0to100);
                SetProp("URL", path);
                object ctl = GetProp("Ctlcontrols");
                if (ctl != null) InvokeMember(ctl, "play", null);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Detiene el audio oculto (si sonaba).</summary>
        public void StopAudio()
        {
            try
            {
                if (_player != null)
                {
                    object ctl = GetProp("Ctlcontrols");
                    InvokeMember(ctl != null ? ctl : _player, "stop", null);
                    SetProp("URL", string.Empty);
                }
            }
            catch (Exception) { }
        }

        public void PauseOrResume()
        {
            try
            {
                if (_player == null) return;
                object ctl = GetProp("Ctlcontrols");
                if (ctl == null) return;
                if (IsPlaying()) InvokeMember(ctl, "pause", null);
                else InvokeMember(ctl, "play", null);
            }
            catch (Exception) { }
        }

        public bool IsPlaying()
        {
            try
            {
                if (_player == null) return false;
                // playState: 1=Stopped 2=Paused 3=Playing 8=MediaEnded …
                object st = GetProp("playState");
                return st is int && ((int)st == 3);
            }
            catch (Exception) { return false; }
        }

        /// <summary>Ruta del video actual (para el evento video_ended del motor de triggers).</summary>
        public string CurrentPath { get { return _path ?? string.Empty; } }

        private void PollPlayState()
        {
            if (_player == null) return;
            try
            {
                object stObj = GetProp("playState");
                int st = stObj is int ? (int)stObj : 0;
                bool playing = st == 3;
                // Transición playing→(ended|stopped): disparo UNA vez
                if (_wasPlaying && (st == 8 || st == 1))
                {
                    _wasPlaying = false;
                    if (_loop == 1)
                    {
                        // con loop el propio WMP reinicia; solo reactivar el flag
                        _wasPlaying = true;
                    }
                    else
                    {
                        EventHandler h = MediaEnded;
                        if (h != null) h(this, EventArgs.Empty);
                    }
                    return;
                }
                if (playing) _wasPlaying = true;
            }
            catch (Exception)
            {
                // El OCX puede soltar COMException al cerrar: ignorar.
            }
        }

        // ------------------------------------------------------------ late binding

        private object GetProp(string name)
        {
            if (_player == null) return null;
            try { return InvokeMember(_player, name, null); }
            catch (Exception) { return null; }
        }

        private void SetProp(string name, object value)
        {
            if (_player == null) return;
            try { InvokeMember(_player, name, new object[] { value }, true); }
            catch (Exception) { }
        }

        /// <summary>Invocación tardía genérica sobre el objeto COM (propiedad o método).</summary>
        private static object InvokeMember(object target, string name, object[] args)
        {
            return InvokeMember(target, name, args, false);
        }

        private static object InvokeMember(object target, string name, object[] args, bool setProperty)
        {
            return target.GetType().InvokeMember(name,
                setProperty ? BindingFlags.SetProperty : (BindingFlags.GetProperty | BindingFlags.InvokeMethod),
                null, target, args ?? new object[0]);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _poll.Stop();
            try { StopAndHide(); } catch (Exception) { }
            base.OnFormClosing(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (!WmpAvailable)
            {
                Label msg = new Label();
                msg.Dock = DockStyle.Fill;
                msg.ForeColor = Color.White;
                msg.BackColor = Color.Black;
                msg.TextAlign = ContentAlignment.MiddleCenter;
                msg.Font = new Font("Segoe UI", 14F, FontStyle.Regular, GraphicsUnit.Point);
                msg.Text = "Windows Media Player no está disponible en este equipo.\r\n" +
                           "Ediciones N de Windows: instale el «Media Feature Pack».";
                Controls.Add(msg);
                msg.BringToFront();
            }
        }
    }
}
