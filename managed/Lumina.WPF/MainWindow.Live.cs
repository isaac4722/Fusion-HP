// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/MainWindow.Live.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainWindow.Live.cs (2/4): proyección en vivo (transporte, lista, vista
//  previa), carga de escenarios (canción/pasaje/culto) y salidas auxiliares
//  (video, monitor de escenario, director, zócalo) con su sincronización.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using lumina.bridge;
using lumina.core;

namespace lumina.wpf
{
    public partial class MainWindow : Window
    {
        /* ======================================================================
         *  EN VIVO — transporte y lista
         * ==================================================================== */

        internal void ShowSelectedSlide()
        {
            if (!RequireEngine()) return;
            int idx = pageLive.SelectedIndex();
            if (idx >= 0) _engine.ShowSlide(idx);
            else if (_currentIndex >= 0) _engine.ShowSlide(_currentIndex);
            else Status("No hay slide seleccionada.");
        }

        internal void ToggleBlack()
        {
            if (!RequireEngine()) return;
            _engine.Black(!_black);
        }

        /// <summary>El botón «Negro» se enciende cuando la salida está oculta.</summary>
        internal void UpdateBlackButton()
        {
            try { pageLive.btnBlack.Active = _black; }
            catch (Exception) { }
        }

        /// <summary>Reconstruye la lista de slides (badge + ref + primera línea).</summary>
        internal void RefreshLiveList()
        {
            pageLive.FillSlides(_slides);
            HighlightCurrentSlide();
        }

        internal void HighlightCurrentSlide()
        {
            pageLive.HighlightSlide(_currentIndex);
            UpdatePreviewInfo();
        }

        /// <summary>«Jn 3:16 · Diapositiva 2 de 5» bajo la vista previa.</summary>
        private void UpdatePreviewInfo()
        {
            try
            {
                int n = _slides.Count;
                if (n == 0)
                {
                    pageLive.txtPreviewInfo.Text = "Sin diapositivas en el escenario";
                    return;
                }
                if (_currentIndex < 0 || _currentIndex >= n)
                {
                    pageLive.txtPreviewInfo.Text = "Diapositiva — de " +
                        n.ToString(CultureInfo.InvariantCulture);
                    return;
                }
                SlideView v = _slides[_currentIndex];
                string refLabel = v != null && v.RefLabel != null && v.RefLabel.Length > 0
                    ? v.RefLabel : "Diapositiva";
                pageLive.txtPreviewInfo.Text = refLabel + "  ·  Diapositiva " +
                    (_currentIndex + 1).ToString(CultureInfo.InvariantCulture) + " de " +
                    n.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception) { }
        }

        /// <summary>Pide el PNG al núcleo y lo pinta en la vista previa.</summary>
        internal void RefreshPreview()
        {
            if (pageLive == null) return;
            byte[] png = null;
            if (_engine != null && _currentIndex >= 0)
            {
                try { png = _engine.RenderPreviewPng(_currentIndex); }
                catch (Exception) { png = null; }
            }
            if (png != null && png.Length > 0)
            {
                try
                {
                    BitmapImage bi = new BitmapImage();
                    bi.BeginInit();
                    bi.StreamSource = new MemoryStream(png);
                    bi.CacheOption = BitmapCacheOption.OnLoad;
                    bi.EndInit();
                    bi.Freeze();
                    pageLive.imgPreview.Source = bi;
                }
                catch (Exception)
                {
                    pageLive.imgPreview.Source = null;
                }
            }
            else
            {
                pageLive.imgPreview.Source = null;
            }
            pageLive.txtNoPreview.Text = _engine == null
                ? "Vista previa no disponible (núcleo inactivo)"
                : (_currentIndex >= 0
                    ? "Vista previa no disponible para esta slide"
                    : "Carga una canción o pasaje para ver la vista previa");
            pageLive.UpdatePreviewChrome();
        }

