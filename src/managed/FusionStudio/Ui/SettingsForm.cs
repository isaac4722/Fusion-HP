// ============================================================================
//  Fusion-HP · SettingsForm — configuración [SPEC §4.4, §8.1, §8.4]:
// pantallas, API HTTP de control remoto (token autogenerado), pantalla de reposo
// y comportamiento de arranque. Persistencia en JSON (nunca el Registro).
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class SettingsForm : Form
    {
        readonly MainForm owner;
        ComboBox cmbPublic, cmbStage;
        CheckBox chkApi;
        NumericUpDown numPort;
        Button btnToken, btnQr;
        ComboBox cmbRest;
        CheckBox chkStartPresent;
        Label lblQrHint;
        ComboBox cmbAdvance, cmbTransition;
        CheckBox chkClock, chkAnimation, chkKeepEngine;

        public SettingsForm(MainForm owner)
        {
            this.owner = owner;
            Text = "Configuración";
            Size = new Size(560, 710);
            StartPosition = FormStartPosition.CenterParent;
            Font = UiTheme.Normal();
            BackColor = UiTheme.Panel;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;

            var S = owner.Settings;
            int y = 16;
            y = Group("Pantallas", y);

            cmbPublic = new ComboBox { Location = new Point(24, y), Size = new Size(300, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var sc in Screen.AllScreens)
                cmbPublic.Items.Add(sc.DeviceName + (sc.Primary ? " (principal)" : ""));
            cmbPublic.SelectedIndex = S.PublicMonitor >= 0 && S.PublicMonitor < Screen.AllScreens.Length ? S.PublicMonitor : 0;
            Controls.Add(cmbPublic);
            y += 30;

            cmbStage = new ComboBox { Location = new Point(24, y), Size = new Size(300, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStage.Items.Add("(desactivada)");
            foreach (var sc in Screen.AllScreens) cmbStage.Items.Add(sc.DeviceName);
            cmbStage.SelectedIndex = S.StageMonitor >= 0 ? S.StageMonitor + 1 : 0;
            Controls.Add(cmbStage);
            y += 40;

            var g2 = Group("API HTTP (control remoto)", y); y = g2;
            chkApi = new CheckBox { Text = "Activar servidor API local", Location = new Point(24, y), AutoSize = true, Checked = S.ApiEnabled };
            Controls.Add(chkApi);
            y += 28;
            var lblPort = new Label { Text = "Puerto:", Location = new Point(24, y + 3), AutoSize = true };
            Controls.Add(lblPort);
            numPort = new NumericUpDown { Location = new Point(80, y), Width = 80, Minimum = 1024, Maximum = 65535, Value = S.ApiPort };
            Controls.Add(numPort);
            btnToken = new FusionButton { Text = "Regenerar token", IconName = "refresh",
                                          Location = new Point(180, y - 3), Size = new Size(150, 32) };
            btnToken.Click += delegate { txtToken.Text = NewToken(); };
            Controls.Add(btnToken);
            txtToken = new TextBox { Location = new Point(24, y + 34), Width = 320, ReadOnly = true, BorderStyle = BorderStyle.FixedSingle };
            txtToken.Text = string.IsNullOrEmpty(S.ApiToken) ? NewToken() : S.ApiToken;
            Controls.Add(txtToken);
            btnQr = new FusionButton { Text = "Ver código QR de emparejamiento", IconName = "device-mobile",
                                      Location = new Point(24, y + 72), Size = new Size(244, 32) };
            btnQr.Click += delegate { ShowQr(); };
            Controls.Add(btnQr);
            lblQrHint = new Label { Text = "El control remoto móvil se empareja por IP + token, sin nube ni cuentas.",
                                    Location = new Point(24, y + 108), AutoSize = true, ForeColor = UiTheme.TextDim, Font = UiTheme.Small() };
            Controls.Add(lblQrHint);
            y += 136;

            var g4 = Group("Comportamiento", y); y = g4;
            cmbRest = new ComboBox { Location = new Point(24, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRest.Items.Add("Negro"); cmbRest.Items.Add("Logo"); cmbRest.Items.Add("Fondo del tema");
            cmbRest.SelectedIndex = S.RestScreen == "logo" ? 1 : S.RestScreen == "theme" ? 2 : 0;
            Controls.Add(cmbRest);
            y += 32;
            chkStartPresent = new CheckBox { Text = "Iniciar en modo Presentación (recomendado)", Location = new Point(24, y), AutoSize = true, Checked = S.StartInPresentMode };
            Controls.Add(chkStartPresent);
            y += 30;

            var l5 = new Label { Text = "Avance con Espacio/flechas:", Location = new Point(24, y + 3), AutoSize = true };
            Controls.Add(l5);
            cmbAdvance = new ComboBox { Location = new Point(220, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbAdvance.Items.Add("Línea por línea (letra sincronizada)");
            cmbAdvance.Items.Add("Diapositiva completa");
            cmbAdvance.SelectedIndex = S.AdvanceMode == "slide" ? 1 : 0;
            Controls.Add(cmbAdvance);
            y += 32;

            var l6 = new Label { Text = "Transición entre elementos:", Location = new Point(24, y + 3), AutoSize = true };
            Controls.Add(l6);
            cmbTransition = new ComboBox { Location = new Point(220, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbTransition.Items.Add("Fundido (fade)");
            cmbTransition.Items.Add("Deslizamiento (slide)");
            cmbTransition.Items.Add("Corte directo (cut)");
            cmbTransition.SelectedIndex = S.DefaultTransition == "slide" ? 1 : S.DefaultTransition == "cut" ? 2 : 0;
            Controls.Add(cmbTransition);
            y += 32;

            chkAnimation = new CheckBox { Text = "Animaciones de la interfaz y transiciones", Location = new Point(24, y), AutoSize = true, Checked = S.Animation };
            Controls.Add(chkAnimation);
            y += 28;
            chkClock = new CheckBox { Text = "Mostrar reloj en la consola", Location = new Point(24, y), AutoSize = true, Checked = S.ShowClock };
            Controls.Add(chkClock);
            y += 34;

            var g5 = Group("Motor (núcleo de proyección)", y); y = g5;
            chkKeepEngine = new CheckBox
            {
                Text = "Al cerrar esta ventana el Motor sigue proyectando (recomendado)",
                Location = new Point(24, y), AutoSize = true, Checked = S.KeepEngineAlive
            };
            Controls.Add(chkKeepEngine);
            var lblKeep = new Label
            {
                Text = "El Motor queda autónomo: teclado sobre la salida (Espacio avanza,\n" +
                       "B/C/L pantallas, Esc negro, Alt+F4 apaga) y al reabrir la GUI todo\n" +
                       "se sincroniza desde el estado del Motor.",
                Location = new Point(24, y + 24), Size = new Size(500, 48),
                ForeColor = UiTheme.TextDim, Font = UiTheme.Small()
            };
            Controls.Add(lblKeep);
            y += 80;

            var btnSave = new FusionButton { Text = "Guardar", IconName = "check", Kind = FusionButtonKind.Primary,
                                            Location = new Point(360, 620), Size = new Size(90, 34) };
            btnSave.Click += delegate { Save(); Close(); };
            Controls.Add(btnSave);
            var btnClose = new FusionButton { Text = "Cancelar", IconName = "x",
                                            Location = new Point(458, 620), Size = new Size(86, 34) };
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
        }

        int Group(string title, int y)
        {
            var g = new Label { Text = title, Font = UiTheme.NormalBold(), ForeColor = UiTheme.AccentDark,
                                Location = new Point(16, y), AutoSize = true };
            Controls.Add(g);
            var line = new Panel { Location = new Point(16, y + 22), Size = new Size(520, 1), BackColor = UiTheme.Border };
            Controls.Add(line);
            return y + 30;
        }

        void AddField(string label, ref int y, out TextBox box, string value, bool password = false)
        {
            var l = new Label { Text = label, Location = new Point(24, y + 3), AutoSize = true };
            Controls.Add(l);
            box = new TextBox { Location = new Point(140, y), Width = 240 };
            if (password) box.UseSystemPasswordChar = true;
            box.Text = value ?? "";
            Controls.Add(box);
            y += 30;
        }

        NumericUpDown AddNum(string label, ref int y, int value)
        {
            var l = new Label { Text = label, Location = new Point(24, y + 3), AutoSize = true };
            Controls.Add(l);
            var n = new NumericUpDown { Location = new Point(140, y), Width = 90, Minimum = 1, Maximum = 65535, Value = value };
            Controls.Add(n);
            y += 30;
            return n;
        }

        TextBox txtToken;

        static string NewToken()
        {
            var rng = new Random(Environment.TickCount);
            var chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < 24; i++) sb.Append(chars[rng.Next(chars.Length)]);
            return sb.ToString();
        }

        void ShowQr()
        {
            string ip = "127.0.0.1";
            try
            {
                var host = System.Net.Dns.GetHostEntry(System.Net.Dns.GetHostName());
                foreach (var a in host.AddressList)
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) { ip = a.ToString(); break; }
            }
            catch { }
            string url = "http://" + ip + ":" + (int)numPort.Value + "/remote?token=" + txtToken.Text;
            // QR simple por texto: el operador puede teclear la URL o copiarla.
            // (La representación gráfica del QR vive en el diálogo de diagnóstico.)
            MessageBox.Show(this,
                "Emparejamiento del control remoto:\n\n" + url + "\n\n" +
                "Abre esa dirección en el teléfono (misma red Wi-Fi) e introduce el token:\n" +
                txtToken.Text + "\n\nSin Internet, sin nube, sin cuentas.",
                "Fusion HP — control remoto", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void Save()
        {
            var S = owner.Settings;
            S.PublicMonitor = cmbPublic.SelectedIndex;
            S.StageMonitor = cmbStage.SelectedIndex - 1;
            S.ApiEnabled = chkApi.Checked;
            S.ApiPort = (int)numPort.Value;
            S.ApiToken = txtToken.Text;
            S.RestScreen = cmbRest.SelectedIndex == 1 ? "logo" : cmbRest.SelectedIndex == 2 ? "theme" : "black";
            S.StartInPresentMode = chkStartPresent.Checked;
            S.AdvanceMode = cmbAdvance.SelectedIndex == 1 ? "slide" : "line";
            S.DefaultTransition = cmbTransition.SelectedIndex == 1 ? "slide" : cmbTransition.SelectedIndex == 2 ? "cut" : "fade";
            S.Animation = chkAnimation.Checked;
            S.ShowClock = chkClock.Checked;
            S.KeepEngineAlive = chkKeepEngine.Checked;
            S.Save();
            owner.ApplySettings();
        }
    }
}
