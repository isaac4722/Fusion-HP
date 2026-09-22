// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ZefaniaBible.cs : importador de Biblias en formato ZEFania XML (estándar
//  usado por Holyrics/OpenLP: Reina Valera 1960, etc. — requisito del spec §3.2).
//
//  Estructura soportada (tolerante):
//    <XMLBIBLE biblename="Reina Valera 1960" ...>
//      <BIBLEBOOK bnumber="1" bname="Génesis" bsname="Gn">
//        <CHAPTER cnumber="1">
//          <VERSE vnumber="1">En el principio... <BR/> ...</VERSE>
//        </CHAPTER>
//      </BIBLEBOOK>
//    </XMLBIBLE>
//
//  * STREAMING con XmlReader (pull): memoria constante con 31.084+ versículos
//    (objetivo de diseño: 4 GB RAM — nunca se carga el DOM completo).
//  * Tolerante: nombres de elemento case-insensitive, atributos opcionales,
//    <BR/> → '\n', texto interno concatenado, PROLOG/NOTES ignorados.
//  * Validación: bnumber 1..66, capítulo/versículo >= 1; filas inválidas se
//    cuentan y reportan (nunca abortan el importe completo).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace lumina.core
{
    /// <summary>Versículo parseado (fila lista para la tabla bible del núcleo).</summary>
    public sealed class ZefaniaVerse
    {
        public int Book;       // 1..66
        public int Chapter;
        public int Verse;
        public string Text = string.Empty;
    }

    /// <summary>Resultado del parseo.</summary>
    public sealed class ZefaniaResult
    {
        public string VersionName = string.Empty;   // atributo biblename (o el nombre de archivo)
        public List<ZefaniaVerse> Verses = new List<ZefaniaVerse>();
        public int SkippedRows;                      // filas descartadas por rango inválido
        public List<string> BooksSeen = new List<string>(); // bname en orden de aparición

        public int BookCount
        {
            get
            {
                HashSet<int> seen = new HashSet<int>();
                foreach (ZefaniaVerse v in Verses) seen.Add(v.Book);
                return seen.Count;
            }
        }
    }

    public static class ZefaniaBible
    {
        /// <summary>
        /// Parsea el archivo XML. progress: callback (versesLeídos, totalAprox)
        /// invocado cada 1024 versículos — no lanza excepciones.
        /// </summary>
        public static ZefaniaResult Parse(string path, Action<int, int> progress)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("Ruta vacía.");
            if (!File.Exists(path)) throw new FileNotFoundException("No se encuentra el archivo .xml.", path);
            using (FileStream fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return Parse(fs, progress, TryVersionName(path));
            }
        }

        public static ZefaniaResult Parse(Stream stream, Action<int, int> progress, string fallbackName)
        {
            ZefaniaResult res = new ZefaniaResult();
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

                while (r.Read())
                {
                    if (r.NodeType != XmlNodeType.Element) continue;
                    string name = r.LocalName;
                    if (string.Equals(name, "XMLBIBLE", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(name, "Bible", StringComparison.OrdinalIgnoreCase))
                    {
                        string bn = r.GetAttribute("biblename");
                        if (string.IsNullOrEmpty(bn)) bn = r.GetAttribute("name");
                        if (!string.IsNullOrEmpty(bn)) res.VersionName = bn.Trim();
                    }
                    else if (string.Equals(name, "BIBLEBOOK", StringComparison.OrdinalIgnoreCase) ||
                             string.Equals(name, "BOOK", StringComparison.OrdinalIgnoreCase))
                    {
                        curBook = ParseInt(r.GetAttribute("bnumber"));
                        string bname = r.GetAttribute("bname");
                        if (curBook >= 1 && curBook <= 66 && !string.IsNullOrEmpty(bname) &&
                            booksSeen.Add(curBook.ToString(CultureInfoInv)))
                        {
                            res.BooksSeen.Add(bname.Trim());
                        }
                        curChapter = 0;
                    }
                    else if (string.Equals(name, "CHAPTER", StringComparison.OrdinalIgnoreCase))
                    {
                        curChapter = ParseInt(r.GetAttribute("cnumber"));
                    }
                    else if (string.Equals(name, "VERSE", StringComparison.OrdinalIgnoreCase))
                    {
                        int vnum = ParseInt(r.GetAttribute("vnumber"));
                        text.Length = 0;
                        if (!r.IsEmptyElement)
                        {
                            // Contenido mixto: texto + <BR/> + <STYLE>/<NOTE> anidados
                            int depth = r.Depth;
                            while (r.Read() && !(r.NodeType == XmlNodeType.EndElement &&
                                                 string.Equals(r.LocalName, name, StringComparison.OrdinalIgnoreCase) &&
                                                 r.Depth == depth))
                            {
                                if (r.NodeType == XmlNodeType.Text || r.NodeType == XmlNodeType.CDATA ||
                                    r.NodeType == XmlNodeType.SignificantWhitespace)
                                {
                                    text.Append(r.Value);
                                }
                                else if (r.NodeType == XmlNodeType.Element &&
                                         string.Equals(r.LocalName, "BR", StringComparison.OrdinalIgnoreCase))
                                {
                                    text.Append('\n');
                                }
                                else if (r.NodeType == XmlNodeType.Element &&
                                         string.Equals(r.LocalName, "STYLE", StringComparison.OrdinalIgnoreCase))
                                {
                                    text.Append(r.ReadInnerXml()); // el estilo es texto real
                                }
                            }
                        }
                        if (curBook >= 1 && curBook <= 66 && curChapter >= 1 && vnum >= 1)
                        {
                            ZefaniaVerse v = new ZefaniaVerse();
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
            }
            return res;
        }

        /// <summary>Nombre de versión sugerido desde el nombre del archivo.</summary>
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
            // compacta espacios/tabs pero respeta '\n' (BR)
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
            int v;
            // vnumber puede venir "1-2" o "1a": toma el prefijo numérico
            int end = 0;
            while (end < s.Length && char.IsDigit(s[end])) end++;
            if (end == 0) return -1;
            if (!int.TryParse(s.Substring(0, end), out v)) return -1;
            return v;
        }

        private static System.Globalization.CultureInfo CultureInfoInv
        {
            get { return System.Globalization.CultureInfo.InvariantCulture; }
        }
    }
}