        /// <summary>Mapa de atajos (F1).</summary>
        internal void ShowShortcutsDialog()
        {
            try
            {
                string text =
                    "Atajos de teclado\n\n" +
                    "  F1      · esta ayuda\n" +
                    "  F5      · mostrar proyector\n" +
                    "  F6      · monitor de escenario\n" +
                    "  F7      · ventana de director\n" +
                    "  F8      · zócalo inferior / aviso\n" +
                    "  → / Espacio / AvPág  · siguiente diapositiva\n" +
                    "  ← / RePág             · diapositiva anterior\n" +
                    "  B       · ocultar la salida en negro\n" +
                    "  L       · limpiar la salida\n" +
                    "  P       · pausar el video en la salida\n" +
                    "  Enter   · proyectar la diapositiva seleccionada\n" +
                    "  Ctrl+1…9 · ir a la página n de la navegación\n\n" +
                    "(las letras solo actúan cuando NO se está editando texto)";
                MessageBox.Show(this, text, AppName + " — atajos de teclado",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception) { }
        }

        /* ======================================================================
         *  PROYECCIÓN
         * ==================================================================== */

        private System.Drawing.Rectangle ProjectorScreenBounds()
        {
            try
            {
                System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
                int idx = pageLive.SelectedScreenIndex();
                if (screens.Length == 0) return System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                return screens[Math.Min(idx, screens.Length - 1)].Bounds;
            }
            catch (Exception)
            {
                try { return System.Windows.Forms.Screen.PrimaryScreen.Bounds; }
                catch (Exception) { return new System.Drawing.Rectangle(0, 0, 1280, 720); }
            }
        }

        internal void ShowProjector()
        {
            if (!RequireEngine()) return;
            int screen = pageLive.SelectedScreenIndex();
            bool fullscreen = pageLive.IsFullscreenChecked();
            int st = _engine.ProjectorShow(screen, fullscreen);
            Status(st == LuminaStatus.Ok
                ? "Proyector mostrado (pantalla " + screen + (fullscreen ? ", completa)." : ").")
                : "El núcleo no puede proyectar (" + LuminaStatus.Name(st) + "). " +
                  "La proyección nativa requiere el núcleo Windows (no headless).");
        }

        /* ======================================================================
         *  ESCENARIO (builder → motor)
         * ==================================================================== */

        internal void LoadScenarioFromItems(IList<ScenarioItem> items, string name)
        {
            // Contrato (blindaje v5.2.0): NUNCA lanza; sin datos o sin núcleo →
            // mensaje claro en la barra de estado.
            if (!RequireEngine()) return;
            if (items == null || items.Count == 0)
            {
                Status("No hay ítems que cargar al escenario.");
                return;
            }
            name = name ?? string.Empty;
            if (_theme == null) _theme = new Theme();
            try
            {
                LoadScenarioFromItemsCore(items, name);
            }
            catch (Exception ex)
            {
                Status("No se pudo cargar el escenario: " + ex.Message);
                try { App.LogLine("LoadScenarioFromItems FALLO: " + ex); }
                catch (Exception) { }
            }
        }

        private void LoadScenarioFromItemsCore(IList<ScenarioItem> items, string name)
        {
            string json = ScenarioBuilder.BuildScenarioJson(name, _theme, items);
            int st = _engine.LoadScenario(json);
            if (st != LuminaStatus.Ok)
            {
                Status("El núcleo rechazó el escenario (" + LuminaStatus.Name(st) + ").");
                return;
            }
            _slides.Clear();
            _slides.AddRange(ScenarioBuilder.FlattenScenario(json,
                delegate(string songJson) { return LuminaEngine.SongParse(songJson); }));
            if (_api != null) _api.SetSlideCatalog(_slides);
            if (_remote != null) _remote.SetSlideCatalog(_slides);
            StopVideo();
            _lastScenarioItems = new List<ScenarioItem>(items);
            _lastScenarioName = name;
            RefreshLiveList();
            NavigateToIndex(0);
            pageExport.lblExportInfo.Text = "Escenario activo: «" + name + "» — " + _slides.Count +
                " diapositiva(s) listas para exportar (página Exportar).";
            Status("Escenario «" + name + "» cargado: " + _slides.Count + " slide(s).");
        }

        internal void LoadSongToStage()
        {
            if (!RequireEngine()) return;
            Song s = ScenarioBuilder.SongFromEditor(
                pageSongs.txtTitle.Text, pageSongs.txtArtist.Text, pageSongs.txtLyrics.Text,
                pageSongs.chkHymnMode.IsChecked == true, pageSongs.numTranspose.IntValue);
            if (s.Title.Length == 0) s.Title = "Sin título";
            if (s.Blocks.Count == 0)
            {
                Status("La letra está vacía: escribe bloques como [Verso 1] / [Coro].");
                return;
            }
            LoadScenarioFromItems(new ScenarioItem[] { ScenarioBuilder.FromSong(s) }, s.Title);
        }

        internal void LoadScriptureToStage()
        {
            if (!RequireEngine()) return;
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0)
            {
                Status("Escribe una referencia (p. ej. Jn 3:16-18).");
                return;
            }
            string version = pageBible.txtVersion.Text.Trim();
            string versesText = ResolveScriptureText(reference, version);
            ScenarioItem it = ScenarioBuilder.ScriptureItem(
                reference, version, pageBible.numVerses.IntValue, versesText);
            // v6.0.0: si el pasaje viene de una búsqueda, la palabra buscada se
            // resalta EN PROYECCIÓN (color de acento, insensible a caja/acento).
            it.Highlight = _lastBibleSearchTerm;
            if (!_dbOpen)
                Status("Nota: sin BD abierta el núcleo no puede resolver los versículos.");
            LoadScenarioFromItems(new ScenarioItem[] { it }, reference);
            if (version.Length > 0)
            {
                _settings.LastBibleVersion = version;
                SaveSettings();
            }
        }

