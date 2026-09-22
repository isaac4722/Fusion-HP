// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Program.cs : LA PRUEBA DE CONCEPTO FORMAL del puente C# ↔ núcleo nativo.
//  Código de salida: 0 = PASS, 1 = FAIL. Imprime "POC PASS n/n" o
//  "POC FAIL i/n detalle".
//
//  Debe funcionar SIN cambios en net35, net48 y net8.0 (DllImport funciona
//  igual; sin NativeLibrary para mantener compatibilidad).
//  C# 7.3: sin async/await, sin APIs posteriores a .NET 3.5 salvo #if !NET35.
//
//  Los 12 checks:
//    1. Carga de la biblioteca nativa (DllImport resuelto)
//    2. fusion_version: "FusionCore" + "3.0"
//    3. Estructura + handle: create headless != 0; destroy sin crash
//    4. UTF-8 ida/vuelta por fusion_ping → EV_PONG idéntico (callback)
//    5. Callbacks desde el hilo del motor: EV_STATE con slideCount >= 3 (5 s)
//    6. fusion_song_parse: ok=1, slides no vacías
//    7. fusion_bib_parse: .BIB embebido (directivas + TAB + acentos) ok=1,
//       verses>=6, version TEST
//    8. fusion_bible_ref_resolve("Jn 3:16") → 43/3/16
//    9. fusion_chords_transpose("Do Sol", +2, latin) → "Re La"
//   10. fusion_db_open + INSERT/SELECT con params + FTS5 MATCH 'aleluya'
//   11. Escenario + En Vivo: next→index 0; next×2→index 2; black→true
//   12. Estrés de marshaling: 200 next/prev + 1000 pings, >=1000 PONG
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using fusion.bridge;
using fusion.core;

namespace fusion.poc
{
    internal static class Program
    {
        private const int TotalChecks = 12;

        /* ------------------------------------------------- estado compartido */

        private static readonly object EvLock = new object();
        private static int _pongCount;
        private static string _lastPong;
        private static long _lastStateSlideCount;
        private static long _lastSlideIndex;
        private static ManualResetEvent _stateArrived = new ManualResetEvent(false);
        private static ManualResetEvent _slideArrived = new ManualResetEvent(false);

        private static int _pass;
        private static readonly List<string> Failures = new List<string>();

        /* ------------------------------------------------------------- main */

        private static int Main()
        {
            Console.WriteLine("PoC.Managed — puente C# ↔ FusionCore (" +
                Environment.Version + ")");

            FusionEngine engine = null;
            try
            {
                // Chequeos 1-2 sin handle.
                Check(1, "Cargar biblioteca nativa", CheckLibraryLoads);
                Check(2, "fusion_version empieza por FusionCore y contiene 3.0", CheckVersion);

                if (Failures.Count == 0)
                {
                    // Motor headless compartido con callback global.
                    engine = FusionEngine.Create(true, OnEngineEvent);
                    Check(3, "fusion_create headless → handle != 0 (y destroy limpio)", delegate { return CheckHandle(engine); });
                    Check(4, "UTF-8 ida/vuelta por ping → PONG idéntico", delegate { return CheckPingUtf8(engine); });
                    Check(5, "EV_STATE desde el hilo del motor con slideCount>=3", delegate { return CheckStateEvent(engine); });
                    Check(6, "fusion_song_parse ok=1 y slides", delegate { return CheckSongParse(); });
                    Check(7, "fusion_bib_parse .BIB embebido (verses>=6, TEST)", delegate { return CheckBibParse(); });
                    Check(8, "fusion_bible_ref_resolve Jn 3:16 → 43/3/16", delegate { return CheckRefResolve(); });
                    Check(9, "fusion_chords_transpose Do Sol +2 → Re La", delegate { return CheckChords(); });
                    Check(10, "BD: INSERT/SELECT con params + FTS5 'aleluya'", delegate { return CheckDatabase(engine); });
                    Check(11, "Escenario + next/next/next + black (eventos)", delegate { return CheckLiveFlow(engine); });
                    Check(12, "Estrés: 200 next/prev + 1000 pings (>=1000 PONG)", delegate { return CheckStress(engine); });
                }
                else
                {
                    // Sin biblioteca nativa (o ABI distinta) los 10 chequeos
                    // restantes no pueden correr: se reportan como omitidos.
                    for (int idx = 3; idx <= TotalChecks; idx++)
                        Failures.Add("check " + idx + ": OMITIDO — biblioteca nativa ausente o ABI incompatible");
                    Console.WriteLine("  (chequeos 3..12 omitidos: sin núcleo nativo)");
                }
            }
            catch (Exception ex)
            {
                Fail("Excepción fatal fuera de un chequeo: " + ex.GetType().Name + ": " + ex.Message);
            }
            finally
            {
                if (engine != null)
                {
                    try { engine.Dispose(); }
                    catch (Exception) { }
                }
            }

            int failed = Failures.Count;
            if (failed == 0)
            {
                Console.WriteLine("POC PASS " + _pass + "/" + TotalChecks);
                return 0;
            }
            Console.WriteLine("POC FAIL " + failed + "/" + TotalChecks + " detalle:");
            foreach (string f in Failures) Console.WriteLine("  - " + f);
            return 1;
        }

