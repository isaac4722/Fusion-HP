// ============================================================================
//  Fusion-HP · FusionStudio/Ui/ChordsForm.cs — ventana flotante de ACORDES
// para los músicos (función única heredada de las betas 1). Muestra el cifrado
// del elemento activo con transposición EN VIVO (±semitonos, notación anglo o
// latina) SIN modificar el canto almacenado: la proyección pública nunca
// muestra acordes; esta ventana es solo para el equipo de alabanza.
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Music;
using Fusion.Shared.Model;
using Fusion.Studio.Services;

namespace Fusion.Studio.Ui
{
    public class ChordsForm : Form
    {
        readonly LiveOrchestrator live;
        int transpose;                       // semitonos de desplazamiento
        bool latin;
        FlowLayoutPanel sheet;
        Label lblKey;
        Button btnLatin;

        public ChordsForm(MainForm owner, LiveOrchestrator orchestrator)
        {
            live = orchestrator;
            Text = "Acordes — músicos";
            StartPosition = FormStartPosition.Manual;
            Location = new Point(owner.Right - 460, Math.Max(40, owner.Top + 90));
            Size = new Size(440, 520);
            MinimumSize = new Size(360, 320);
            Font = UiTheme.Normal();
            BackColor = UiTheme.Panel;
            TopMost = true;
            ShowInTaskbar = false;

            var top = new Panel { Dock = DockStyle.Top, Height = 76, BackColor = UiTheme.Panel };
            var sep = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = UiTheme.Border };
            top.Controls.Add(sep);

            lblKey = new Label { Text = "Tonalidad: —", Location = new Point(12, 8), AutoSize = true,
                                 Font = UiTheme.NormalBold(), ForeColor = UiTheme.Text };
            top.Controls.Add(lblKey);

            int x = 12;
            foreach (var step in new[] { -2, -1, 1, 2 })
            {
                int s = step;
                var b = new Fusion.Studio.Ui.Chrome.FusionButton
                {
                    Text = (step > 0 ? "+" : "") + step,
                    Location = new Point(x, 34),
                    Size = new Size(46, 28), Font = UiTheme.SmallBold()
                };
                b.Click += delegate { transpose = Clamp12(transpose + s); RefreshSheet(); };
                top.Controls.Add(b);
                x += 50;
            }
            var btnReset = new Fusion.Studio.Ui.Chrome.FusionButton { Text = "Original", IconName = "refresh",
                Location = new Point(x, 34), Size = new Size(104, 28), Font = UiTheme.Small() };
            btnReset.Click += delegate { transpose = 0; RefreshSheet(); };
            top.Controls.Add(btnReset);
            x += 108;

            btnLatin = new Fusion.Studio.Ui.Chrome.FusionButton { Text = "Anglo (C D E)", IconName = "piano",
                Location = new Point(x, 34), Size = new Size(140, 28), Font = UiTheme.Small() };
            btnLatin.Click += delegate { latin = !latin; btnLatin.Text = latin ? "Latino (Do Re Mi)" : "Anglo (C D E)"; RefreshSheet(); };
            top.Controls.Add(btnLatin);

            Controls.Add(top);

            sheet = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Color.White, AutoScroll = true,
                                         FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(10) };
            Controls.Add(sheet);
            sheet.BringToFront();

            live.StateChanged += RefreshSheet;
            FormClosed += delegate { live.StateChanged -= RefreshSheet; };
            RefreshSheet();
        }

        static int Clamp12(int v)
        {
            while (v > 6) v -= 12;
            while (v < -6) v += 12;
            return v;
        }

        void RefreshSheet()
        {
            if (IsDisposed) return;
            try { BeginInvoke((Action)BuildSheet); }
            catch { }
        }

        void BuildSheet()
        {
            if (IsDisposed) return;
            sheet.Controls.Clear();
            var el = live.CurrentElement;
            if (el == null)
            {
                lblKey.Text = "Tonalidad: —";
                AddLine("Sin elemento activo.", false);
                return;
            }
            // Tonalidad del canto (la siembra Song.ToScenario como etiqueta "key:")
            string key = "";
            foreach (var t in el.Tags)
                if (t != null && t.StartsWith("key:", StringComparison.OrdinalIgnoreCase)) key = t.Substring(4);
            lblKey.Text = "Tonalidad: " + (key.Length > 0 ? key : "—") +
                          (transpose != 0 ? "  →  " + TransposedKey(key) + "  (" + (transpose > 0 ? "+" : "") + transpose + ")" : "");

            bool anyChord = false;
            foreach (var ln in el.Lines)
            {
                bool isChord = Chords.IsChordLine(ln);
                if (isChord) anyChord = true;
                string text = isChord ? Chords.TransposeLine(ln, transpose, latin) : ln;
                AddLine(text, isChord);
            }
            if (!anyChord)
                AddLine("", false);
        }

        string TransposedKey(string key)
        {
            string k = key.Length > 0 ? key : "";
            return Chords.TransposeKey(k, transpose, latin);
        }

        void AddLine(string text, bool chord)
        {
            var l = new Label
            {
                Text = text,
                AutoSize = true,
                MaximumSize = new Size(sheet.ClientSize.Width - 30, 0),
                Margin = new Padding(2, 1, 2, 1),
                Font = chord ? new Font("Consolas", 11f, FontStyle.Bold)
                             : new Font("Segoe UI", 10f),
                ForeColor = chord ? UiTheme.AccentDark : UiTheme.TextDim,
                BackColor = Color.Transparent
            };
            sheet.Controls.Add(l);
        }
    }
}