        /// <summary>Texto de los versículos de una referencia desde la BD del motor.</summary>
        private string ResolveScriptureText(string reference, string version)
        {
            if (_engine == null || !_dbOpen) return string.Empty;
            try
            {
                string resJson = LuminaEngine.BibleRefResolve(reference);
                Dictionary<string, object> o = MiniJson.Parse(resJson);
                if (MiniJson.GetInt(o, "valid", 0) != 1) return string.Empty;
                long book = MiniJson.GetInt(o, "book", 0);
                long chapter = MiniJson.GetInt(o, "chapter", 0);
                long verse = MiniJson.GetInt(o, "verse", 0);
                long vFrom = verse > 0 ? verse : 1;
                const int kMaxVerses = 120;
                string q = LuminaStorage.BuildExecJson(
                    "SELECT text FROM bible WHERE version=?1 AND book=?2 AND chapter=?3 " +
                    "AND verse>=?4 AND verse<=?5 ORDER BY verse",
                    new object[] { version, book, chapter, vFrom, vFrom + kMaxVerses - 1 });
                string rows = _engine.DbExec(q);
                List<List<object>> r = LuminaStorage.Rows(rows);
                StringBuilder sb = new StringBuilder();
                foreach (List<object> row in r)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(Cell(row, 0));
                }
                return sb.ToString();
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }

        /// <summary>Texto en vivo para /api/live.txt (API y mando remoto).</summary>
        internal string BuildLiveText()
        {
            if (_currentIndex >= 0 && _currentIndex < _slides.Count)
            {
                SlideView v = _slides[_currentIndex];
                StringBuilder sb = new StringBuilder();
                if (v.RefLabel.Length > 0) sb.Append(v.RefLabel).Append('\n');
                foreach (string l in v.Lines) sb.Append(l).Append('\n');
                return sb.ToString().TrimEnd('\n');
            }
            return string.Empty;
        }

        /* ======================================================================
         *  SALIDAS AUXILIARES (video · escenario · director · zócalo)
         * ==================================================================== */

