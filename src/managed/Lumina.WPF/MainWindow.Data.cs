// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/MainWindow.Data.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  MainWindow.Data.cs (3/4): canciones (BD/FTS, transposición), Biblia
//  (referencias, búsqueda por palabra, importadores .BIB/ZEFania/JSON y
//  biblia de fábrica), culto (ítems, planes JSON) y exportación PPTX/PDF.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using lumina.bridge;
using lumina.core;

namespace lumina.wpf
{
    public partial class MainWindow : Window
    {
        private static string Cell(List<object> row, int idx)
        {
            if (row == null || idx >= row.Count || row[idx] == null) return string.Empty;
            return Convert.ToString(row[idx], CultureInfo.InvariantCulture) ?? string.Empty;
        }

        internal bool RequireDb()
        {
            if (_dbOpen) return true;
            Status("Abre (o crea) una base de datos en Ajustes → Base de datos.");
            return false;
        }

        /* ======================================================================
         *  CANCIONES — BD
         * ==================================================================== */

        internal void SaveSongToDb()
        {
            if (!RequireEngine() || !RequireDb()) return;
            string lyrics = pageSongs.txtLyrics.Text ?? string.Empty;
            string title = pageSongs.txtTitle.Text.Trim();
            if (title.Length == 0) { Status("La canción necesita título."); return; }
            try
            {
                string res = _engine.DbExec(LuminaStorage.InsertSongRequest(
                    title, pageSongs.txtArtist.Text.Trim(), string.Empty, 0, string.Empty, lyrics));
                long id = LuminaStorage.LastId(res);
                Status("Canción guardada en la BD (id " + id + ").");
            }
            catch (LuminaException ex)
            {
                Status("No se pudo guardar: " + ex.Message);
            }
        }

        internal void SearchSongs()
        {
            if (!RequireEngine() || !RequireDb()) return;
            // v7.1.0 «OPERADOR» (feedback #7): la biblioteca COMPLETA se ve SIN
            // buscar — el cuadro de texto es un FILTRO opcional (como
            // Holyrics: toda la biblioteca a un clic).
            string term = pageSongs.txtSearch.Text.Trim();
            try
            {
                string res = term.Length == 0
                    ? _engine.DbExec(LuminaStorage.ListAllSongsRequest(1000))
                    : _engine.DbExec(LuminaStorage.SearchSongsRequest(term, 50));
                List<List<object>> rows = LuminaStorage.Rows(res);
                List<SongRowVm> vms = new List<SongRowVm>();
                foreach (List<object> row in rows)
                {
                    SongRowVm vm = new SongRowVm();
                    vm.Id = Cell(row, 0);
                    vm.Title = Cell(row, 1);
                    vm.Author = Cell(row, 2);
                    string lyrics = Cell(row, 3);
                    vm.Snippet = lyrics.Length > 80 ? lyrics.Substring(0, 80) + "…" : lyrics;
                    vms.Add(vm);
                }
                pageSongs.FillResults(vms);
                pageSongs.lblResultCount.Text = term.Length == 0
                    ? rows.Count + " canciones en la biblioteca (completa — el cuadro filtra)"
                    : rows.Count + " resultado(s) · filtro «" + term + "»";
                Status(term.Length == 0
                    ? "Biblioteca completa: " + rows.Count + " canción(es)."
                    : "Búsqueda «" + term + "»: " + rows.Count + " resultado(s).");
            }
            catch (LuminaException ex)
            {
                Status("Búsqueda falló: " + ex.Message);
            }
        }

        internal void LoadResultToEditor()
        {
            SongRowVm vm = pageSongs.SelectedResult();
            long id;
            if (vm == null || !long.TryParse(vm.Id, out id)) return;
            try
            {
                string res = _engine.DbExec(LuminaStorage.SelectSongByIdRequest(id));
                List<List<object>> rows = LuminaStorage.Rows(res);
                if (rows.Count == 0) { Status("La canción ya no existe."); return; }
                List<object> row = rows[0];
                pageSongs.txtTitle.Text = row.Count > 1 ? Cell(row, 1) : string.Empty;
                pageSongs.txtArtist.Text = row.Count > 2 ? Cell(row, 2) : string.Empty;
                pageSongs.txtLyrics.Text = row.Count > 6 ? Cell(row, 6) : string.Empty;
                Status("Canción #" + id + " cargada al editor.");
            }
            catch (LuminaException ex)
            {
                Status("No se pudo leer la canción: " + ex.Message);
            }
        }

        /* ------------------------------------------------ transposición v4.2.0 */

