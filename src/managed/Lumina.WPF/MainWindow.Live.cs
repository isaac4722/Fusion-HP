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
            // v7.1.0 «OPERADOR» (feedback #3): el botón/F5 es un INTERRUPTOR:
            // visible → oculta (ProjectorHide); oculto → abre. La ventana
            // nativa es SIEMPRE sin bordes (WS_POPUP) y pantalla completa por
            // defecto. Cerrar con X/ESC ya no duplica la ventana y se puede
            // reabrir cuantas veces sea (el hilo sobrevive).
            if (_projectorVisible)
            {
                int st = _engine.ProjectorHide();
                if (st == LuminaStatus.Ok) Status("Proyector oculto (la proyección se reanuda al volver a abrir).");
                else Status("El núcleo no pudo ocultar la salida (" + LuminaStatus.Name(st) + ").");
                return;
            }
            int screen = pageLive.SelectedScreenIndex();
            bool fullscreen = pageLive.IsFullscreenChecked();
            int st2 = _engine.ProjectorShow(screen, fullscreen);
            if (st2 == LuminaStatus.Ok)
            {
                _projectorVisible = true;
                UpdateProjectorUi();
                Status("Proyector en pantalla " + screen +
                       (fullscreen ? " (sin bordes, pantalla completa)." : " (sin bordes, 960×540)."));
            }
            else
            {
                Status("El núcleo no puede proyectar (" + LuminaStatus.Name(st2) + "). " +
                      "La proyección nativa requiere el núcleo Windows (no headless).");
            }
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #4): al cambiar de monitor con la
        /// proyección abierta, la MISMA ventana se recrea sobre el nuevo
        /// monitor (sin abrir otra ventana).
        /// </summary>
        internal void ReopenProjectorOnScreenChange()
        {
            if (_engine == null || !_projectorVisible) return;
            try
            {
                _engine.ProjectorShow(pageLive.SelectedScreenIndex(),
                                      pageLive.IsFullscreenChecked());
            }
            catch (Exception) { }
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #3): sondeo ligero del estado del
        /// proyector — detecta el cierre con X/ESC del usuario (el núcleo
        /// informa render.visible) y actualiza el interruptor.
        /// </summary>
        internal void PollProjectorState()
        {
            if (_engine == null || _engine.IsDisposed) return;
            try
            {
                string json = _engine.StateJson();
                if (string.IsNullOrEmpty(json)) return;
                Dictionary<string, object> st = MiniJson.Parse(json);
                Dictionary<string, object> render = MiniJson.GetObject(st, "render");
                bool visible = render != null && MiniJson.GetInt(render, "visible", 0) == 1;
                if (visible != _projectorVisible)
                {
                    _projectorVisible = visible;
                    UpdateProjectorUi();
                }
            }
            catch (Exception) { }
        }

        private void UpdateProjectorUi()
        {
            try { pageLive.SetProjectorVisible(_projectorVisible); }
            catch (Exception) { }
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
            if (_remote != null) _remote.SetSlideCatalog(_slides);
            StopVideo();
            StopPowerPoint();
            _lastScenarioItems = new List<ScenarioItem>(items);
            _lastScenarioName = name;
            // v7.1.0 «OPERADOR» (feedback #1): ¿el origen del escenario en vivo
            // es la LISTA DEL CULTO? (para recargar en caliente al editar una
            // diapositiva Diseño sin tener que reenviar a mano)
            _serviceIsLive = (items != null && items == _serviceItems);
            // v7.1.0 «OPERADOR» (feedback #2/#4): nombre del escenario e ítems
            // visibles en la cabecera de En Vivo (modo Operador de dos niveles).
            pageLive.SetScenarioName(name);
            pageLive.SetItemsSource(_lastScenarioItems);
            RefreshLiveList();
            NavigateToIndex(0);
            pageExport.lblExportInfo.Text = "Escenario activo: «" + name + "» — " + _slides.Count +
                " diapositiva(s) listas para exportar (página Exportar).";
            Status("Escenario «" + name + "» cargado: " + _slides.Count + " slide(s).");
        }

        /// <summary>
        /// v7.1.0 «OPERADOR»: proyecta la slide por ÍNDICE GLOBAL (doble clic
        /// en un ítem de la lista En Vivo → su primera slide).
        /// </summary>
        internal void ShowSlideIndex(int index)
        {
            if (!RequireEngine()) return;
            if (index >= 0 && index < _slides.Count) _engine.ShowSlide(index);
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

            // ---- PPTX ORIGINAL (v7.1.0 «OPERADOR», feedback #6) ----
            // ítem pptx en vivo → PowerPoint proyecta el ARCHIVO ORIGINAL;
            // salir del ítem → show cerrado (la ventana nativa reaparece).
            if (curItem != null && curItem.Kind == "pptx" && curItem.PptxPath.Length > 0 &&
                _currentIndex >= 0 && !_black && IsWindows())
            {
                if (_pptxShow == null) _pptxShow = new PowerPointShow();
                if (!_pptxShow.IsPlaying || _pptxShow.CurrentPath != curItem.PptxPath)
                {
                    _pptxSlideIndex = _currentIndex;
                    string err;
                    bool ok = _pptxShow.Play(curItem.PptxPath, ProjectorScreenBounds(), out err);
                    Status(ok
                        ? "Proyectando presentación (PowerPoint): " + Path.GetFileName(curItem.PptxPath)
                        : "La presentación no se pudo abrir: " + err +
                          ". La slide del culto muestra el nombre del archivo.");
                }
                else
                {
                    _pptxShow.BringToFront();
                }
            }
            else
            {
                StopPowerPoint();
            }

            UpdateStageView(curItem);
            UpdateDirectorView(curItem, itemIndex);

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

        /* ======================================================================
         *  PPTX ORIGINAL — PowerPoint COM (v7.1.0 «OPERADOR», feedback #6)
         * ==================================================================== */

        /// <summary>Cierra el show de PowerPoint (si había) — sin tumbar la app.</summary>
        internal void StopPowerPoint()
        {
            if (_pptxShow != null && _pptxShow.IsPlaying)
            {
                _pptxShow.Stop();
            }
            _pptxSlideIndex = -1;
        }

        /// <summary>
        /// v7.1.0: «siguiente» contextual — si el PPTX original está en
        /// pantalla, avanza DENTRO de PowerPoint (View.Next); si no, el
        /// motor de proyección nativo.
        /// </summary>
        internal void LiveNext()
        {
            if (_pptxShow != null && _pptxShow.IsPlaying && _pptxShow.NextSlide()) return;
            if (RequireEngine()) _engine.Next();
        }

        /// <summary>v7.1.0: «anterior» contextual (PowerPoint o motor).</summary>
        internal void LivePrev()
        {
            if (_pptxShow != null && _pptxShow.IsPlaying && _pptxShow.PrevSlide()) return;
            if (RequireEngine()) _engine.Prev();
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

        /// <summary>Ruta del último proyecto (para el mosaico «Continuar» de Inicio).</summary>
        internal string LastProjectPath
        {
            get { return _settings != null ? _settings.LastProjectPath : string.Empty; }
        }

        /// <summary>
        /// «Continuar» desde Inicio: carga el último plan guardado. true si se
        /// restauró algo (ítems > 0). Errores al log, nunca lanza.
        /// </summary>
        internal bool ResumeLastProject()
        {
            string path = _settings.LastProjectPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Status("No hay último proyecto guardado.");
                return false;
            }
            try
            {
                int added = ImportPlanCore(File.ReadAllText(path, new UTF8Encoding(false)));
                if (added > 0)
                {
                    SelectLastServiceItem();
                    RefreshPreview();
                    RememberProjectPath(path);
                    Status("Proyecto abierto: " + Path.GetFileName(path)
                        + " (" + added + " ítems).");
                    return true;
                }
                Status("El plan no trae ítems.");
                return false;
            }
            catch (Exception ex)
            {
                App.LogLine("ResumeLastProject: " + ex.Message);
                Status("El último proyecto no se pudo abrir (ver log).");
                return false;
            }
        }

        /* ======================================================================
         *  F1.06 «Arranque en Presentación» — la ventana abre en Inicio
         *  (NavigateToIndex(0), constructor) y RESTAURA el último plan de
         *  culto guardado (settings.json → lastProjectPath). La creación de
         *  contenido sigue siendo explícita por página/atajo; entrar/salir
         *  del editor no bloquea la salida.
         * ==================================================================== */

        /// <summary>
        /// F1.06: guarda la ruta del último proyecto (settings.json). Ruta
        /// absoluta para que el arranque la encuentre desde cualquier carpeta.
        /// </summary>
        internal void RememberProjectPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                _settings.LastProjectPath = Path.GetFullPath(path);
                _settings.Save();
            }
            catch (Exception ex)
            {
                try { App.LogLine("RememberProjectPath FALLO: " + ex.Message); } catch (Exception) { }
            }
        }

        /// <summary>
        /// F1.06: restauración silenciosa del último proyecto al arrancar.
        /// Nunca bloquea el arranque: cualquier fallo queda en el log y la
        /// ventana sigue en En Vivo con el escenario vacío (fail-safe).
        /// </summary>
        internal void RestoreLastProjectAtStartup()
        {
            string path = _settings.LastProjectPath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                int added = ImportPlanCore(File.ReadAllText(path, new UTF8Encoding(false)));
                if (added > 0)
                {
                    SelectLastServiceItem();
                    RefreshPreview();
                    Status("Último proyecto restaurado: " + Path.GetFileName(path)
                        + " (" + added + " ítems).");
                    App.LogLine("F1.06: último proyecto restaurado (" + added + " ítems).");
                }
                else
                {
                    Status("El último proyecto no trae ítems: escenario vacío.");
                }
            }
            catch (Exception ex)
            {
                App.LogLine("F1.06: no se pudo restaurar el último proyecto: " + ex.Message);
                Status("El último proyecto no se pudo restaurar (ver log).");
            }
        }

        /* ======================================================================
         *  F1.07 «Búsqueda en caliente» — cuadro visible durante la proyección
         *  (LivePage, Ctrl+K). Búsqueda GLOBAL de canciones y versículos vía
         *  FTS5 (el índice, NO la biblioteca completa). Seleccionar/agregar
         *  NO interrumpe la salida: el resultado se agrega al escenario
         *  pendiente y proyectar sigue siendo una acción explícita del
         *  operador (EN VIVO / doble clic).
         * ==================================================================== */

        /// <summary>Fila de resultado de la búsqueda en caliente (F1.07).</summary>
        internal sealed class HotResultVm
        {
            public string KindLabel = string.Empty;   // «Canción» | «Biblia»
            public string RefLabel = string.Empty;    // autor | «Juan 3:16 (RVR1909)»
            public string Detail = string.Empty;      // primera línea del texto
            public long SongId = -1;                  // >0 → canción de la biblioteca
            public string Reference = string.Empty;   // biblia: referencia cruda
            public string Version = string.Empty;     // biblia: versión
            public string VerseText = string.Empty;   // biblia: texto del versículo
        }

        /// <summary>Filtro FTS5 seguro (delegado a Core — testeable sin UI).</summary>
        private static string FtsQuote(string term)
        {
            return FtsQuery.Sanitize(term);
        }

        /// <summary>Ejecuta la búsqueda en caliente y llena el popup.</summary>
        internal void HotSearchRun(string term)
        {
            List<HotResultVm> results = new List<HotResultVm>();
            string fts = FtsQuote(term);
            if (fts.Length > 0 && _engine != null && _dbOpen)
            {
                // Canciones (biblioteca F2.12).
                try
                {
                    List<List<object>> rows = LuminaStorage.Rows(
                        _engine.DbExec(LuminaStorage.SearchSongsRequest(fts, 8)));
                    foreach (List<object> r in rows)
                    {
                        HotResultVm vm = new HotResultVm();
                        vm.KindLabel = "Canción";
                        vm.RefLabel = r.Count > 2 ? Cell(r, 2) : string.Empty;
                        string lyrics = r.Count > 3 ? Cell(r, 3) : string.Empty;
                        int nl = lyrics.IndexOf('\n');
                        vm.Detail = nl > 0 ? lyrics.Substring(0, nl) : lyrics;
                        if (vm.Detail.Length > 90) vm.Detail = vm.Detail.Substring(0, 90) + "…";
                        long id;
                        long.TryParse(r.Count > 0 ? Cell(r, 0) : string.Empty, out id);
                        vm.SongId = id;
                        results.Add(vm);
                    }
                }
                catch (Exception ex) { App.LogLine("HotSearch canciones: " + ex.Message); }
                // Versículos (búsqueda por palabra, F2.13/F1.07).
                try
                {
                    List<List<object>> rows = LuminaStorage.Rows(
                        _engine.DbExec(LuminaStorage.SearchVersesRequest(fts, 8)));
                    foreach (List<object> r in rows)
                    {
                        HotResultVm vm = new HotResultVm();
                        vm.KindLabel = "Biblia";
                        vm.Version = r.Count > 0 ? Cell(r, 0) : string.Empty;
                        string refLabel = BooksTable.AbbrOf((int)(r.Count > 1 ? ToLong(r, 1) : 0))
                            + " " + (r.Count > 2 ? ToLong(r, 2) : 0).ToString()
                            + ":" + (r.Count > 3 ? ToLong(r, 3) : 0).ToString();
                        vm.RefLabel = refLabel + " (" + vm.Version + ")";
                        vm.Reference = refLabel;
                        vm.VerseText = r.Count > 4 ? Cell(r, 4) : string.Empty;
                        vm.Detail = vm.VerseText;
                        if (vm.Detail.Length > 90) vm.Detail = vm.Detail.Substring(0, 90) + "…";
                        results.Add(vm);
                    }
                }
                catch (Exception ex) { App.LogLine("HotSearch versículos: " + ex.Message); }
            }
            pageLive.ShowHotResults(results, results.Count > 0);
        }

        /// <summary>Agrega el resultado seleccionado al escenario SIN proyectar.</summary>
        internal void HotSearchAddSelected()
        {
            HotResultVm r = pageLive.SelectedHotResult();
            if (r != null) HotSearchAdd(r);
        }

        /// <summary>Convierte un resultado en ítem del culto (sin tocar la salida).</summary>
        internal void HotSearchAdd(HotResultVm r)
        {
            if (r == null) return;
            try
            {
                if (r.SongId > 0)
                {
                    // Canción completa desde la biblioteca.
                    string res = _engine.DbExec(LuminaStorage.SelectSongByIdRequest(r.SongId));
                    List<List<object>> rows = LuminaStorage.Rows(res);
                    if (rows.Count == 0) { Status("La canción ya no existe."); return; }
                    List<object> row = rows[0];
                    string title = row.Count > 1 ? Cell(row, 1) : "Canción";
                    string artist = row.Count > 2 ? Cell(row, 2) : string.Empty;
                    string lyrics = row.Count > 6 ? Cell(row, 6) : string.Empty;
                    Song s = ScenarioBuilder.SongFromEditor(title, artist, lyrics, false, 0);
                    _serviceItems.Add(ScenarioBuilder.FromSong(s));
                    try { _engine.DbExec(LuminaStorage.RecordSongUseRequest(r.SongId)); }
                    catch (Exception) { }
                }
                else if (r.Reference.Length > 0)
                {
                    // Versículo: ítem scripture con el texto ya resuelto.
                    ScenarioItem it = ScenarioBuilder.ScriptureItem(
                        r.Reference, r.Version, 1, r.VerseText);
                    it.Title = r.Reference + " (" + r.Version + ")";
                    _serviceItems.Add(it);
                }
                RefreshServiceList();
                SelectLastServiceItem();
                RefreshPreview();
                Status("Agregado al escenario (la proyección NO cambia hasta que lo pidas): "
                    + (r.SongId > 0 ? r.RefLabel : r.Reference));
            }
            catch (Exception ex)
            {
                Status("No se pudo agregar el resultado: " + ex.Message);
            }
        }

        /// <summary>long de celda (para book/chapter/verse del índice bíblico).</summary>
        private static long ToLong(List<object> row, int i)
        {
            if (row == null || i >= row.Count || row[i] == null) return 0;
            long v;
            return long.TryParse(row[i].ToString(), out v) ? v : 0;
        }
    }
}
