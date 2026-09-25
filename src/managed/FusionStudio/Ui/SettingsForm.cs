// ============================================================================
//  Fusion-HP · SettingsForm — configuración [SPEC §4.4, §8.1, §8.4]:
// pantallas, API HTTP (token autogenerado), OBS WebSocket, pantalla de reposo
// y comportamiento de arranque. Persistencia en JSON (nunca el Registro).
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;

namespace Fusion.Studio.Ui
{
    public class SettingsForm : Form
    {
        readonly MainForm owner;
        ComboBox cmbPublic, cmbStage;
        CheckBox chkApi, chkObs;
        NumericUpDown numPort, numObsPort;
        TextBox txtObsHost, txtObsPass, txtObsSource;
        Button btnToken, btnQr;
        ComboBox cmbRest;
        CheckBox chkStartPresent;
        Label lblQrHint;

        public SettingsForm(MainForm owner)
        {
            this.owner = owner;
            Text = "Configuración";
            Size = new Size(560, 520);
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

            var g2 = Group("API HTTP (para OBS y control remoto)", y); y = g2;
            chkApi = new CheckBox { Text = "Activar servidor API local", Location = new Point(24, y), AutoSize = true, Checked = S.ApiEnabled };
            Controls.Add(chkApi);
            y += 28;
            var lblPort = new Label { Text = "Puerto:", Location = new Point(24, y + 3), AutoSize = true };
            Controls.Add(lblPort);
            numPort = new NumericUpDown { Location = new Point(80, y), Width = 80, Minimum = 1024, Maximum = 65535, Value = S.ApiPort };
            Controls.Add(numPort);
            btnToken = new Button { Text = "Regenerar token", Location = new Point(180, y - 2), AutoSize = true, FlatStyle = FlatStyle.Flat };
            btnToken.Click += delegate { txtToken.Text = NewToken(); };
            Controls.Add(btnToken);
            txtToken = new TextBox { Location = new Point(24, y + 30), Width = 320, ReadOnly = true };
            txtToken.Text = string.IsNullOrEmpty(S.ApiToken) ? NewToken() : S.ApiToken;
            Controls.Add(txtToken);
            btnQr = new Button { Text = "Ver código QR de emparejamiento", Location = new Point(24, y + 58), AutoSize = true, FlatStyle = FlatStyle.Flat };
            btnQr.Click += delegate { ShowQr(); };
            Controls.Add(btnQr);
            lblQrHint = new Label { Text = "El control remoto móvil se empareja por IP + token, sin nube ni cuentas.",
                                    Location = new Point(24, y + 86), AutoSize = true, ForeColor = UiTheme.TextDim, Font = UiTheme.Small() };
            Controls.Add(lblQrHint);
            y += 116;

            var g3 = Group("OBS Studio (WebSocket)", y); y = g3;
            chkObs = new CheckBox { Text = "Conectar con obs-websocket 5.x", Location = new Point(24, y), AutoSize = true, Checked = S.ObsEnabled };
            Controls.Add(chkObs);
            y += 28;
            AddField("Host:", ref y, out txtObsHost, S.ObsHost);
            numObsPort = AddNum("Puerto:", ref y, S.ObsPort);
            AddField("Contraseña:", ref y, out txtObsPass, S.ObsPassword, true);
            AddField("Fuente de texto:", ref y, out txtObsSource, S.ObsTextSource);
            y += 10;

            var g4 = Group("Comportamiento", y); y = g4;
            cmbRest = new ComboBox { Location = new Point(24, y), Size = new Size(200, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbRest.Items.Add("Negro"); cmbRest.Items.Add("Logo"); cmbRest.Items.Add("Fondo del tema");
            cmbRest.SelectedIndex = S.RestScreen == "logo" ? 1 : S.RestScreen == "theme" ? 2 : 0;
            Controls.Add(cmbRest);
            y += 32;
            chkStartPresent = new CheckBox { Text = "Iniciar en modo Presentación (recomendado)", Location = new Point(24, y), AutoSize = true, Checked = S.StartInPresentMode };
            Controls.Add(chkStartPresent);
            y += 40;

            var btnSave = new Button { Text = "Guardar", Location = new Point(370, 430), Size = new Size(80, 32),
                                       BackColor = UiTheme.Accent, ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
            btnSave.Click += delegate { Save(); Close(); };
            Controls.Add(btnSave);
            var btnClose = new Button { Text = "Cancelar", Location = new Point(458, 430), Size = new Size(80, 32), FlatStyle = FlatStyle.Flat };
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

        void AddField(string label, ref int y, out TextBox box, string value, bool password)
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
            S.ObsEnabled = chkObs.Checked;
            S.ObsHost = txtObsHost.Text;
            S.ObsPort = (int)numObsPort.Value;
            S.ObsPassword = txtObsPass.Text;
            S.ObsTextSource = txtObsSource.Text;
            S.RestScreen = cmbRest.SelectedIndex == 1 ? "logo" : cmbRest.SelectedIndex == 2 ? "theme" : "black";
            S.StartInPresentMode = chkStartPresent.Checked;
            S.Save();
            owner.ApplySettings();
        }
    }
}