        /// <summary>Sincroniza salidas extra tras cada cambio de slide.</summary>
        private void SyncLiveExtras(int itemIndex)
        {
            ScenarioItem curItem = null;
            if (_lastScenarioItems != null && itemIndex >= 0 && itemIndex < _lastScenarioItems.Count)
                curItem = _lastScenarioItems[itemIndex];

            // ---- VIDEO: interceptar ítems de video ----
            if (curItem != null && curItem.Kind == "video" && curItem.VideoPath.Length > 0 &&
                _currentIndex >= 0 && !_black && IsWindows())
            {
                if (_videoForm == null)
                {
                    _videoForm = new lumina.ui.VideoPlayerForm();
                    _videoForm.MediaEnded += OnVideoEnded;
                }
                if (_videoSlideIndex != _currentIndex)
                {
                    _videoSlideIndex = _currentIndex;
                    bool ok = _videoForm.Play(curItem.VideoPath, ProjectorScreenBounds(),
                        curItem.VideoLoop, curItem.VideoVolume);
                    Status(ok ? "Reproduciendo video: " + Path.GetFileName(curItem.VideoPath)
                              : "El video no pudo iniciarse (¿Windows Media Player?).");
                }
            }
            else
            {
                StopVideo();
            }

            UpdateStageView(curItem);
            UpdateDirectorView(curItem, itemIndex);

            if (_settings.ObsTextSource.Length > 0)
            {
                ObsSendLiveText(BuildLiveText());
            }

            Dictionary<string, object> jsCtx = new Dictionary<string, object>();
            jsCtx["index"] = _currentIndex;
            jsCtx["item"] = itemIndex;
            if (curItem != null)
            {
                jsCtx["kind"] = curItem.Kind;
                jsCtx["title"] = curItem.Title;
                jsCtx["path"] = curItem.Kind == "video" ? curItem.VideoPath : curItem.ImagePath;
            }
            // v6.1.0 «GUION»: los módulos JS ven los MISMOS eventos que los
            // activadores (payload JSON para jsonParse del preámbulo).
            FireJsEvent("slide_changed", jsCtx);
            if (curItem != null)
            {
                FireJsEvent("item_changed", jsCtx);
                if (curItem.Kind == "song" && _lastItemIndex != itemIndex)
                    FireJsEvent("song_started", jsCtx);
            }

            if (_triggers != null && _settings.TriggersEnabled)
            {
                _triggers.Fire("slide_changed", jsCtx);
                if (curItem != null)
                {
                    _triggers.Fire("item_changed", jsCtx);
                    if (curItem.Kind == "song" && _lastItemIndex != itemIndex)
                        _triggers.Fire("song_started", jsCtx);
                }
            }
            _lastItemIndex = itemIndex;
        }

        private void StopVideo()
        {
            if (_videoForm != null && _videoSlideIndex >= 0)
            {
                _videoForm.StopAndHide();
                _videoSlideIndex = -1;
            }
        }

        private void OnVideoEnded(object sender, EventArgs e)
        {
            try
            {
                Dispatcher.BeginInvoke((Action)(delegate
                {
                    string path = _videoForm != null ? _videoForm.CurrentPath : string.Empty;
                    Dictionary<string, object> vctx = new Dictionary<string, object>();
                    vctx["item"] = _lastItemIndex;
                    vctx["path"] = path;
                    vctx["title"] = Path.GetFileName(path);
                    FireJsEvent("video_ended", vctx);          // v6.1.0 «GUION»
                    if (_triggers != null && _settings.TriggersEnabled)
                    {
                        _triggers.Fire("video_ended", vctx);
                    }
                    if (_settings.AutoAdvanceVideo && _videoSlideIndex >= 0 && RequireEngine())
                    {
                        _videoSlideIndex = -1;
                        _engine.Next();
                    }
                    else
                    {
                        StopVideo();
                    }
                }));
            }
            catch (Exception) { }
        }

        /// <summary>Monitor de escenario: abre en la pantalla configurada.</summary>
        internal void OpenStageView()
        {
            if (!IsWindows()) { Status("El monitor de escenario requiere Windows."); return; }
            if (_stageForm == null || _stageForm.IsDisposed)
                _stageForm = new lumina.ui.StageViewForm();
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(_settings.StageScreen, screens.Length - 1));
            _stageForm.OpenOn(screens[idx]);
            UpdateStageView(null);
            Status("Monitor de escenario abierto (pantalla " + idx + "). Escape para cerrar.");
        }