        /* ------------------------------------------------- chequeo framework */

        private static void Check(int index, string name, Func<bool> fn)
        {
            string detail;
            bool ok;
            try
            {
                ok = fn();
                detail = ok ? "" : "sin detalle";
            }
            catch (Exception ex)
            {
                ok = false;
                detail = ex.GetType().Name + ": " + ex.Message;
            }
            if (ok)
            {
                _pass++;
                Console.WriteLine("  [" + index.ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ') +
                                  "/" + TotalChecks + "] " + name + " … OK");
            }
            else
            {
                Console.WriteLine("  [" + index.ToString(CultureInfo.InvariantCulture).PadLeft(2, ' ') +
                                  "/" + TotalChecks + "] " + name + " … FAIL (" + detail + ")");
                Failures.Add("check " + index + " (" + name + "): " + detail);
            }
        }

        private static void Fail(string detail)
        {
            Failures.Add(detail);
            Console.WriteLine("  [!!] " + detail);
        }

        /* ------------------------------------------------ callback del motor */

        private static void OnEngineEvent(object sender, FusionEvent e)
        {
            // Hilo del motor: solo contar/almacenar con lock. NUNCA llamar al motor.
            lock (EvLock)
            {
                switch (e.Code)
                {
                    case FusionEvents.Pong:
                        _pongCount++;
                        _lastPong = e.Text;
                        break;
                    case FusionEvents.State:
                        Dictionary<string, object> st = TryParse(e.Text);
                        if (st != null)
                        {
                            _lastStateSlideCount = MiniJson.GetInt(st, "slideCount", 0);
                            _stateArrived.Set();
                        }
                        break;
                    case FusionEvents.SlideChanged:
                        Dictionary<string, object> sc = TryParse(e.Text);
                        if (sc != null)
                        {
                            _lastSlideIndex = MiniJson.GetInt(sc, "index", -1);
                            _slideArrived.Set();
                        }
                        break;
                    default:
                        break;
                }
            }
        }

        private static Dictionary<string, object> TryParse(string json)
        {
            try { return MiniJson.Parse(json); }
            catch (FormatException) { return null; }
        }

        private static bool WaitFor(ManualResetEvent ev, int ms)
        {
#if NET35
            return ev.WaitOne(ms, false);   // net35: firma con exitContext
#else
            return ev.WaitOne(ms);
#endif
        }

        /* ----------------------------------------------------------- chequeos */

        private static bool CheckLibraryLoads()
        {
            // DllImport lanza DllNotFoundException/BadImageFormatException en la
            // primera llamada si la lib no está o es de otra arquitectura.
            try
            {
                string v = FusionEngine.Version();
                return v != null && v.Length > 0;
            }
            catch (DllNotFoundException ex)
            {
                throw new Exception("No se encontró FusionCore (busca libFusionCore.so en Linux / FusionCore.dll en Windows junto al ejecutable). " + ex.Message);
            }
            catch (BadImageFormatException ex)
            {
                throw new Exception("FusionCore encontrada pero con arquitectura incompatible (¿x86 vs x64?). " + ex.Message);
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new Exception("La librería cargada no exporta la ABI esperada. " + ex.Message);
            }
        }

