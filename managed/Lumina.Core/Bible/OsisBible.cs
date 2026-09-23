// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  OsisBible.cs (v6.0.0 «HORIZONTE») : importador de Biblias en formato
//  OSIS XML (roadmap: «segundo formato académico junto a ZEFania») — el
//  estándar de la Bible Technologies Group usado por biblias académicas
//  (KJV, LXX, Biblias hebreas, etc.).
//
//  Estructura soportada (tolerante — ver docs/api/OSIS import en el repo):
//    <osis>
//      <osisText osisRefWork="Bible" osisIDWork="KJV">
//        <header><work osisWork="KJV"><title>King James…</title></work></header>
//        <div type="book" osisID="Gen">
//          <chapter osisID="Gen.1">           (o <chapter n="1">)
//            <verse osisID="Gen.1.1">En el principio <w>creó</w> …
//              <note>editorial</note>          (se descarta)
//            </verse>                          (o <verse n="1">)
//          </chapter>
//        </div>
//      </osisText>
//    </osis>
//
//  * STREAMING con XmlReader (pull): memoria constante con 31.000+ versículos
//    (mismo objetivo de diseño que ZefaniaBible — nunca se carga el DOM).
//  * Libros por CÓDIGO OSIS (Gen, Exod, …, Rev) con tabla completa 1..66 y
//    variantes frecuentes (Gn, Ex, Lv…); osisID compuesto «Gen.1.1» también
//    resuelve libro/capítulo/versículo aunque falten los contenedores.
//  * Tolerante: <note>/<title>/<reference>/<catchWord> se descartan; <lb/> y
//    <br/> → '\n'; <w> es texto real ( OSIS «word elements» ); versículos con
//    rango «Gen.1.1-Gen.1.2» o sufijo «!a» se toman por el prefijo numérico.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace lumina.core
{
    /// <summary>Versículo OSIS parseado (fila lista para la tabla bible del núcleo).</summary>
    public sealed class OsisVerse
    {
        public int Book;       // 1..66
        public int Chapter;
        public int Verse;
        public string Text = string.Empty;
    }

    /// <summary>Resultado del parseo (espejo de ZefaniaResult: mismo consumidor).</summary>
    public sealed class OsisResult
    {
        public string VersionName = string.Empty;   // osisIDWork / work.title / nombre de archivo
        public List<OsisVerse> Verses = new List<OsisVerse>();
        public int SkippedRows;                      // filas descartadas por rango inválido
        public List<string> BooksSeen = new List<string>(); // códigos en orden de aparición

        public int BookCount
        {
            get
            {
                HashSet<int> seen = new HashSet<int>();
                foreach (OsisVerse v in Verses) seen.Add(v.Book);
                return seen.Count;
            }
        }
    }

    public static class OsisBible
    {
        /// <summary>
        /// Parsea el archivo OSIS XML. progress: callback (versesLeídos, 0)
        /// invocado cada 1024 versículos — no lanza excepciones.
        /// </summary>
        public static OsisResult Parse(string path, Action<int, int> progress)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Ruta vacía.");
            if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo OSIS.", path);
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Parse(fs, progress, TryVersionName(path));
            }
        }

        public static OsisResult Parse(Stream stream, Action<int, int> progress, string fallbackName)
        {
            OsisResult res = new OsisResult();
            if (!string.IsNullOrEmpty(fallbackName)) res.VersionName = fallbackName;

            XmlReaderSettings st = new XmlReaderSettings();
            st.IgnoreComments = true;
            st.IgnoreProcessingInstructions = true;
            st.IgnoreWhitespace = true;
#if LUMINA_NET35
            // net35 no tiene DtdProcessing (llegó en 4.0): ProhibitDtd=false
            // + XmlResolver null → DTD interno tolerado sin resolución externa.
            st.ProhibitDtd = false;
            st.XmlResolver = null;
#else
            st.DtdProcessing = DtdProcessing.Ignore;   // jamás resoluciones de red externas
#endif
            st.CloseInput = false;

            using (XmlReader r = XmlReader.Create(stream, st))
            {
                int curBook = 0, curChapter = 0;
                HashSet<string> booksSeen = new HashSet<string>();
                StringBuilder text = new StringBuilder();
                string workTitle = string.Empty;

                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    string name = r.LocalName;

                    if (string.Equals(name, "osisText", StringComparison.OrdinalIgnoreCase))
                    {
                        string w = r.GetAttribute("osisIDWork");
                        if (string.IsNullOrEmpty(w)) w = r.GetAttribute("osisRefWork");
                        if (!string.IsNullOrEmpty(w)) res.VersionName = w.Trim();
                    }
                    else if (string.Equals(name, "work", StringComparison.OrdinalIgnoreCase))
                    {
                        // <work osisWork="X"><title>…</title></work>: título legible.
                        if (r.IsEmptyElement) continue;
                        int depth = r.Depth;
                        for (bool more = r.Read(); more; )
                        {
                            if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
                            if (r.NodeType == XmlNodeType.Element &&
                                !r.IsEmptyElement &&
                                string.Equals(r.LocalName, "title", StringComparison.OrdinalIgnoreCase))
                            {
                                string t = r.ReadElementContentAsString();
                                if (!string.IsNullOrEmpty(t) && workTitle.Length == 0)
                                    workTitle = t.Trim();
                                // Reader ya posicionado en el nodo siguiente: procesar sin Read.
                                if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth) break;
                            }
                            more = r.Read();
                        }
                    }
                    else if (string.Equals(name, "div", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(name, "book", StringComparison.OrdinalIgnoreCase))
                    {
                        // <div type="book" osisID="Gen">
                        string type = r.GetAttribute("type");
                        bool isBook = string.Equals(name, "book", StringComparison.OrdinalIgnoreCase);
                        if (!isBook && !string.IsNullOrEmpty(type) &&
                            !string.Equals(type, "book", StringComparison.OrdinalIgnoreCase))
                            continue;   // div de otra naturaleza (sección, comentario…)
                        string osisId = r.GetAttribute("osisID");
                        curBook = osisId != null ? BookFromCode(FirstToken(osisId)) : 0;
                        if (curBook >= 1 && curBook <= 66 && osisId != null)
                        {
                            string code = FirstToken(osisId);
                            if (booksSeen.Add(code)) res.BooksSeen.Add(code);
                        }
                        curChapter = 0;
                    }
                    else if (string.Equals(name, "chapter", StringComparison.OrdinalIgnoreCase))
                    {
                        string osisId = r.GetAttribute("osisID");
                        curChapter = ParseInt(osisId != null ? TokenAfterFirst(osisId) : null);
                        if (curChapter < 1)
                        {
                            curChapter = ParseInt(r.GetAttribute("n"));
                            // osisID compuesto también puede fijar el libro.
                            if (curChapter >= 1 && osisId != null)
                            {
                                int b = BookFromCode(FirstToken(osisId));
                                if (b >= 1 && b <= 66) curBook = b;
                            }
                        }
                    }
                    else if (string.Equals(name, "verse", StringComparison.OrdinalIgnoreCase))
                    {
                        string osisId = r.GetAttribute("osisID");
                        int vnum = ParseInt(osisId != null ? LastToken(osisId) : null);
                        if (vnum < 1) vnum = ParseInt(r.GetAttribute("n"));
                        // El osisID compuesto «Libro.Cap.Vers» resuelve lo que falte.
                        // Si el CÓDIGO del libro existe pero es DESCONOCIDO (p. ej. un
                        // div de sección que no es libro), la fila es inválida: se
                        // descarta (jamás hereda el libro anterior en silencio).
                        bool unknownBook = false;
                        if (osisId != null)
                        {
                            int b = BookFromCode(FirstToken(osisId));
                            if (b >= 1 && b <= 66) curBook = b;
                            else if (FirstToken(osisId).Length > 0) unknownBook = true;
                            int c = ParseInt(TokenAfterFirst(osisId));
                            if (c >= 1) curChapter = c;
                        }

                        text.Length = 0;
                        if (!r.IsEmptyElement)
                        {
                            // Contenido mixto: texto + <w> + <lb/>; <note>/<title> se descartan.
                            // SEMÁNTICA de XmlReader (verificada por prueba empírica): tras
                            // ReadElementContentAsString/Skip el reader queda POSICIONADO en
                            // el nodo siguiente — el bucle reprocesa el nodo ACTUAL sin
                            // llamar Read() (un Read() incondicional perdería ese nodo).
                            int depth = r.Depth;
                            bool end = false;
                            for (bool more = r.Read(); more && !end; )
                            {
                                // Procesa el nodo ACTUAL. Devuelve true si un helper
                                // (ReadElementContentAsString/Skip) movió el reader AL
                                // nodo siguiente (reprocesar sin Read).
                                bool advanced = false;
                                if (r.NodeType == XmlNodeType.EndElement && r.Depth == depth)
                                {
                                    end = true;   // </verse>
                                    break;
                                }
                                if (r.NodeType == XmlNodeType.Text || r.NodeType == XmlNodeType.CDATA ||
                                    r.NodeType == XmlNodeType.SignificantWhitespace)
                                {
                                    text.Append(r.Value);
                                }
                                else if (r.NodeType == XmlNodeType.Element)
                                {
                                    string sub = r.LocalName;
                                    if (!r.IsEmptyElement &&
                                        (string.Equals(sub, "w", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(sub, "seg", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(sub, "transChange", StringComparison.OrdinalIgnoreCase) ||
                                         string.Equals(sub, "divineName", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        // Palabra OSIS: texto real (elemento simple).
                                        text.Append(r.ReadElementContentAsString());
                                        advanced = true;
                                    }
                                    else if (string.Equals(sub, "lb", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(sub, "br", StringComparison.OrdinalIgnoreCase))
                                    {
                                        text.Append('\n');   // <lb/> es vacío: sin avance especial
                                    }
                                    else if (!r.IsEmptyElement &&
                                             (string.Equals(sub, "note", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(sub, "title", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(sub, "reference", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(sub, "catchWord", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(sub, "q", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(sub, "milestone", StringComparison.OrdinalIgnoreCase) ||
                                              string.Equals(sub, "hi", StringComparison.OrdinalIgnoreCase)))
                                    {
                                        r.Skip();      // editorial: descartar el contenido
                                        advanced = true;
                                    }
                                }
                                // advanced → el reader YA está en el nodo siguiente:
                                // la condición del for lo reprocesa sin avanzar.
                                more = advanced ? true : r.Read();
                            }
                        }
                        if (!unknownBook && curBook >= 1 && curBook <= 66 &&
                            curChapter >= 1 && vnum >= 1)
                        {
                            OsisVerse v = new OsisVerse();
                            v.Book = curBook;
                            v.Chapter = curChapter;
                            v.Verse = vnum;
                            v.Text = NormalizeSpace(text.ToString());
                            res.Verses.Add(v);
                            if (progress != null && (res.Verses.Count & 1023) == 0)
                            {
                                try { progress(res.Verses.Count, 0); }
                                catch (Exception) { }
                            }
                        }
                        else
                        {
                            res.SkippedRows++;
                        }
                    }
                }

                // Versión legible: header <work><title> gana sobre el identificador.
                if (workTitle.Length > 0) res.VersionName = workTitle;
            }
            return res;
        }

        /* ------------------------------------------------------ códigos ---- */

        /// <summary>Códigos OSIS de libro (tabla canónica 1..66) + variantes cortas.</summary>
        private static readonly Dictionary<string, int> BookCodes = BuildBookCodes();

        private static Dictionary<string, int> BuildBookCodes()
        {
            // Códigos OSIS canónicos (Bible Technologies Group).
            string[] codes = new string[] {
                "Gen","Exod","Lev","Num","Deut","Josh","Judg","Ruth","1Sam","2Sam",
                "1Kgs","2Kgs","1Chr","2Chr","Ezra","Neh","Esth","Job","Ps","Prov",
                "Eccl","Song","Isa","Jer","Lam","Ezek","Dan","Hos","Joel","Amos",
                "Obad","Jonah","Mic","Nah","Hab","Zeph","Hag","Zech","Mal",
                "Matt","Mark","Luke","John","Acts","Rom","1Cor","2Cor","Gal","Eph",
                "Phil","Col","1Thess","2Thess","1Tim","2Tim","Titus","Phlm","Heb","Jas",
                "1Pet","2Pet","1John","2John","3John","Jude","Rev"
            };
            // Variantes cortas frecuentes en biblias caseras.
            string[] alt = new string[] {
                "Gn","Ex","Lv","Nm","Dt","Jos","Jue","Rt","1S","2S","1R","2R",
                "1Cr","2Cr","Esd","Neh","Est"
            };
            Dictionary<string, int> map = new Dictionary<string, int>(96, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < codes.Length; i++) map[codes[i]] = i + 1;
            for (int i = 0; i < alt.Length && i < 17; i++) map[alt[i]] = i + 1;
            return map;
        }

        private static int BookFromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return 0;
            int b;
            return BookCodes.TryGetValue(code, out b) ? b : 0;
        }

        /// <summary>«Gen.1.1» → «Gen» (primer token antes de '.').</summary>
        private static string FirstToken(string osisId)
        {
            if (osisId == null) return string.Empty;
            int i = osisId.IndexOf('.');
            return i < 0 ? osisId : osisId.Substring(0, i);
        }

        /// <summary>«Gen.1.1» → «1» (token entre el primero y el último).</summary>
        private static string TokenAfterFirst(string osisId)
        {
            if (osisId == null) return null;
            int i = osisId.IndexOf('.');
            if (i < 0) return null;
            string rest = osisId.Substring(i + 1);
            int j = rest.IndexOf('.');
            return j < 0 ? rest : rest.Substring(0, j);
        }

        /// <summary>«Gen.1.1» / «Gen.1.1-Gen.1.2» / «Gen.1.1!a» → «1» (último segmento).</summary>
        private static string LastToken(string osisId)
        {
            if (osisId == null) return null;
            string s = osisId;
            int bang = s.IndexOf('!');
            if (bang > 0) s = s.Substring(0, bang);
            int dash = s.IndexOf('-');
            if (dash > 0) s = s.Substring(0, dash);
            int i = s.LastIndexOf('.');
            return i < 0 ? null : s.Substring(i + 1);
        }

        private static string TryVersionName(string path)
        {
            try
            {
                string n = Path.GetFileNameWithoutExtension(path);
                return n.Length == 0 ? string.Empty : n;
            }
            catch (Exception) { return string.Empty; }
        }

        private static string NormalizeSpace(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            // compacta espacios/tabs pero respeta '\n' (lb/br)
            string[] parts = s.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            StringBuilder sb = new StringBuilder(s.Length);
            for (int i = 0; i < parts.Length; i++)
            {
                string p = parts[i].Trim();
                if (p.Length == 0) continue;
                if (sb.Length > 0) sb.Append('\n');
                sb.Append(p);
            }
            return sb.ToString();
        }

        private static int ParseInt(string s)
        {
            if (string.IsNullOrEmpty(s)) return -1;
            // vnumber puede venir "1-2" o "1a": toma el prefijo numérico
            int end = 0;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end == 0) return -1;
            int v;
            if (!int.TryParse(s.Substring(0, end), out v)) return -1;
            return v;
        }
    }
}