        private void UpdateStageView(ScenarioItem curItem)
        {
            if (_stageForm == null || _stageForm.IsDisposed || !_stageForm.Visible) return;
            List<string> cur = new List<string>();
            if (_currentIndex >= 0 && _currentIndex < _slides.Count)
                cur = _slides[_currentIndex].Lines;
            List<string> next = new List<string>();
            if (_currentIndex + 1 < _slides.Count)
                next = _slides[_currentIndex + 1].Lines;
            string refLabel = _currentIndex >= 0 && _currentIndex < _slides.Count
                ? _slides[_currentIndex].RefLabel : string.Empty;
            string nextItemTitle = string.Empty;
            if (_lastScenarioItems != null && curItem != null)
            {
                int nextIdx = _lastItemIndex + 1;
                if (nextIdx >= 0 && nextIdx < _lastScenarioItems.Count)
                    nextItemTitle = _lastScenarioItems[nextIdx].Title;
            }
            _stageForm.SetState(curItem != null ? curItem.Title : string.Empty,
                nextItemTitle, refLabel, cur, next);
        }

        /// <summary>3ª salida: pantalla del director.</summary>
        internal void OpenDirectorView()
        {
            if (!IsWindows()) { Status("La pantalla del director requiere Windows."); return; }
            if (_directorForm == null || _directorForm.IsDisposed)
                _directorForm = new lumina.ui.DirectorForm();
            System.Windows.Forms.Screen[] screens = System.Windows.Forms.Screen.AllScreens;
            int idx = Math.Max(0, Math.Min(_settings.DirectorScreen, screens.Length - 1));
            _directorForm.OpenOn(screens[idx]);
            // v6.0.0: canal de persistencia de notas (índice -1 = sin ítem).
            _directorNotesItemIndex = -1;
            _directorForm.NotesEdited += OnDirectorNotesEdited;
            UpdateDirectorView(null, -1);
            Status("Pantalla del director abierta (pantalla " + idx + "). Escape para cerrar; " +
                   "Espacio/R = cronómetro; notas abajo.");
        }

        private void UpdateDirectorView(ScenarioItem curItem, int itemIndex)
        {
            if (_directorForm == null || _directorForm.IsDisposed || !_directorForm.Visible) return;

            // v6.0.0: notas del director — se cargan SOLO al cambiar de ítem
            // (en cada slide se conservaría lo que el director está escribiendo).
            if (itemIndex != _directorNotesItemIndex)
            {
                _directorNotesItemIndex = itemIndex;
                _directorForm.LoadNotes(curItem != null ? curItem.Notes : string.Empty);
            }
            List<string> cur = new List<string>();
            if (curItem != null) cur.AddRange(DirectorItemLines(curItem));
            List<string> next = new List<string>();
            string nextTitle = string.Empty;
            if (_lastScenarioItems != null && _lastItemIndex + 1 >= 0 &&
                _lastItemIndex + 1 < _lastScenarioItems.Count)
            {
                ScenarioItem nextItem = _lastScenarioItems[_lastItemIndex + 1];
                nextTitle = nextItem.Title;
                next.AddRange(DirectorItemLines(nextItem));
            }
            string refLabel = _currentIndex >= 0 && _currentIndex < _slides.Count
                ? _slides[_currentIndex].RefLabel : string.Empty;
            _directorForm.SetState(curItem != null ? curItem.Title : string.Empty,
                nextTitle, refLabel, cur, next);
        }

        /// <summary>v6.0.0: edición de notas del director → ítem actual del culto.</summary>
        private void OnDirectorNotesEdited(string text)
        {
            if (_lastScenarioItems == null || _lastItemIndex < 0 ||
                _lastItemIndex >= _lastScenarioItems.Count) return;
            ScenarioItem it = _lastScenarioItems[_lastItemIndex];
            if (it != null) it.Notes = text ?? string.Empty;
        }