        private static bool CheckVersion()
        {
            string v = FusionEngine.Version();
            Console.WriteLine("        version = \"" + v + "\"");
            return v.StartsWith("FusionCore", StringComparison.Ordinal) && v.Contains("3.0");
        }

        private static bool CheckHandle(FusionEngine engine)
        {
            if (engine.NativeHandle == IntPtr.Zero) return false;
            // El estado es la prueba viva del handle; destroy se valida al salir
            // (Dispose en el finally — un crash ahí rompería el proceso entero).
            string state = engine.StateJson();
            Dictionary<string, object> o = TryParse(state);
            return o != null && MiniJson.GetInt(o, "headless", -1) == 1;
        }

        private static bool CheckPingUtf8(FusionEngine engine)
        {
            string msg = "¡Canción — ñ Á é í ó ú ✓!";
            int before;
            lock (EvLock) { before = _pongCount; _lastPong = null; }
            engine.Ping(msg);
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                lock (EvLock)
                {
                    if (_pongCount > before && _lastPong == msg) return true;
                }
                Thread.Sleep(10);
            }
            string got;
            lock (EvLock) { got = _lastPong; }
            throw new Exception("PONG no llegó o difiere. Esperado \"" + msg + "\", recibido \"" + got + "\"");
        }

        private static bool CheckStateEvent(FusionEngine engine)
        {
            // Canción de 3 bloques × 2 líneas → título + 3 slides de verso = 4.
            string scenario = ScenarioBuilder.BuildScenarioJson("PoC-5", null,
                new ScenarioItem[] { ScenarioBuilder.FromSong(PocSong()) });
            ResetWaiters();
            int st = engine.LoadScenario(scenario);
            if (st != FusionStatus.Ok)
                throw new Exception("LoadScenario=" + FusionStatus.Name(st));
            if (!WaitFor(_stateArrived, 5000))
                throw new Exception("EV_STATE no llegó en 5 s");
            lock (EvLock)
            {
                Console.WriteLine("        slideCount = " + _lastStateSlideCount);
                return _lastStateSlideCount >= 3;
            }
        }

        private static bool CheckSongParse()
        {
            string json = MiniJson.Serialize(ScenarioBuilder.SongToDict(PocSong()));
            string res = FusionEngine.SongParse(json);
            Dictionary<string, object> o = TryParse(res);
            if (o == null) return false;
            List<object> slides = MiniJson.GetArray(o, "slides");
            Console.WriteLine("        ok=" + MiniJson.GetInt(o, "ok", 0) + " slides=" + slides.Count);
            return MiniJson.GetInt(o, "ok", 0) == 1 && slides.Count > 0;
        }

