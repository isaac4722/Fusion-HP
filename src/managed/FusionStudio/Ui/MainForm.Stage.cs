// ============================================================================
//  Fusion-HP · MainForm.Stage.cs — ESCENARIO DE MÚSICOS (Stage View beta-1):
//  alertas doradas, temporizador regresivo y tono/BPM enviados al monitor de
//  retorno del núcleo C++ (comandos ipc stage.*). Además: grabación de
//  historial y aperturas de Clasificador/Historial (GUI web + beta-1).
// ============================================================================
using System;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Services;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public partial class MainForm
    {
        FusionInput txtStageAlert;
        FusionButton btnStageAlert, btnStageAlertClear, btnStageTimer, btnStageTimerStop, btnSorter, btnHistory;
        NumericUpDown numStageMin;
        Timer stageTimer;
        int stageCountdown = -1;
        string lastHistoryKey = "";

        /// <summary>Construye la sección ESCENARIO (músicos) del panel derecho.</summary>
        void BuildStageTools(Panel right, ref int y)
        {
            var lbl = new Label { Text = "ESCENARIO (MÚSICOS)", Font = UiTheme.SmallBold(),
                                  ForeColor = UiTheme.TextDim, Location = new Point(12, y), AutoSize = true };
            right.Controls.Add(lbl); y += 22;

            txtStageAlert = new FusionInput { Location = new Point(12, y), Size = new Size(RightWidth - 24, 30) };
            Tips.SetToolTip(txtStageAlert, "Mensaje dorado en el monitor de retorno (Enter envía).");
            txtStageAlert.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { SendStageAlert(); e.SuppressKeyPress = true; }
            };
            right.Controls.Add(txtStageAlert); y += 32;

            btnStageAlert = new FusionButton { Text = "Enviar alerta", IconName = "bolt",
                                              Kind = FusionButtonKind.Primary,
                                              Location = new Point(12, y), Size = new Size(122, 30) };
            btnStageAlert.Click += delegate { SendStageAlert(); };
            right.Controls.Add(btnStageAlert);

            btnStageAlertClear = new FusionButton { Text = "Limpiar", IconName = "x",
                                                    Kind = FusionButtonKind.Chip,
                                                    Location = new Point(140, y), Size = new Size(78, 30) };
            btnStageAlertClear.Click += delegate
            {
                txtStageAlert.Text = "";
                var p = JsonValue.Object();
                p.Set("message", JsonValue.Make(""));
                Live.PostCore("stage.alert", p);
            };
            right.Controls.Add(btnStageAlertClear); y += 34;

            var lblTimer = new Label { Text = "Temporizador (min):", Font = UiTheme.Small(),
                                       ForeColor = UiTheme.TextDim, Location = new Point(12, y + 4), AutoSize = true };
            right.Controls.Add(lblTimer);
            numStageMin = new NumericUpDown { Location = new Point(140, y), Width = 78,
                                              Minimum = 1, Maximum = 180, Value = 5 };
            right.Controls.Add(numStageMin); y += 30;

            btnStageTimer = new FusionButton { Text = "Iniciar cuenta atrás", IconName = "hourglass",
                                              Kind = FusionButtonKind.Chip,
                                              Location = new Point(12, y), Size = new Size(206, 30) };
            btnStageTimer.Click += delegate
            {
                stageCountdown = (int)(numStageMin.Value * 60);
                var p = JsonValue.Object();
                p.Set("seconds", JsonValue.Make(stageCountdown));
                Live.PostCore("stage.timer", p);
                if (stageTimer != null) stageTimer.Start();
            };
            right.Controls.Add(btnStageTimer); y += 32;

            btnStageTimerStop = new FusionButton { Text = "Detener temporizador", IconName = "square",
                                                  Kind = FusionButtonKind.Chip,
                                                  Location = new Point(12, y), Size = new Size(206, 28) };
            btnStageTimerStop.Click += delegate
            {
                stageCountdown = -1;
                var p = JsonValue.Object();
                p.Set("seconds", JsonValue.Make(-1));
                Live.PostCore("stage.timer", p);
                if (stageTimer != null) stageTimer.Stop();
            };
            right.Controls.Add(btnStageTimerStop); y += 34;

            // descuento local para reenviar cada segundo (el núcleo también cuenta)
            stageTimer = new Timer { Interval = 1000 };
            stageTimer.Tick += delegate
            {
                if (stageCountdown > 0) stageCountdown--;
                if (stageCountdown == 0) stageTimer.Stop();
            };
        }

        void SendStageAlert()
        {
            var p = JsonValue.Object();
            p.Set("message", JsonValue.Make(txtStageAlert.Text.Trim()));
            Live.PostCore("stage.alert", p);
        }

        /// <summary>Tono/BPM del canto actual al escenario (busca por título en el cancionero).</summary>
        void SendStageInfoForCurrent()
        {
            try
            {
                var scn = Live.CurrentScenario;
                if (scn == null || Live.Songs == null) return;
                foreach (var s in Live.Songs.Search(scn.Title))
                {
                    if (string.Equals(s.Title, scn.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        var p = JsonValue.Object();
                        if (!string.IsNullOrEmpty(s.Key_)) p.Set("key", JsonValue.Make(s.Key_));
                        if (s.Bpm > 0) p.Set("bpm", JsonValue.Make(s.Bpm));
                        Live.PostCore("stage.info", p);
                        return;
                    }
                }
            }
            catch { }
        }

        /// <summary>Registra el historial de uso cuando cambia el elemento proyectado.</summary>
        void RecordHistoryFromLive()
        {
            try
            {
                var scn = Live.CurrentScenario;
                var el = Live.CurrentElement;
                string key = Live.State.ScenarioIndex + ":" + Live.State.ElementIndex;
                if (scn == null || el == null || key == lastHistoryKey) return;
                lastHistoryKey = key;
                UsageHistory.Record(scn.Title, el.KindKey);
                SendStageInfoForCurrent();
            }
            catch { }
        }

        /// <summary>Botones de la GUI web/beta-1 en el panel de herramientas.
        /// v4.2.0: Clasificador e Historial se crean ahora en la cuadrícula de
        /// HERRAMIENTAS (BuildPresent) — el método separado duplicaba la fila y
        /// consumía 66 px de la columna derecha que ya no existían (C2).</summary>

        SorterForm sorterForm;
        void ShowSorter()
        {
            if (sorterForm == null || sorterForm.IsDisposed)
                sorterForm = new SorterForm(this, Live);
            sorterForm.Show(this);
            sorterForm.BringToFront();
        }

        HistoryForm historyForm;
        void ShowHistory()
        {
            if (historyForm == null || historyForm.IsDisposed)
                historyForm = new HistoryForm(this);
            historyForm.Show(this);
            historyForm.BringToFront();
        }

        OperatorForm operatorForm;

        /// <summary>Modo Operador (v4.2.0, F8): consola en vivo dedicada —
        /// «en pantalla» + «siguiente», transporte gigante y pantallas a un toque
        /// (patrón Holy/PTT, vista presentador de PowerPoint).</summary>
        void ShowOperator()
        {
            if (operatorForm == null || operatorForm.IsDisposed)
                operatorForm = new OperatorForm(this, Live);
            operatorForm.Show(this);
            operatorForm.BringToFront();
            operatorForm.Activate();
        }
    }
}