        private static List<string> DirectorItemLines(ScenarioItem it)
        {
            List<string> lines = new List<string>();
            if (it == null) return lines;
            if (it.Kind == "song" && it.Song != null)
            {
                string raw = it.Song.LyricsText();
                foreach (string l in raw.Replace("\r\n", "\n").Split('\n'))
                {
                    string t = l.Trim();
                    if (t.Length > 0 && !t.StartsWith("[", StringComparison.Ordinal))
                        lines.Add(t);
                }
            }
            else if (it.Kind == "text" && it.Text.Length > 0)
            {
                foreach (string l in it.Text.Replace("\r\n", "\n").Split('\n'))
                    if (l.Trim().Length > 0) lines.Add(l.Trim());
            }
            else if (it.Kind == "scripture" && it.Text.Length > 0)
            {
                foreach (string l in it.Text.Replace("\r\n", "\n").Split('\n'))
                    if (l.Trim().Length > 0) lines.Add(l.Trim());
            }
            else if (it.Kind == "video")
            {
                lines.Add("(video en reproducción: " + Path.GetFileName(it.VideoPath) + ")");
            }
            else if (it.Kind == "image")
            {
                lines.Add("(imagen: " + Path.GetFileName(it.ImagePath) + ")");
                if (it.Text.Length > 0) lines.Add(it.Text);
            }
            else if (it.Title.Length > 0)
            {
                lines.Add(it.Title);
            }
            return lines;
        }

        /// <summary>Pausa/reanuda el video en vivo (tecla P / botón).</summary>
        internal void ToggleVideoPause()
        {
            if (_videoForm == null || _videoSlideIndex < 0)
            {
                Status("No hay video en reproducción.");
                return;
            }
            _videoForm.PauseOrResume();
            Status("Video en pausa/reanudado (P alterna).");
        }

        /// <summary>Muestra el zócalo (avisos/acciones show_text).</summary>
        internal void ShowLowerThird(string line1, string line2, int seconds)
        {
            if (!IsWindows()) { Status("El zócalo requiere Windows."); return; }
            if (_lowerThird == null || _lowerThird.IsDisposed)
                _lowerThird = new lumina.ui.LowerThirdsOverlay();
            _lowerThird.Show(line1, line2, ProjectorScreenBounds(), seconds);
            Status("Zócalo: " + line1);
        }

        /// <summary>Audio oculto (acción play_audio) vía WMP late-bound.</summary>
        private void PlayAudioHidden(string path, int volume)
        {
            if (!IsWindows()) return;
            if (_audioForm == null)
            {
                _audioForm = new lumina.ui.VideoPlayerForm();
                _audioForm.Visible = false;
            }
            _audioForm.PlayAudio(path, volume);
        }

        /* ------------------------------------------------- diálogo del zócalo */

        internal void ShowLowerThirdDialog()
        {
            Window dlg = new Window();
            dlg.Title = "Aviso en pantalla (zócalo)";
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.SizeToContent = SizeToContent.WidthAndHeight;
            dlg.Background = FindResource("PageBrush") as Brush;
            dlg.MinWidth = 420;

            StackPanel sp = new StackPanel { Margin = new Thickness(18) };
            System.Windows.Controls.TextBox t1 = new System.Windows.Controls.TextBox();
            System.Windows.Controls.TextBox t2 = new System.Windows.Controls.TextBox();
            sp.Children.Add(new TextBlock { Text = "Línea principal:", Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 0, 0, 4) });
            sp.Children.Add(t1);
            sp.Children.Add(new TextBlock { Text = "Línea secundaria (opcional):", Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 10, 0, 4) });
            sp.Children.Add(t2);

            StackPanel btns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 14, 0, 0) };
            LumButton ok = new LumButton { Text = "Mostrar", Variant = "Primary", MinWidth = 110, Margin = new Thickness(0, 0, 8, 0) };
            LumButton hide = new LumButton { Text = "Ocultar zócalo", Variant = "Secondary", MinWidth = 120 };
            btns.Children.Add(ok);
            btns.Children.Add(hide);
            sp.Children.Add(btns);
            dlg.Content = sp;

            ok.Click += delegate
            {
                if (t1.Text.Trim().Length > 0) ShowLowerThird(t1.Text.Trim(), t2.Text.Trim(), 8);
                dlg.Close();
            };
            hide.Click += delegate
            {
                if (_lowerThird != null) _lowerThird.HideNow();
                dlg.Close();
            };
            dlg.ShowDialog();
        }
    }
}