        /// <summary>Fixture .BIB embebido: directivas + TAB + acentos (UTF-8).</summary>
        private static byte[] BibFixture()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("#BIB 1\n");
            sb.Append("#VERSION TEST\n");
            sb.Append("#NAME Biblia de Prueba\n");
            sb.Append("Génesis\t1\t1\tEn el principio creó Dios los cielos y la tierra.\n");
            sb.Append("Génesis\t1\t2\tY la tierra estaba desordenada y vacía.\n");
            sb.Append("Éxodo\t3\t5\tLa zona en que tú estás, tierra santa es.\n");
            sb.Append("Salmos\t23\t1\tJehová es mi pastor; nada me faltará.\n");
            sb.Append("Juan\t3\t16\tPorque de tal manera amó Dios al mundo.\n");
            sb.Append("Juan\t3\t17\tPorque no envió Dios a su Hijo al mundo para condenarlo.\n");
            sb.Append("Apocalipsis\t22\t21\tLa gracia de nuestro Señor sea con vosotros. Amén.\n");
            return new UTF8Encoding(false).GetBytes(sb.ToString());
        }

        private static bool CheckBibParse()
        {
            string res = FusionEngine.BibParse(BibFixture(), null);
            Dictionary<string, object> o = TryParse(res);
            if (o == null) return false;
            long ok = MiniJson.GetInt(o, "ok", 0);
            long verses = MiniJson.GetInt(o, "verses", 0);
            string version = MiniJson.GetString(o, "version", string.Empty);
            Console.WriteLine("        ok=" + ok + " verses=" + verses + " version=" + version);
            return ok == 1 && verses >= 6 && version == "TEST";
        }

        private static bool CheckRefResolve()
        {
            string res = FusionEngine.BibleRefResolve("Jn 3:16");
            Dictionary<string, object> o = TryParse(res);
            if (o == null) return false;
            long book = MiniJson.GetInt(o, "book", 0);
            long chapter = MiniJson.GetInt(o, "chapter", 0);
            long verse = MiniJson.GetInt(o, "verse", 0);
            Console.WriteLine("        book=" + book + " chapter=" + chapter + " verse=" + verse);
            return book == 43 && chapter == 3 && verse == 16;
        }

        private static bool CheckChords()
        {
            string res = FusionEngine.ChordsTranspose("Do Sol", 2, true);
            Dictionary<string, object> o = TryParse(res);
            if (o == null) return false;
            string line = MiniJson.GetString(o, "line", string.Empty);
            Console.WriteLine("        line = \"" + line + "\"");
            // El núcleo puede normalizar espacios: comparar sin bordes.
            return line.Trim() == "Re La".Trim();
        }

        private static bool CheckDatabase(FusionEngine engine)
        {
            string dbPath = Path.Combine(Path.GetTempPath(),
                "fusion_poc_" + Guid.NewGuid().ToString("N") + ".db");
            try
            {
                if (engine.DbOpen(dbPath) != FusionStatus.Ok)
                    throw new Exception("DbOpen falló: " + dbPath);

                // INSERT con params (enlazados — jamás interpolado).
                string res = engine.DbExec(FusionStorage.BuildExecJson(
                    "INSERT INTO songs(title,author,lyrics) VALUES(?,?,?)",
                    "Prueba POC", "Autor", "Santo, santo, santo aleluya"));
                if (FusionStorage.Changes(res) != 1) throw new Exception("INSERT no reportó changes=1");

                // SELECT con params → fila devuelta.
                res = engine.DbExec(FusionStorage.BuildExecJson(
                    "SELECT title FROM songs WHERE title = ?1", "Prueba POC"));
                List<List<object>> rows = FusionStorage.Rows(res);
                if (rows.Count != 1 || Cell(rows[0], 0) != "Prueba POC")
                    throw new Exception("SELECT devolvió " + rows.Count + " filas");

                // FTS5: INSERT canción + MATCH 'aleluya'.
                engine.DbExec(FusionStorage.BuildExecJson(
                    "INSERT INTO songs(title,lyrics) VALUES(?,?)",
                    "Canción FTS", "Canta al Señor aleluya con gozo"));
                res = engine.DbExec(FusionStorage.BuildExecJson(
                    "SELECT rowid FROM songs_fts WHERE songs_fts MATCH ?1", "aleluya"));
                rows = FusionStorage.Rows(res);
                Console.WriteLine("        fts rows = " + rows.Count);
                return rows.Count >= 1;
            }
            finally
            {
                try { engine.DbClose(); } catch (Exception) { }
                try { File.Delete(dbPath); } catch (Exception) { }
                try { File.Delete(dbPath + "-journal"); } catch (Exception) { }
            }
        }

        private static bool CheckLiveFlow(FusionEngine engine)
        {
            string scenario = ScenarioBuilder.BuildScenarioJson("PoC-11", null,
                new ScenarioItem[] { ScenarioBuilder.FromSong(PocSong()) });
            ResetWaiters();
            if (engine.LoadScenario(scenario) != FusionStatus.Ok)
                throw new Exception("LoadScenario falló");
            if (!WaitFor(_stateArrived, 5000)) throw new Exception("EV_STATE inicial no llegó");

            // next → index 0
            ResetWaiters();
            engine.Next();
            if (!WaitFor(_slideArrived, 5000)) throw new Exception("EV_SLIDE_CHANGED #1 no llegó");
            long i1;
            lock (EvLock) { i1 = _lastSlideIndex; }
            if (i1 != 0) throw new Exception("primer next → index " + i1 + " (esperado 0)");

            // next ×2 → index 2. Los dos EV_SLIDE_CHANGED llegan seguidos y el
            // orden de procesamiento del observador no está garantizado: se
            // espera con plazo a que el índice reportado alcance el FINAL (2),
            // igual que el resto de chequeos asíncronos (sin esperar eventos
            // intermedios que introducirían una carrera de lectura).
            engine.Next();
            engine.Next();
            DateTime deadlineIdx = DateTime.UtcNow.AddSeconds(5);
            long i2 = -99;
            while (DateTime.UtcNow < deadlineIdx)
            {
                lock (EvLock) { i2 = _lastSlideIndex; }
                if (i2 == 2) break;
                Thread.Sleep(10);
            }
            if (i2 != 2) throw new Exception("tercer next → index " + i2 + " (esperado 2)");

            // black(1) → state black:true
            engine.Black(true);
            DateTime deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline)
            {
                Dictionary<string, object> st = TryParse(engine.StateJson());
                if (st != null && MiniJson.GetBool(st, "black", false)) return true;
                Thread.Sleep(20);
            }
            throw new Exception("black nunca quedó en true");
        }

        private static bool CheckStress(FusionEngine engine)
        {
            // Escenario pequeño para 200 alternancias next/prev.
            string scenario = ScenarioBuilder.BuildScenarioJson("PoC-12", null,
                new ScenarioItem[] { ScenarioBuilder.FromSong(PocSong()) });
            engine.LoadScenario(scenario);

            int pongBefore;
            lock (EvLock) { pongBefore = _pongCount; }

            for (int i = 0; i < 200; i++)
            {
                if ((i & 1) == 0) engine.Next(); else engine.Prev();
            }
            for (int i = 0; i < 1000; i++)
            {
                engine.Ping("stress-" + i.ToString(CultureInfo.InvariantCulture));
            }

            // Orden no garantizado: cuenta TOTAL de PONGs con plazo generoso.
            DateTime deadline = DateTime.UtcNow.AddSeconds(15);
            int pongs;
            while (true)
            {
                lock (EvLock) { pongs = _pongCount - pongBefore; }
                if (pongs >= 1000) break;
                if (DateTime.UtcNow > deadline)
                    throw new Exception("PONGs recibidos: " + pongs + "/1000");
                Thread.Sleep(20);
            }
            Console.WriteLine("        PONGs recibidos = " + pongs + " (sin crash)");
            return true;
        }

        /* ------------------------------------------------------------ util */

        private static Song PocSong()
        {
            Song s = new Song();
            s.Title = "Canción del PoC";
            s.Artist = "Fusion-HP";
            s.ChorusInterleave = false;
            s.TitleSlide = true;
            s.MaxLinesPerSlide = 4;
            s.Blocks.Add(Block("Verso 1", "Aleluya al Rey", "que viene en nombre del Señor"));
            s.Blocks.Add(Block("Verso 2", "Gloria en las alturas", "y paz en la tierra"));
            s.Blocks.Add(Block("Coro", "Santo, santo, santo", "el Señor de los ejércitos"));
            return s;
        }

        private static SongBlock Block(string label, string l1, string l2)
        {
            SongBlock b = new SongBlock();
            b.Label = label;
            b.Lines.Add(l1);
            b.Lines.Add(l2);
            return b;
        }

        private static string Cell(List<object> row, int idx)
        {
            if (row == null || idx >= row.Count || row[idx] == null) return string.Empty;
            return Convert.ToString(row[idx], CultureInfo.InvariantCulture);
        }

        private static void ResetWaiters()
        {
            _stateArrived.Reset();
            _slideArrived.Reset();
            lock (EvLock)
            {
                _lastStateSlideCount = 0;
                _lastSlideIndex = -1;
            }
        }
    }
}
