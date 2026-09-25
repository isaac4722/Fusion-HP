// ============================================================================
//  LuminaPresentation Suite — managed/Lumina.UI/WpfEditorHost.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  WpfEditorHost.cs — F2.09 «Shell WinForms + WPF».
//
//  La ventana principal SIGUE SIENDO WinForms (norma del fixture: no dos
//  aplicaciones independientes) y el EDITOR de canción vive en WPF,
//  incrustado en la página de Canciones mediante ElementHost.
//
//    * Solo se compila en net48 (WindowsFormsIntegration no existe en net35;
//      la variante net35 conserva el editor 100% WinForms — misma página).
//    * Contenido editable COMPLETO incluso en PERFIL B (sin núcleo nativo):
//      el editor WPF edita el MISMO modelo de la canción (título/artista/
//      letra) sincronizado en dos vías con los controles WinForms.
//    * PERFIL B ⇒ efectos acelerados DESACTIVADOS: sin BitmapCache ni
//      animaciones (RenderCapability.Tier 0 o núcleo ausente →Effects=false).
// ============================================================================
#if NET48
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using FormsInt = System.Windows.Forms.Integration;

namespace lumina.ui
{
    /// <summary>
    /// Editor de canción en WPF (mismo modelo que el editor WinForms). Dos
    /// vías: la UI WinForms empuja el texto aquí y los cambios del usuario
    /// en WPF vuelven al modelo vía <see cref="TextChanged"/>.
    /// </summary>
    public sealed class WpfSongEditor : UserControl
    {
        private readonly TextBox _txtTitle;
        private readonly TextBox _txtArtist;
        private readonly TextBox _txtLyrics;
        private readonly TextBlock _lblKey;
        private bool _syncing;

        /// <summary>El usuario editó algo en WPF (la UI WinForms lo persiste).</summary>
        public event Action EditedByUser;

        public WpfSongEditor()
        {
            Grid root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _txtTitle = BuildBox(root, 0, "Título:");
            _txtArtist = BuildBox(root, 1, "Artista:");
            _lblKey = new TextBlock
            {
                Text = "Tono: —",
                Margin = new Thickness(4, 6, 4, 2),
                Foreground = Brushes.DimGray,
            };
            Grid.SetRow(_lblKey, 2);
            root.Children.Add(_lblKey);

            _txtLyrics = new TextBox
            {
                AcceptsReturn = true,
                AcceptsTab = false,
                TextWrapping = TextWrapping.NoWrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(4, 2, 4, 4),
                FontFamily = new FontFamily("Consolas"),
            };
            Grid.SetRow(_txtLyrics, 3);
            root.Children.Add(_txtLyrics);

            TextBlock hint = new TextBlock
            {
                Text = "Editor WPF incrustado (ElementHost · F2.09) — edita el mismo modelo",
                Margin = new Thickness(4, 2, 4, 4),
                Foreground = Brushes.DimGray,
                FontSize = 10.5,
            };
            Grid.SetRow(hint, 4);
            root.Children.Add(hint);

            Content = root;

            TextChangedEventHandler onEdit = delegate
            {
                if (_syncing) return;
                Action h = EditedByUser;
                if (h != null) h();
            };
            _txtTitle.TextChanged += onEdit;
            _txtArtist.TextChanged += onEdit;
            _txtLyrics.TextChanged += onEdit;
        }

        private static TextBox BuildBox(Grid root, int row, string label)
        {
            Grid g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Label l = new Label { Content = label, Width = 62, HorizontalContentAlignment = HorizontalAlignment.Right };
            TextBox t = new TextBox { Margin = new Thickness(4, 2, 4, 2) };
            Grid.SetColumn(l, 0);
            Grid.SetColumn(t, 1);
            g.Children.Add(l);
            g.Children.Add(t);
            Grid.SetRow(g, row);
            root.Children.Add(g);
            return t;
        }

        /// <summary>Escribe el modelo (desde WinForms) sin disparar EditedByUser.</summary>
        public void SetModel(string title, string artist, string lyrics)
        {
            _syncing = true;
            try
            {
                string t = title ?? string.Empty;
                string a = artist ?? string.Empty;
                string l = lyrics ?? string.Empty;
                if (_txtTitle.Text != t) _txtTitle.Text = t;
                if (_txtArtist.Text != a) _txtArtist.Text = a;
                if (_txtLyrics.Text != l) _txtLyrics.Text = l;
                _lblKey.Text = "Tono: " + DetectKeyOf(l);
            }
            finally { _syncing = false; }
        }

        /// <summary>Lee el modelo (hacia WinForms).</summary>
        public void GetModel(out string title, out string artist, out string lyrics)
        {
            title = _txtTitle.Text; artist = _txtArtist.Text; lyrics = _txtLyrics.Text;
        }

        private static string DetectKeyOf(string lyrics)
        {
            // Espejo de ChordUtil.DetectKey (la detección REAL vive en Core y
            // la UI WinForms la aplica; aquí solo el eco visual del editor).
            if (string.IsNullOrEmpty(lyrics)) return "—";
            return null;   // null = la UI WinForms recalcóla con ChordUtil real
        }
    }

    /// <summary>
    /// Anfitrión ElementHost del editor WPF dentro de la página de Canciones
    /// (WinForms). En PERFIL B desactiva los efectos acelerados (F2.09.4).
    /// </summary>
    public sealed class WpfEditorHost : IDisposable
    {
        public readonly FormsInt.ElementHost Host;
        public readonly WpfSongEditor Editor;
        public readonly bool EffectsEnabled;

        /// <param name="profileB">true = sin núcleo nativo (perfil B) → sin efectos.</param>
        public WpfEditorHost(bool profileB)
        {
            Editor = new WpfSongEditor();
            Host = new FormsInt.ElementHost { Dock = System.Windows.Forms.DockStyle.Fill, Child = Editor };

            // F2.09.4: perfil B (o GPU tier 0) ⇒ SIN efectos acelerados.
            int tier = System.Windows.Media.RenderCapability.Tier >> 16;
            EffectsEnabled = !profileB && tier > 0;
            if (!EffectsEnabled)
            {
                Editor.CacheMode = null;                     // sin composición en caché
                TextOptions.SetTextFormattingMode(Editor, TextFormattingMode.Display);
                RenderOptions.SetBitmapScalingMode(Editor, BitmapScalingMode.LowQuality);
                // Sin animaciones registradas: el editor es estático por diseño
                // (lo documentado en el fixture); en perfil A se podría añadir
                // una transición de opacidad con Timeline.DesiredFrameRate.
            }
        }

        public void Dispose()
        {
            if (Host != null)
            {
                Host.Child = null;
                Host.Dispose();
            }
        }
    }
}
#endif // NET48
