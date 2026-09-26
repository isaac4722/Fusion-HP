// ============================================================================
//  Fusion-HP · QuickVerseForm — BIBLIA RÁPIDA (tecla G, paridad de la web):
//  ventana flotante que NO saca al operador del modo Presentación. Acepta cita
//  directa («Juan 3:16», «Sal 23», «1co 13») o búsqueda por palabra; sin
//  consultar nada muestra los pasajes favoritos (como la referencia web).
//  Casilla «Tercio»: inserta el versículo como lower third (paridad del modo
//  «Tercio» de la web). v4.0.0.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Fusion.Shared;
using Fusion.Shared.Bible;
using Fusion.Shared.Model;
using Fusion.Studio.Ui.Widgets;

namespace Fusion.Studio.Ui
{
    public class QuickVerseForm : Form
    {
        readonly MainForm owner;
        ComboBox cmbVersion;
        FusionSearchBox txtQuery;
        ListBox results;
        CheckBox chkThird;
        List<KeyValuePair<string, string>> hits = new List<KeyValuePair<string, string>>();

        /// <summary>Favoritos de la referencia web (FAVORITE_VERSE_IDS).</summary>
        static readonly string[] Favorites = new string[]
        {
            "Juan 3:16", "Salmos 23:1-6", "Filipenses 4:13", "Jeremías 29:11",
            "Romanos 8:28", "Isaías 41:10", "Proverbios 3:5-6", "Juan 14:6",
            "Efesios 2:8", "Números 6:24-26", "Mateo 11:28", "Salmos 119:105"
        };

        public QuickVerseForm(MainForm owner)
        {
            this.owner = owner;
            Text = "Biblia rápida";
            ClientSize = new Size(520, 430);
            StartPosition = FormStartPosition.CenterParent;
            Font = UiTheme.Normal();
            BackColor = UiTheme.Panel;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;

            var lbl = new Label { Text = "BIBLIA RÁPIDA", Font = UiTheme.SmallBold(), ForeColor = UiTheme.TextDim,
                                  Location = new Point(14, 12), AutoSize = true };
            Controls.Add(lbl);

            cmbVersion = new ComboBox { Location = new Point(14, 34), Size = new Size(200, 24),
                                        FlatStyle = FlatStyle.Flat, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var b in owner.Live.Bibles.List()) cmbVersion.Items.Add(b);
            if (cmbVersion.Items.Count > 0) cmbVersion.SelectedIndex = 0;
            cmbVersion.SelectedIndexChanged += delegate { RefreshResults(); };
            Controls.Add(cmbVersion);

            txtQuery = new FusionSearchBox { Location = new Point(224, 32), Size = new Size(282, 30) };
            txtQuery.Placeholder = "Cita (Jn 3:16) o palabra… (vacío = favoritos)";
            txtQuery.InnerTextChanged += delegate { RefreshResults(); };
            txtQuery.Inner.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter && results.Items.Count > 0)
                {
                    results.SelectedIndex = results.SelectedIndex < 0 ? 0 : results.SelectedIndex;
                    InsertSelected(); e.SuppressKeyPress = true;
                }
            };
            Controls.Add(txtQuery);

            results = new ListBox { Location = new Point(14, 72), Size = new Size(492, 280),
                                    BorderStyle = BorderStyle.FixedSingle, DrawMode = DrawMode.OwnerDrawVariable,
                                    IntegralHeight = false, Font = UiTheme.Small(), BackColor = Color.White };
            results.DrawItem += ResultsDraw;
            results.MeasureItem += delegate(object s, MeasureItemEventArgs e) { e.ItemHeight = 34; };
            results.DoubleClick += delegate { InsertSelected(); };
            Controls.Add(results);

            chkThird = new CheckBox { Text = "Insertar como Tercio (lower third)", Location = new Point(14, 360),
                                      AutoSize = true };
            Controls.Add(chkThird);

            var btnInsert = new FusionButton { Text = "Insertar en vivo", IconName = "player-play",
                                               Kind = FusionButtonKind.Primary,
                                               Location = new Point(356, 384), Size = new Size(150, 32) };
            btnInsert.Click += delegate { InsertSelected(); };
            Controls.Add(btnInsert);

            RefreshResults();
        }

        InstalledBible CurrentBible()
        {
            return cmbVersion.SelectedItem as InstalledBible;
        }

        void RefreshResults()
        {
            var b = CurrentBible();
            hits.Clear();
            results.Items.Clear();
            if (b == null) return;
            string q = txtQuery.Text.Trim();
            try
            {
                if (q.Length == 0)
                {
                    // favoritos: resuelve cada cita contra la versión activa
                    foreach (string f in Favorites)
                    {
                        try
                        {
                            var r = BibleReference.Parse(f);
                            if (!r.Valid) continue;
                            var vs = owner.Live.Bibles.GetPassage(b, r);
                            if (vs.Count == 0) continue;
                            string text = string.Join(" ", vs.ToArray());
                            if (text.Length > 90) text = text.Substring(0, 90) + "…";
                            hits.Add(new KeyValuePair<string, string>(f, text));
                        }
                        catch { }
                    }
                }
                else
                {
                    foreach (var h in owner.Live.Bibles.Search(b, q, 30))
                        hits.Add(new KeyValuePair<string, string>(h.Key, h.Value));
                }
            }
            catch { }
            foreach (var h in hits) results.Items.Add(h);
        }

        void ResultsDraw(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= hits.Count) return;
            var h = hits[e.Index];
            bool sel = (e.State & DrawItemState.Selected) != 0;
            using (var bg = new SolidBrush(sel ? UiTheme.AccentSoft : Color.White))
                e.Graphics.FillRectangle(bg, e.Bounds);
            using (var b = new SolidBrush(UiTheme.AccentDark))
                TextRenderer.DrawText(e.Graphics, h.Key, UiTheme.NormalBold(),
                    new Rectangle(e.Bounds.X + 6, e.Bounds.Y, e.Bounds.Width - 10, 16),
                    UiTheme.AccentDark, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, h.Value, UiTheme.Small(),
                new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 16, e.Bounds.Width - 10, 16),
                UiTheme.TextDim, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        void InsertSelected()
        {
            if (results.SelectedIndex < 0 || results.SelectedIndex >= hits.Count) return;
            string cite = hits[results.SelectedIndex].Key;
            var b = CurrentBible();
            if (b == null) return;
            var refr = BibleReference.Parse(cite);
            if (!refr.Valid) return;
            var verses = owner.Live.Bibles.GetPassage(b, refr);
            if (verses.Count == 0) return;

            Scenario scn;
            if (chkThird.Checked)
            {
                // Tercio (web): el versículo como lower third — una sola línea de
                // pantalla que no interrumpe el elemento en curso.
                string text = string.Join(" ", verses.ToArray());
                if (text.Length > 220) text = text.Substring(0, 220) + "…";
                scn = new Scenario { Title = refr.ToString() + " (tercio)" };
                scn.Elements.Add(new Element
                {
                    Kind = ElementKind.LowerThird,
                    Reference = refr.ToString(),
                    OverlayText = refr.ToString() + " — " + text,
                    Lines = { refr.ToString() }
                });
            }
            else
            {
                scn = new Scenario { Title = refr.ToString() };
                for (int i = 0; i < verses.Count; i += 3)
                {
                    var el = new Element { Kind = ElementKind.Verse, Reference = refr.ToString() };
                    for (int k = i; k < i + 3 && k < verses.Count; k++) el.Lines.Add(verses[k]);
                    scn.Elements.Add(el);
                }
            }
            owner.Live.SendToLive(scn);
            Close();
        }
    }
}
