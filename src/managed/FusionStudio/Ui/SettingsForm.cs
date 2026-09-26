// ============================================================================
//  Fusion-HP · SettingsForm — configuración [SPEC §4.4]:
//  pantallas (monitor público y de músicos), pantalla de reposo y
//  comportamiento de arranque. Persistencia en JSON (nunca el Registro).
//  v4.0.0: sin grupos de API/OBS — el programa es 100% local.
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
        ComboBox cmbRest;
        CheckBox chkStartPresent;
        ComboBox cmbAdvance, cmbTransition;
        CheckBox chkClock, chkAnimation, chkKeepEngine;

        public SettingsForm(MainForm owner)
        {
            this.owner = owner;
            Text = "Configuración";
            ClientSize = new Size(544, 520);
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
            // [v3.0.0] Aviso honesto con un solo monitor (la salida borderless cubre
            // el escritorio del operador; el teclado del Motor mantiene el control).
            if (Screen.AllScreens.Length == 1)
            {
                var lblSolo = new Label
                {
                    Text = "Con un solo monitor la proyección cubrirá tu pantalla.\n" +
                           "Conecta un segundo monitor o usa la tecla Esc para reposo.",
                    Location = new Point(24, y + 2), Size = new Size(500, 30),
                    ForeColor = UiTheme.TextDim, Font = UiTheme.Small()
                };
                Controls.Add(lblSolo);
                y += 36;
            }

            cmbStage = new ComboBox { Location = new Point(24, y), Size = new Size(300, 24), DropDownStyle = ComboBoxStyle.DropDownList };
            cmbStage.Items.Add("(desactivada)");
            foreach (var sc in Screen.AllScreens) cmbStage.Items.Add(sc.DeviceName);
            cmbStage.SelectedIndex = S.StageMonitor >= 0 ? S.StageMonitor + 1 : 0;
            Controls.Add(cmbStage);
            y += 40;

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

            int by = y + 10;
            var btnSave = new FusionButton { Text = "Guardar", IconName = "check", Kind = FusionButtonKind.Primary,
                                             Location = new Point(356, by), Size = new Size(90, 34) };
            btnSave.Click += delegate { Save(); Close(); };
            Controls.Add(btnSave);
            var btnClose = new FusionButton { Text = "Cancelar", IconName = "x",
                                              Location = new Point(454, by), Size = new Size(86, 34) };
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnClose);
            ClientSize = new Size(544, by + 60);
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

        void Save()
        {
            var S = owner.Settings;
            S.PublicMonitor = cmbPublic.SelectedIndex;
            S.StageMonitor = cmbStage.SelectedIndex - 1;
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