        internal void TransposeEditorNow()
        {
            if (!RequireEngine()) return;
            string raw = pageSongs.txtLyrics.Text ?? string.Empty;
            if (raw.Length == 0) { Status("La letra está vacía: nada que transponer."); return; }
            int semi = pageSongs.numTranspose.IntValue;
            if (semi == 0) { Status("Desplazamiento 0: ajusta «Transponer» antes de aplicar."); return; }

            string[] lines = raw.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder();
            int changed = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ChordUtil.IsChordLine(ln))
                {
                    try
                    {
                        Dictionary<string, object> o = MiniJson.Parse(LuminaEngine.ChordsTranspose(ln, semi, true));
                        ln = MiniJson.GetString(o, "line", ln);
                        changed++;
                    }
                    catch (Exception) { }
                }
                sb.Append(ln);
                if (i < lines.Length - 1) sb.Append("\r\n");
            }
            if (changed == 0)
            {
                Status("No hay líneas de acordes: escribe el cifrado sobre la letra (p. ej. «Do Sol»).");
                return;
            }
            pageSongs.txtLyrics.Text = sb.ToString();
            pageSongs.numTranspose.Value = 0;
            Status("Acordes transpuestos " + (semi > 0 ? "+" : "") + semi +
                   " semitonos (notación latina) — " + changed + " línea(s).");
        }

        /* ======================================================================
         *  BIBLIA
         * ==================================================================== */

        internal void ResolveReference()
        {
            if (!RequireEngine()) return;
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0)
            {
                pageBible.lblResolve.Text = "Escribe una referencia.";
                return;
            }
            // v6.0.0: resolver una referencia es una acción MANUAL: el término
            // de la última búsqueda ya no aplica como resaltado del pasaje.
            _lastBibleSearchTerm = string.Empty;
            try
            {
                string resJson = LuminaEngine.BibleRefResolve(reference);
                Dictionary<string, object> o = MiniJson.Parse(resJson);
                long book = MiniJson.GetInt(o, "book", 0);
                long chapter = MiniJson.GetInt(o, "chapter", 0);
                long verse = MiniJson.GetInt(o, "verse", 0);
                string name = MiniJson.GetString(o, "name", "?");
                long valid = MiniJson.GetInt(o, "valid", 0);
                pageBible.lblResolve.Text = valid == 1
                    ? string.Format(CultureInfo.InvariantCulture,
                        "{0} → libro {1}, capítulo {2}, versículo {3} ({4})",
                        reference, book, chapter, verse, name)
                    : "Referencia no reconocida: " + reference;
                pageBible.lblResolve.Foreground = valid == 1
                    ? OkBrush : ErrBrush;
            }
            catch (LuminaException ex)
            {
                pageBible.lblResolve.Text = "Error del núcleo: " + ex.Message;
                pageBible.lblResolve.Foreground = ErrBrush;
            }
            catch (FormatException)
            {
                pageBible.lblResolve.Text = "Respuesta inválida del núcleo.";
                pageBible.lblResolve.Foreground = ErrBrush;
            }
            catch (Exception ex)
            {
                pageBible.lblResolve.Text = "Núcleo no disponible: " + ex.Message;
                pageBible.lblResolve.Foreground = ErrBrush;
            }
        }

        /* -------------------------------------------- búsqueda por palabra FTS */

        private static string FtsTerm(string raw)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in (raw ?? string.Empty))
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (char.IsWhiteSpace(c)) break;
            }
            return sb.ToString().ToLowerInvariant();
        }

        private static string HighlightTerm(string text, string term)
        {
            if (string.IsNullOrEmpty(text) || term.Length == 0) return text;
            int idx = text.IndexOf(term, StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return text;
            return text.Substring(0, idx) + "«" + text.Substring(idx, term.Length) + "»" +
                   text.Substring(idx + term.Length);
        }

        internal void SearchBibleWords()
        {
            if (!RequireEngine() || !RequireDb()) return;
            string term = FtsTerm(pageBible.txtSearch.Text);
            if (term.Length == 0)
            {
                Status("Escribe una palabra para buscar en la Biblia (p. ej. «misericordia»).");
                return;
            }
            // v6.0.0: el término queda pegado al último resultado — al cargar al
            // escenario/agregar al culto se propaga como «highlight» y el motor
            // resalta las coincidencias EN PROYECCIÓN (color de acento).
            _lastBibleSearchTerm = term;
            try
            {
                string sql = "SELECT version, book, chapter, verse, text FROM bible_fts " +
                             "WHERE bible_fts MATCH ?1 ORDER BY book, chapter, verse LIMIT 200";
                string req = LuminaStorage.BuildExecJson(sql, term + "*");
                string res = _engine.DbExec(req);
                List<List<object>> rows = LuminaStorage.Rows(res);
                List<BibleRowVm> vms = new List<BibleRowVm>();
                foreach (List<object> row in rows)
                {
                    string version = Cell(row, 0);
                    long book, chapter, verse;
                    long.TryParse(Cell(row, 1), out book);
                    long.TryParse(Cell(row, 2), out chapter);
                    long.TryParse(Cell(row, 3), out verse);
                    string text = Cell(row, 4);
                    string name = BooksTable.NameOf((int)book);
                    string abbr = BooksTable.AbbrOf((int)book);
                    BibleRowVm vm = new BibleRowVm();
                    vm.Ref = (abbr.Length > 0 ? abbr : "libro " + book) + " " +
                        chapter + ":" + verse + (version.Length > 0 ? " (" + version + ")" : "");
                    vm.Text = HighlightTerm(text, term);
                    vm.LoadRef = BooksTable.Reference((int)book, (int)chapter, (int)verse, (int)verse);
                    vm.Version = version;
                    vms.Add(vm);
                }
                pageBible.FillResults(vms);
                pageBible.lblSearchInfo.Text = rows.Count + " versículo(s) con «" + term + "»" +
                    (rows.Count >= 200 ? " (límite alcanzado — afina la búsqueda)" : "") +
                    " · doble clic carga el pasaje";
                Status("Búsqueda bíblica «" + term + "»: " + rows.Count + " resultado(s).");
            }
            catch (LuminaException ex)
            {
                Status("Búsqueda bíblica falló: " + ex.Message);
            }
        }

        internal void LoadBibleResultToStage()
        {
            BibleRowVm vm = pageBible.SelectedResult();
            if (vm == null || vm.LoadRef.Length == 0)
            {
                Status("No se pudo derivar la referencia de ese versículo.");
                return;
            }
            _txtRef.Text = vm.LoadRef;
            pageBible.txtVersion.Text = vm.Version;
            LoadScriptureToStage();
        }

        /* ------------------------------ Biblia directa (v7.1.0 «OPERADOR») -- */

        /// <summary>
        /// v7.1.0 (feedback #7): versiones bíblicas INSTALADAS en la BD para los
        /// selectores de la página Biblia (con la última usada preseleccionada).
        /// </summary>
        internal void RefreshBibleVersions()
        {
            if (!RequireEngine() || !RequireDb()) return;
            try
            {
                string res = _engine.DbExec(LuminaStorage.ListBibleVersionsRequest());
                List<List<object>> rows = LuminaStorage.Rows(res);
                List<string> versions = new List<string>(rows.Count);
                foreach (List<object> row in rows)
                {
                    string v = Cell(row, 0);
                    if (v.Length > 0) versions.Add(v);
                }
                pageBible.FillVersions(versions);
                if (versions.Count > 0)
                {
                    string last = _settings.LastBibleVersion;
                    if (last.Length > 0 && versions.Contains(last) && pageBible.txtVersion.Text.Trim().Length == 0)
                        pageBible.txtVersion.Text = last;
                    Status("Versiones bíblicas instaladas: " + string.Join(", ", versions.ToArray()) + ".");
                }
            }
            catch (LuminaException ex)
            {
                Status("No se pudieron leer las versiones: " + ex.Message);
            }
        }

        /// <summary>
        /// v7.1.0 (feedback #7/#4): muestra el CAPÍTULO completo en la lista de
        /// resultados SIN proyectar (el operador lee; doble clic proyecta un
        /// versículo). Con «Comparar»: cada versículo muestra DOS versiones
        /// lado a lado.
        /// </summary>
        internal void ReadBibleChapter()
        {
            if (!RequireEngine() || !RequireDb()) return;
            int book = pageBible.SelectedBook();
            int chapter = pageBible.SelectedChapter();
            if (book < 1 || chapter < 1) { Status("Elige libro y capítulo."); return; }
            string version = pageBible.SelectedVersion();
            if (version.Length == 0) { Status("Elige la versión (o abre la BD)."); return; }
            string version2 = pageBible.IsCompareChecked() ? pageBible.SelectedVersion2() : string.Empty;

            Dictionary<long, string> versesA = ReadVerses(version, book, chapter);
            if (versesA.Count == 0)
            {
                Status("La versión «" + version + "» no trae " +
                       BooksTable.NameOf(book) + " " + chapter + ".");
                return;
            }
            Dictionary<long, string> versesB = version2.Length > 0
                ? ReadVerses(version2, book, chapter)
                : new Dictionary<long, string>();

            List<BibleRowVm> vms = new List<BibleRowVm>();
            List<long> numbers = new List<long>(versesA.Keys);
            numbers.Sort();
            foreach (long verse in numbers)
            {
                BibleRowVm vm = new BibleRowVm();
                vm.Version = version;
                vm.Ref = BooksTable.NameOf(book) + " " + chapter + ":" + verse +
                         (version2.Length > 0 ? "  ·  " + version : string.Empty);
                vm.Text = versesA[verse];
                if (version2.Length > 0)
                {
                    vm.VersionB = version2;
                    vm.TextB = versesB.ContainsKey(verse)
                        ? versesB[verse]
                        : "(sin este versículo en " + version2 + ")";
                }
                vm.LoadRef = BooksTable.Reference(book, chapter, (int)verse, (int)verse);
                vms.Add(vm);
            }
            pageBible.FillResults(vms);
            Status(BooksTable.NameOf(book) + " " + chapter + " (" + version + "): " +
                   vms.Count + " versículos" +
                   (version2.Length > 0 ? " · comparando con " + version2 : "") +
                   " — sin proyectar.");
        }

        private Dictionary<long, string> ReadVerses(string version, int book, int chapter)
        {
            Dictionary<long, string> map = new Dictionary<long, string>();
            if (version.Length == 0) return map;
            try
            {
                string req = LuminaStorage.SelectVersesRequest(version, book, chapter, 1, 200);
                List<List<object>> rows = LuminaStorage.Rows(_engine.DbExec(req));
                foreach (List<object> row in rows)
                {
                    long verse;
                    if (!long.TryParse(Cell(row, 0), out verse)) continue;
                    map[verse] = Cell(row, 1);
                }
            }
            catch (LuminaException) { }
            return map;
        }

        /// <summary>
        /// v7.1.0 (feedback #4): capítulo (o COMPARACIÓN de dos versiones) al
        /// escenario. Sin comparar: ítem scripture del capítulo. Comparando:
        /// un ítem de texto por GRUPO de versículos con ambas versiones
        /// etiquetadas (como las «dos versiones al mismo tiempo» de Holyrics).
        /// </summary>
        internal void LoadBibleChapterToStage()
        {
            if (!RequireEngine() || !RequireDb()) return;
            int book = pageBible.SelectedBook();
            int chapter = pageBible.SelectedChapter();
            if (book < 1 || chapter < 1) { Status("Elige libro y capítulo."); return; }
            string version = pageBible.SelectedVersion();
            if (version.Length == 0) { Status("Elige la versión (o abre la BD)."); return; }
            string version2 = pageBible.IsCompareChecked() ? pageBible.SelectedVersion2() : string.Empty;
            string bookName = BooksTable.NameOf(book);
            // referencia del capítulo completo ("jn 3" — el núcleo resuelve
            // capítulo sin versículo como 1..fin)
            string chapterRef = BooksTable.AbbrOf(book) + " " + chapter.ToString(CultureInfo.InvariantCulture);

            if (version2.Length == 0)
            {
                // ---- capítulo normal (scripture) ----
                string versesText = ResolveScriptureText(chapterRef, version);
                ScenarioItem it = ScenarioBuilder.ScriptureItem(
                    chapterRef, version, pageBible.numVerses.IntValue, versesText);
                // Título legible para las listas (el Ref lleva la referencia
                // RESUELTA por si el núcleo no encuentra la versión — fail-safe)
                it.Title = bookName + " " + chapter + " (" + version + ")";
                it.Ref = chapterRef;
                LoadScenarioFromItems(new ScenarioItem[] { it }, bookName + " " + chapter);
                return;
            }

            // ---- comparación de dos versiones ----
            Dictionary<long, string> versesA = ReadVerses(version, book, chapter);
            Dictionary<long, string> versesB = ReadVerses(version2, book, chapter);
            if (versesA.Count == 0 && versesB.Count == 0)
            {
                Status("Ninguna de las dos versiones trae " + bookName + " " + chapter + ".");
                return;
            }
            List<long> numbers = new List<long>(versesA.Keys);
            foreach (long v in versesB.Keys) if (!numbers.Contains(v)) numbers.Add(v);
            numbers.Sort();
            int per = pageBible.numVerses.IntValue;
            if (per < 1) per = 1;
            List<ScenarioItem> items = new List<ScenarioItem>();
            for (int i = 0; i < numbers.Count; i += per)
            {
                StringBuilder sb = new StringBuilder();
                for (int k = i; k < i + per && k < numbers.Count; k++)
                {
                    long verse = numbers[k];
                    string va = versesA.ContainsKey(verse) ? versesA[verse] : "—";
                    string vb = versesB.ContainsKey(verse) ? versesB[verse] : "—";
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(bookName).Append(' ').Append(chapter).Append(':').Append(verse)
                      .Append(" — ").Append(version).Append('\n').Append(va)
                      .Append('\n').Append(version2).Append('\n').Append(vb);
                }
                ScenarioItem it = ScenarioBuilder.TextItem(
                    bookName + " " + chapter + ":" + numbers[i] + " · " + version + " / " + version2,
                    sb.ToString(), 6);
                items.Add(it);
            }
            LoadScenarioFromItems(items, bookName + " " + chapter + " (comparación)");
            Status("Comparación " + version + " / " + version2 + " de " + bookName +
                   " " + chapter + ": " + items.Count + " slide(s).");
        }

        /* ------------------------------------------------ importadores de biblias */

        internal void ImportBibFile()
        {
            if (!RequireEngine()) return;
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Importar Biblia .BIB";
            dlg.Filter = "Biblia (*.bib)|*.bib|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            byte[] data;
            try { data = File.ReadAllBytes(dlg.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo leer el archivo: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string statsJson;
            try { statsJson = LuminaEngine.BibParse(data, "{\"wantRows\":1}"); }
            catch (LuminaException ex)
            {
                MessageBox.Show(this, "El archivo .BIB no es válido: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Dictionary<string, object> stats;
            try { stats = MiniJson.Parse(statsJson); }
            catch (FormatException)
            {
                MessageBox.Show(this, "Respuesta inválida del núcleo.", "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            long verses = MiniJson.GetInt(stats, "verses", 0);
            long books = MiniJson.GetInt(stats, "books", 0);
            string version = MiniJson.GetString(stats, "version", string.Empty);
            string name = MiniJson.GetString(stats, "name", string.Empty);
            long errors = MiniJson.GetInt(stats, "errors", 0);

            MessageBox.Show(this,
                "Biblia validada:\n" +
                "  Versión: " + version + (name.Length > 0 ? " (" + name + ")" : string.Empty) + "\n" +
                "  Libros: " + books + "\n" +
                "  Versículos: " + verses + "\n" +
                "  Filas descartadas: " + errors,
                "Importar Biblia", MessageBoxButton.OK, MessageBoxImage.Information);

            if (MessageBox.Show(this, "¿Insertar esta biblia en la base de datos?",
                    "Importar Biblia", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;

            if (!RequireDb()) return;

            List<object> rows = MiniJson.GetArray(stats, "rows");
            if (rows.Count == 0)
            {
                MessageBox.Show(this,
                    "El núcleo no devolvió filas para insertar (solo estadísticas):\n" +
                    "el archivo fue VALIDADO pero NO se insertó nada.\n\n" +
                    "Vuelve a intentarlo con un núcleo que soporte {\"wantRows\":1}.",
                    "Importar Biblia", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            InsertBibleRows(version, rows, verses);
        }

        internal void ImportZefaniaXml()
        {
            if (!RequireEngine()) return;
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Importar Biblia ZEFania XML";
            dlg.Filter = "Biblia ZEFania (*.xml)|*.xml|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;

            ZefaniaResult res;
            try { res = ZefaniaBible.Parse(dlg.FileName, null); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "El XML no se pudo leer: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (res.Verses.Count == 0)
            {
                MessageBox.Show(this,
                    "No se encontraron versículos.\n\n" +
                    "El formato esperado es ZEFania XML:\n" +
                    "  <XMLBIBLE biblename=\"…\">\n" +
                    "    <BIBLEBOOK bnumber=\"1\" …><CHAPTER cnumber=\"1\">\n" +
                    "      <VERSE vnumber=\"1\">texto…</VERSE>",
                    "Importar ZEFania", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string version = res.VersionName.Length > 0 ? res.VersionName : "Zefania";
            MessageBox.Show(this,
                "Biblia ZEFania validada:\n" +
                "  Versión: " + version + "\n" +
                "  Libros: " + res.BookCount + "\n" +
                "  Versículos: " + res.Verses.Count + "\n" +
                "  Filas descartadas: " + res.SkippedRows,
                "Importar ZEFania", MessageBoxButton.OK, MessageBoxImage.Information);

            if (MessageBox.Show(this, "¿Insertar esta biblia en la base de datos?",
                    "Importar ZEFania", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            if (!RequireDb()) return;

            List<object> rows = new List<object>(res.Verses.Count);
            foreach (ZefaniaVerse v in res.Verses)
            {
                List<object> row = new List<object>(5);
                row.Add(version);
                row.Add(v.Book);
                row.Add(v.Chapter);
                row.Add(v.Verse);
                row.Add(v.Text);
                rows.Add(row);
            }
            InsertBibleRows(version, rows, res.Verses.Count);
        }

        internal void ImportOsisXml()
        {
            if (!RequireEngine()) return;
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Importar Biblia OSIS XML";
            dlg.Filter = "Biblia OSIS (*.xml)|*.xml|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;

            OsisResult res;
            try { res = OsisBible.Parse(dlg.FileName, null); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "El XML no se pudo leer: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (res.Verses.Count == 0)
            {
                MessageBox.Show(this,
                    "No se encontraron versículos.\n\n" +
                    "El formato esperado es OSIS XML:\n" +
                    "  <osis><osisText osisIDWork=\"…\">\n" +
                    "    <div type=\"book\" osisID=\"Gen\"><chapter osisID=\"Gen.1\">\n" +
                    "      <verse osisID=\"Gen.1.1\">texto…</verse>",
                    "Importar OSIS", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string version = res.VersionName.Length > 0 ? res.VersionName : "OSIS";
            MessageBox.Show(this,
                "Biblia OSIS validada:\n" +
                "  Versión: " + version + "\n" +
                "  Libros: " + res.BookCount + "\n" +
                "  Versículos: " + res.Verses.Count + "\n" +
                "  Filas descartadas: " + res.SkippedRows,
                "Importar OSIS", MessageBoxButton.OK, MessageBoxImage.Information);

            if (MessageBox.Show(this, "¿Insertar esta biblia en la base de datos?",
                    "Importar OSIS", MessageBoxButton.YesNo, MessageBoxImage.Question)
                != MessageBoxResult.Yes) return;
            if (!RequireDb()) return;

            List<object> rows = new List<object>(res.Verses.Count);
            foreach (OsisVerse v in res.Verses)
            {
                List<object> row = new List<object>(5);
                row.Add(version);
                row.Add(v.Book);
                row.Add(v.Chapter);
                row.Add(v.Verse);
                row.Add(v.Text);
                rows.Add(row);
            }
            InsertBibleRows(version, rows, res.Verses.Count);
        }

        /// <summary>INSERT por lotes en transacción (100 filas por sentencia).</summary>
        private void InsertBibleRows(string version, List<object> rows, long expectedVerses)
        {
            if (_engine == null) { Status("Sin núcleo no se puede importar."); return; }
            try
            {
                string beginRes = _engine.DbExec(LuminaStorage.RawSqlRequest("BEGIN"));
                if (beginRes == null)
                {
                    Status("No se pudo iniciar la transacción.");
                    return;
                }
                long inserted = 0;
                try
                {
                    const int batchSize = 100;
                    int total = rows.Count;
                    for (int start = 0; start < total; start += batchSize)
                    {
                        int count = Math.Min(batchSize, total - start);
                        StringBuilder sql = new StringBuilder(
                            "INSERT OR IGNORE INTO bible(version,book,chapter,verse,text) VALUES ");
                        List<object> pars = new List<object>(count * 5);
                        for (int i = 0; i < count; i++)
                        {
                            if (i > 0) sql.Append(',');
                            sql.Append("(?,?,?,?,?)");
                            List<object> row = rows[start + i] as List<object>;
                            if (row == null)
                            {
                                pars.Add(version); pars.Add(0); pars.Add(0); pars.Add(0); pars.Add("");
                                continue;
                            }
                            string v = row.Count >= 5 ? Cell(row, 0) : version;
                            int off = row.Count >= 5 ? 1 : 0;
                            long book, chapter, verse;
                            if (!long.TryParse(Cell(row, off), out book) ||
                                !long.TryParse(Cell(row, off + 1), out chapter) ||
                                !long.TryParse(Cell(row, off + 2), out verse))
                            {
                                pars.Add(v); pars.Add(0); pars.Add(0); pars.Add(0); pars.Add("");
                                continue;
                            }
                            pars.Add(v);
                            pars.Add(book);
                            pars.Add(chapter);
                            pars.Add(verse);
                            pars.Add(Cell(row, off + 3));
                            inserted++;
                        }
                        _engine.DbExec(LuminaStorage.BuildExecJson(sql.ToString(), pars.ToArray()));
                    }
                    _engine.DbExec(LuminaStorage.RawSqlRequest("COMMIT"));
                    Status("Biblia «" + version + "» importada (" + inserted + " versículos).");
                    MessageBox.Show(this,
                        "Insertados " + inserted + " versículos" +
                        (expectedVerses > 0 && inserted != expectedVerses
                            ? " (esperados " + expectedVerses + "; los duplicados se ignoran)."
                            : "."),
                        "Importar Biblia", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception)
                {
                    _engine.DbExec(LuminaStorage.RawSqlRequest("ROLLBACK"));
                    throw;
                }
            }
            catch (LuminaException ex)
            {
                MessageBox.Show(this, "Fallo insertando la biblia: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /* ======================================================================
         *  CULTO
         * ==================================================================== */

        internal void RefreshServiceList()
        {
            // v7.1.0 «OPERADOR» (feedback #2): filas con nombre/tipo correctos
            // — etiquetas en ESPAÑOL y detalle útil (antes: "1. [song] Título"
            // con el kind interno en inglés).
            List<ServiceItemVm> vms = new List<ServiceItemVm>(_serviceItems.Count);
            for (int i = 0; i < _serviceItems.Count; i++)
            {
                ScenarioItem it = _serviceItems[i];
                ServiceItemVm vm = new ServiceItemVm();
                vm.Number = (i + 1).ToString(CultureInfo.InvariantCulture);
                vm.KindLabel = ServiceItemVm.KindLabelOf(it);
                vm.Title = it.Title.Length > 0 ? it.Title : "(sin nombre)";
                vm.Detail = ServiceItemVm.DetailOf(it);
                vms.Add(vm);
            }
            pageService.FillItems(vms);
        }

        /// <summary>v7.1.0: selecciona el último ítem agregado (feedback UX).</summary>
        private void SelectLastServiceItem()
        {
            pageService.SelectIndex(_serviceItems.Count - 1);
        }

        internal void MoveItem(int delta)
        {
            int i = pageService.SelectedIndex();
            if (i < 0) return;
            int j = i + delta;
            if (j < 0 || j >= _serviceItems.Count) return;
            ScenarioItem tmp = _serviceItems[i];
            _serviceItems[i] = _serviceItems[j];
            _serviceItems[j] = tmp;
            RefreshServiceList();
            pageService.SelectIndex(j);
        }

        internal void RemoveItem()
        {
            int i = pageService.SelectedIndex();
            if (i < 0) return;
            _serviceItems.RemoveAt(i);
            RefreshServiceList();
            if (i < _serviceItems.Count) pageService.SelectIndex(i);
        }

        internal void ImportSongJson()
        {
            if (!RequireEngine()) return;
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Importar canción JSON";
            dlg.Filter = "Canción JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                string json = MiniJson.Utf8BytesToString(File.ReadAllBytes(dlg.FileName));
                string parsed = LuminaEngine.SongParse(json);
                Dictionary<string, object> res = MiniJson.Parse(parsed);
                if (MiniJson.GetInt(res, "ok", 0) != 1)
                {
                    MessageBox.Show(this, "El núcleo no aceptó la canción.", "Importar",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                Dictionary<string, object> songDict = MiniJson.GetObject(res, "song");
                Song song = ScenarioBuilder.SongFromDict(songDict);
                if (song.Title.Length == 0) song.Title = Path.GetFileNameWithoutExtension(dlg.FileName);
                _serviceItems.Add(ScenarioBuilder.FromSong(song));
                RefreshServiceList();
                Status("Canción «" + song.Title + "» agregada al culto.");
            }
            catch (LuminaException ex)
            {
                MessageBox.Show(this, "La canción no es válida: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "No se pudo leer el archivo: " + ex.Message, "Importar",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        internal void ImportPlanJson()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Importar plan de servicio JSON";
            dlg.Filter = "Plan JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Dictionary<string, object> plan = MiniJson.Parse(
                    File.ReadAllText(dlg.FileName, new UTF8Encoding(false)));
                List<object> items = MiniJson.GetArray(plan, "items");
                if (items.Count == 0)
                {
                    MessageBox.Show(this, "El plan no trae elementos (se espera \"items\": [...]).",
                        "Importar plan", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                int added = 0;
                foreach (object ro in items)
                {
                    Dictionary<string, object> io = ro as Dictionary<string, object>;
                    if (io == null) continue;
                    string type = MiniJson.GetString(io, "type", "blank").ToLowerInvariant();
                    string title = MiniJson.GetString(io, "title", string.Empty);
                    // v6.0.0: notas del director (viajan en el plan por ítem).
                    string notes = MiniJson.GetString(io, "notes", string.Empty);
                    // v6.0.0: resaltado en proyección (término de búsqueda).
                    string highlight = MiniJson.GetString(io, "highlight", string.Empty);
                    ScenarioItem added2;
                    if (type == "song")
                    {
                        Song s = new Song();
                        s.Title = title.Length > 0 ? title : "Canción";
                        s.Artist = MiniJson.GetString(io, "artist", string.Empty);
                        s.Lyrics = MiniJson.GetString(io, "lyrics", string.Empty);
                        List<SongBlock> blocks = ScenarioBuilder.ParseLyricsBlocks(s.Lyrics);
                        if (blocks.Count > 0) s.Blocks = blocks;
                        added2 = ScenarioBuilder.FromSong(s);
                    }
                    else if (type == "scripture")
                    {
                        string refr = MiniJson.GetString(io, "ref", string.Empty);
                        if (refr.Length == 0) refr = title;
                        int per = (int)MiniJson.GetInt(io, "versesPerSlide", 1);
                        added2 = ScenarioBuilder.ScriptureItem(refr,
                            MiniJson.GetString(io, "version", _settings.LastBibleVersion),
                            per < 1 ? 1 : per, MiniJson.GetString(io, "text", string.Empty));
                    }
                    else if (type == "text")
                    {
                        int mx = (int)MiniJson.GetInt(io, "maxLinesPerSlide", 4);
                        added2 = ScenarioBuilder.TextItem(title,
                            MiniJson.GetString(io, "text", title), mx < 1 ? 4 : mx);
                    }
                    else if (type == "pptx")
                    {
                        // v7.1.0 «OPERADOR» (feedback #6): PPTX ORIGINAL — solo la
                        // ruta; PowerPoint lo proyecta al ponerlo en vivo.
                        added2 = ScenarioBuilder.PptxItem(
                            title.Length > 0 ? title : "Presentación",
                            MiniJson.GetString(io, "pptxPath", string.Empty));
                    }
                    else if (type == "composed")
                    {
                        // v7.1.0 «OPERADOR» (feedback #1): slide COMPUESTA del
                        // editor de lienzo libre (elementos posicionables).
                        List<ComposedElement> els = new List<ComposedElement>();
                        foreach (object eo in MiniJson.GetArray(io, "elements"))
                        {
                            ComposedElement el = ComposedElement.FromDict(eo as Dictionary<string, object>);
                            if (el != null) els.Add(el);
                        }
                        added2 = ScenarioBuilder.ComposedItem(
                            title.Length > 0 ? title : "Diapositiva", els);
                    }
                    else if (type == "image")
                    {
                        added2 = ScenarioBuilder.ImageItem(
                            title.Length > 0 ? title : "Imagen",
                            MiniJson.GetString(io, "imagePath", string.Empty),
                            MiniJson.GetString(io, "text", string.Empty));
                    }
                    else if (type == "video")
                    {
                        added2 = ScenarioBuilder.VideoItem(
                            title.Length > 0 ? title : "Video",
                            MiniJson.GetString(io, "videoPath", string.Empty));
                    }
                    else
                    {
                        added2 = ScenarioBuilder.BlankItem(
                            title.Length > 0 ? title : "En blanco");
                    }
                    added2.Notes = notes;
                    added2.Highlight = highlight;
                    _serviceItems.Add(added2);
                    added++;
                }
                string planName = MiniJson.GetString(plan, "name", string.Empty);
                if (planName.Length > 0) pageService.txtServiceName.Text = planName;
                RefreshServiceList();
                Status("Plan importado: " + added + " ítems agregados al culto.");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "El plan no se pudo leer: " + ex.Message,
                    "Importar plan", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /* --------------------------------------------- plan JSON v6.0.0 */
        /// <summary>
        /// Guarda el culto COMO PLAN JSON (mismo formato que «Importar plan»):
        /// ítems completos CON notas del director y resaltado de proyección
        /// (v6.0.0: las notas persisten de verdad en el archivo del plan).
        /// </summary>
        internal void SavePlanJson()
        {
            if (_serviceItems.Count == 0)
            {
                Status("El culto está vacío: no hay nada que guardar.");
                return;
            }
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Title = "Guardar plan de culto JSON";
            dlg.Filter = "Plan JSON (*.json)|*.json|Todos los archivos (*.*)|*.*";
            string name = pageService.txtServiceName.Text.Trim();
            dlg.FileName = (name.Length > 0 ? name : "culto") + ".json";
            if (dlg.ShowDialog(this) != true) return;
            try
            {
                Dictionary<string, object> plan = new Dictionary<string, object>();
                plan["name"] = name.Length > 0 ? name : "Culto";
                List<object> arr = new List<object>(_serviceItems.Count);
                foreach (ScenarioItem it in _serviceItems)
                    arr.Add(ScenarioBuilder.ItemToDict(it));
                plan["items"] = arr;
                File.WriteAllText(dlg.FileName,
                    MiniJson.Serialize(plan) + "\n", new UTF8Encoding(false));
                Status("Plan guardado: " + dlg.FileName + " (" + _serviceItems.Count + " ítems).");
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "El plan no se pudo guardar: " + ex.Message,
                    "Guardar plan", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /* --------------------------------------------------- PPTX v7.1.0 «OPERADOR» */
        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #6): AGREGA la presentación PPTX
        /// ORIGINAL al culto — SIN extracción. El archivo se conserva tal cual
        /// (la ruta viaja en el plan JSON) y al ponerlo en vivo PowerPoint lo
        /// proyecta completo vía COM (PowerPointShow). La importación de
        /// SOLO-TEXTO de v6 sigue disponible como opción secundaria.
        /// </summary>
        internal void ImportPptxToService()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Agregar presentación PowerPoint al culto";
            dlg.Filter = "PowerPoint (*.pptx;*.pptm)|*.pptx;*.pptm|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;

            string path = Path.GetFullPath(dlg.FileName);
            if (!File.Exists(path))
            {
                MessageBox.Show(this, "El archivo no existe: " + path, "Agregar PPTX",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (path.EndsWith(".pptm", StringComparison.OrdinalIgnoreCase) &&
                MessageBox.Show(this,
                    "El archivo .pptm puede contener macros.\n\n" +
                    "Se proyectará con PowerPoint tal como está (las macros NO se\n" +
                    "ejecutan desde esta app; PowerPoint aplica su propia política).\n\n¿Agregarlo igual?",
                    "Agregar PPTX", MessageBoxButton.YesNo, MessageBoxImage.Question)
                    != MessageBoxResult.Yes) return;

            // v7.1.0: el ítem conserva el archivo ORIGINAL (ruta absoluta). El
            // nombre visible es el del archivo sin extensión.
            string title = Path.GetFileNameWithoutExtension(path);
            _serviceItems.Add(ScenarioBuilder.PptxItem(title.Length > 0 ? title : "Presentación", path));
            RefreshServiceList();
            SelectLastServiceItem();

            if (!PowerPointShow.IsInstalled())
            {
                Status("Presentación agregada (PowerPoint NO está instalado: se " +
                       "proyectará el marcador con el nombre del archivo).");
            }
            else
            {
                Status("Presentación agregada al culto (se proyectará con PowerPoint): " +
                       Path.GetFileName(path));
            }
        }

        /// <summary>
        /// v7.1.0: importación de SOLO TEXTO de v6.0.0 conservada como opción
        /// secundaria (cada diapositiva de texto → 1 ítem de texto).
        /// </summary>
        internal void ImportPptxTextToService()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Importar PPTX (solo texto) al culto";
            dlg.Filter = "PowerPoint (*.pptx)|*.pptx|Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;

            PptxImportResult res;
            try { res = PptxImporter.Import(dlg.FileName); }
            catch (Exception ex)
            {
                MessageBox.Show(this, "El PPTX no se pudo leer: " + ex.Message, "Importar PPTX",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (res.Slides.Count == 0)
            {
                MessageBox.Show(this,
                    "No se encontraron diapositivas de texto en el archivo.\n\n" +
                    "La importación lee las CAJAS DE TEXTO de cada diapositiva " +
                    "(imágenes, tablas y gráficos no se convierten).\n\n" +
                    "Para proyectar el PPTX COMPLETO use «Agregar PPTX…» (PowerPoint real).",
                    "Importar PPTX (texto)", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int added = 0, blank = 0;
            for (int i = 0; i < res.Slides.Count; i++)
            {
                PptxSlide sl = res.Slides[i];
                if (sl.Lines.Count == 0)
                {
                    _serviceItems.Add(ScenarioBuilder.BlankItem("Diapositiva " + (i + 1)));
                    blank++;
                    continue;
                }
                // Título sugerido: primera línea no vacía; texto: todas las líneas.
                string title = string.Empty;
                StringBuilder body = new StringBuilder();
                foreach (string l in sl.Lines)
                {
                    string t = l == null ? string.Empty : l.Trim();
                    if (t.Length == 0) continue;
                    if (title.Length == 0) title = t;
                    if (body.Length > 0) body.Append('\n');
                    body.Append(t);
                }
                if (body.Length == 0)
                {
                    _serviceItems.Add(ScenarioBuilder.BlankItem("Diapositiva " + (i + 1)));
                    blank++;
                    continue;
                }
                // maxLinesPerSlide = líneas de la diapositiva → 1:1 (no se reparte).
                _serviceItems.Add(ScenarioBuilder.TextItem(
                    title.Length > 0 ? title : "Diapositiva " + (i + 1),
                    body.ToString(), Math.Max(1, sl.Lines.Count)));
                added++;
            }
            string name = res.Title.Length > 0 ? res.Title : Path.GetFileNameWithoutExtension(dlg.FileName);
            if (name.Length > 0 && pageService.txtServiceName.Text.Trim() == "Culto")
                pageService.txtServiceName.Text = name;
            RefreshServiceList();
            Status("PPTX importado: " + added + " diapositivas de texto + " + blank +
                   " en blanco (" + (added + blank) + " ítems en el culto).");
        }

        /* ------------------------------------------------ editor de culto v5.1.0 */

        internal void AddCurrentSongToService()
        {
            Song s = ScenarioBuilder.SongFromEditor(
                pageSongs.txtTitle.Text, pageSongs.txtArtist.Text, pageSongs.txtLyrics.Text,
                pageSongs.chkHymnMode.IsChecked == true, pageSongs.numTranspose.IntValue);
            if (s.Title.Length == 0) s.Title = "Sin título";
            if (s.Blocks.Count == 0)
            {
                Status("El editor de canciones está vacío: escribe la letra primero.");
                return;
            }
            _serviceItems.Add(ScenarioBuilder.FromSong(s));
            RefreshServiceList();
            Status("Canción «" + s.Title + "» agregada al culto (" + _serviceItems.Count + " ítems).");
        }

        internal void AddCurrentScriptureToService()
        {
            string reference = _txtRef.Text.Trim();
            if (reference.Length == 0)
            {
                Status("Escribe la referencia en la página Biblia (p. ej. Sal 23:1-6).");
                return;
            }
            ScenarioItem it = ScenarioBuilder.ScriptureItem(
                reference, pageBible.txtVersion.Text.Trim(), pageBible.numVerses.IntValue, string.Empty);
            // v6.0.0: si hubo búsqueda previa, la palabra se resalta al proyectar.
            it.Highlight = _lastBibleSearchTerm;
            _serviceItems.Add(it);
            RefreshServiceList();
            Status("Pasaje «" + reference + "» agregado al culto (" + _serviceItems.Count + " ítems)." +
                   (it.Highlight.Length > 0 ? " Resaltará «" + it.Highlight + "» en proyección." : ""));
        }

        internal void AddImageItemToService()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Agregar imagen al culto";
            dlg.Filter = "Imágenes (*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff)|" +
                         "*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tif;*.tiff|" +
                         "Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;
            string caption = PromptDialog("Pie de imagen (opcional)",
                "Texto sobre la imagen (opcional):", string.Empty);
            ScenarioItem it = ScenarioBuilder.ImageItem(
                Path.GetFileName(dlg.FileName), dlg.FileName, caption ?? string.Empty);
            _serviceItems.Add(it);
            RefreshServiceList();
            Status("Imagen agregada al culto: " + it.Title);
        }

        internal void AddTextItemToService()
        {
            string text = PromptMultilineDialog("Texto / aviso para el culto",
                "Líneas a proyectar (una por slide agrupada):",
                "Bienvenidos a la casa del Señor");
            if (text == null || text.Trim().Length == 0)
            {
                Status("El texto quedó vacío: no se agregó nada.");
                return;
            }
            string title = text.Trim();
            int nl = title.IndexOf('\n');
            if (nl > 0) title = title.Substring(0, nl).Trim();
            _serviceItems.Add(ScenarioBuilder.TextItem(title, text, 4));
            RefreshServiceList();
            Status("Texto agregado al culto: " + title);
        }

        internal void AddBlankItemToService()
        {
            _serviceItems.Add(ScenarioBuilder.BlankItem("En blanco"));
            RefreshServiceList();
            Status("Ítem en blanco agregado (" + _serviceItems.Count + " ítems).");
        }

        /* ------------------------------------------------ Diseño (v7.1.0) -- */

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #1): NUEVA diapositiva con el editor de
        /// LIENZO LIBRE estilo PowerPoint (arrastrar/redimensionar/editar in
        /// situ). El resultado es un ítem «composed» (1 ítem = 1 slide con
        /// TODOS sus elementos posicionables — WYSIWYG con la proyección).
        /// </summary>
        internal void NewComposedSlide()
        {
            EditComposedCore(-1, "Diapositiva " + (_serviceItems.Count + 1).ToString(
                CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// v7.1.0: editar el ítem seleccionado del culto — los «Diseño»
        /// (composed) reabren el editor con sus elementos; los pptx muestran
        /// su información; el resto no hace nada (doble clic benigno).
        /// </summary>
        internal void EditSelectedServiceItem()
        {
            int idx = pageService.SelectedIndex();
            if (idx < 0 || idx >= _serviceItems.Count) return;
            ScenarioItem it = _serviceItems[idx];
            if (it == null) return;
            if (it.Kind == "composed")
            {
                EditComposedCore(idx, it.Title.Length > 0 ? it.Title : "Diapositiva");
                return;
            }
            if (it.Kind == "pptx" && it.PptxPath.Length > 0)
            {
                MessageBox.Show(this, "Presentación PowerPoint (archivo ORIGINAL, sin extracción):\n" +
                    it.PptxPath + "\n\nSe proyecta completa con PowerPoint al ponerla en vivo.",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void EditComposedCore(int editIndex, string suggestedTitle)
        {
            List<ComposedElement> existing = (editIndex >= 0 && editIndex < _serviceItems.Count &&
                _serviceItems[editIndex].Composed != null)
                ? _serviceItems[editIndex].Composed
                : null;
            SlideEditorWindow editor = new SlideEditorWindow(this, _theme,
                suggestedTitle, existing);
            if (editor.ShowDialog() != true) return;
            ScenarioItem item = ScenarioBuilder.ComposedItem(
                editor.SlideTitle.Length > 0 ? editor.SlideTitle : "Diapositiva",
                editor.Elements);
            if (editIndex >= 0 && editIndex < _serviceItems.Count)
            {
                _serviceItems[editIndex] = item;
                RefreshServiceList();
                pageService.SelectIndex(editIndex);
                // edición en caliente: si el culto está EN VIVO, recargar
                // CONSERVANDO la slide actual (no se reinicia el culto)
                if (_serviceIsLive && _slides.Count > 0)
                {
                    int keep = _currentIndex;
                    SendServiceToStage();
                    if (keep >= 0 && keep < _slides.Count) NavigateToIndex(keep);
                }
                Status("Diapositiva «" + item.Title + "» editada (" +
                       item.Composed.Count + " elementos).");
            }
            else
            {
                _serviceItems.Add(item);
                RefreshServiceList();
                SelectLastServiceItem();
                Status("Diapositiva «" + item.Title + "» agregada al culto (" +
                       _serviceItems.Count + " ítems).");
            }
        }

        internal void AddVideoItemToService()
        {
            Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
            dlg.Title = "Agregar video al culto";
            dlg.Filter = "Video (*.mp4;*.wmv;*.avi;*.m4v)|*.mp4;*.wmv;*.avi;*.m4v|" +
                         "Todos los archivos (*.*)|*.*";
            if (dlg.ShowDialog(this) != true) return;

            // Opciones de reproducción (bucle/volumen).
            bool loop = false;
            double volume = 100;
            Window opt = new Window();
            opt.Title = "Opciones del video";
            opt.Owner = this;
            opt.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            opt.ResizeMode = ResizeMode.NoResize;
            opt.SizeToContent = SizeToContent.WidthAndHeight;
            opt.Background = FindResource("PageBrush") as Brush;
            StackPanel sp = new StackPanel { Margin = new Thickness(18), Width = 320 };
            System.Windows.Controls.CheckBox chkLoop = new System.Windows.Controls.CheckBox
            { Content = "Repetir en bucle", IsChecked = false, Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(chkLoop);
            sp.Children.Add(new TextBlock
            {
                Text = "Volumen (0-100):",
                Style = (Style)FindResource("TxtLabel"),
                Margin = new Thickness(0, 0, 0, 4)
            });
            LumNumeric numVol = new LumNumeric { Min = 0, Max = 100, Step = 5, Value = 100, Width = 90 };
            sp.Children.Add(numVol);
            LumButton ok = new LumButton
            { Text = "Agregar", Variant = "Primary", MinWidth = 110, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(ok);
            opt.Content = sp;
            ok.Click += delegate { opt.DialogResult = true; };
            if (opt.ShowDialog() == true)
            {
                loop = chkLoop.IsChecked == true;
                volume = numVol.Value;
            }

            ScenarioItem it = ScenarioBuilder.VideoItem(
                "Video: " + Path.GetFileName(dlg.FileName), dlg.FileName, loop, (int)volume);
            _serviceItems.Add(it);
            RefreshServiceList();
            Status("Video agregado al culto: " + it.Title +
                   (loop ? " (bucle, vol " + (int)volume + ")" : " (vol " + (int)volume + ")"));
        }

        internal void SendServiceToStage()
        {
            if (!RequireEngine()) return;
            if (_serviceItems.Count == 0)
            {
                Status("El culto está vacío: importa canciones o agrega ítems.");
                return;
            }
            LoadScenarioFromItems(_serviceItems, pageService.txtServiceName.Text.Trim());
        }

        /* ======================================================================
         *  EXPORTAR
         * ==================================================================== */

        internal void ExportScenarioPptx()
        {
            if (_slides.Count == 0)
            {
                Status("No hay escenario cargado que exportar.");
                MessageBox.Show(this, "Carga primero un escenario (canción, pasaje o culto) en «En Vivo».",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Title = "Exportar escenario a PPTX";
            dlg.Filter = "Presentación PowerPoint (*.pptx)|*.pptx";
            dlg.FileName = SafeFileName(_lastScenarioName.Length > 0 ? _lastScenarioName : "Escenario") + ".pptx";
            if (dlg.ShowDialog(this) != true) return;
            string err = PptxExporter.ExportToFile(dlg.FileName, _lastScenarioName,
                BuildExportSlides(), _theme);
            if (err == null)
            {
                Status("PPTX exportado: " + dlg.FileName);
                MessageBox.Show(this, "Exportado correctamente:\n" + dlg.FileName,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Status("Fallo al exportar PPTX.");
                MessageBox.Show(this, err, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        internal void ExportScenarioPdf()
        {
            if (_slides.Count == 0)
            {
                Status("No hay escenario cargado que exportar.");
                MessageBox.Show(this, "Carga primero un escenario (canción, pasaje o culto) en «En Vivo».",
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            Microsoft.Win32.SaveFileDialog dlg = new Microsoft.Win32.SaveFileDialog();
            dlg.Title = "Exportar escenario a PDF";
            dlg.Filter = "Documento PDF (*.pdf)|*.pdf";
            dlg.FileName = SafeFileName(_lastScenarioName.Length > 0 ? _lastScenarioName : "Escenario") + ".pdf";
            if (dlg.ShowDialog(this) != true) return;
            lumina.ui.PdfImageDecoder.Install();
            string err = PdfExporter.ExportToFile(dlg.FileName, _lastScenarioName,
                BuildExportSlides(), _theme);
            if (err == null)
            {
                Status("PDF exportado: " + dlg.FileName);
                MessageBox.Show(this, "Exportado correctamente:\n" + dlg.FileName,
                    AppName, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                Status("Fallo al exportar PDF.");
                MessageBox.Show(this, err, AppName, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        internal List<ExportSlide> BuildExportSlides()
        {
            List<ExportSlide> result = new List<ExportSlide>();
            foreach (SlideView v in _slides)
            {
                ExportSlide es = new ExportSlide(v);
                if (v.RefLabel == "video" || v.RefLabel == "en blanco")
                {
                    es.Kind = SlideKind.Title;
                    es.Subtitle = v.RefLabel == "video" ? "Video (reproducir en vivo)" : string.Empty;
                }
                result.Add(es);
            }
            return result;
        }

        private static string SafeFileName(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Escenario";
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name.Length > 60 ? name.Substring(0, 60) : name;
        }

        /* ------------------------------------------------------------ prompts */

        /// <summary>Diálogo de texto de una línea (null = cancelado).</summary>
        private string PromptDialog(string title, string label, string initial)
        {
            Window dlg = new Window();
            dlg.Title = title;
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.SizeToContent = SizeToContent.WidthAndHeight;
            dlg.Background = FindResource("PageBrush") as Brush;
            dlg.MinWidth = 380;
            StackPanel sp = new StackPanel { Margin = new Thickness(18) };
            sp.Children.Add(new TextBlock { Text = label, Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 0, 0, 4) });
            System.Windows.Controls.TextBox t = new System.Windows.Controls.TextBox { Text = initial ?? string.Empty };
            sp.Children.Add(t);
            LumButton ok = new LumButton { Text = "Aceptar", Variant = "Primary", MinWidth = 110, Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(ok);
            dlg.Content = sp;
            ok.Click += delegate { dlg.DialogResult = true; };
            return dlg.ShowDialog() == true ? t.Text : null;
        }

        /// <summary>Diálogo de texto multilínea (null = cancelado).</summary>
        private string PromptMultilineDialog(string title, string label, string initial)
        {
            Window dlg = new Window();
            dlg.Title = title;
            dlg.Owner = this;
            dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            dlg.ResizeMode = ResizeMode.NoResize;
            dlg.Width = 440; dlg.Height = 260;
            dlg.Background = FindResource("PageBrush") as Brush;
            DockPanel dp = new DockPanel { Margin = new Thickness(18) };
            TextBlock lbl = new TextBlock { Text = label, Style = (Style)FindResource("TxtLabel"), Margin = new Thickness(0, 0, 0, 4) };
            DockPanel.SetDock(lbl, Dock.Top);
            dp.Children.Add(lbl);
            LumButton ok = new LumButton { Text = "Agregar", Variant = "Primary", MinWidth = 110, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            DockPanel.SetDock(ok, Dock.Bottom);
            dp.Children.Add(ok);
            System.Windows.Controls.TextBox t = new System.Windows.Controls.TextBox
            {
                Text = initial ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = System.Windows.TextWrapping.Wrap,
                VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                VerticalContentAlignment = VerticalAlignment.Top
            };
            dp.Children.Add(t);
            dlg.Content = dp;
            ok.Click += delegate { dlg.DialogResult = true; };
            return dlg.ShowDialog() == true ? t.Text : null;
        }
    }
}
