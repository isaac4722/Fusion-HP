// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  interop/FidelityReport.cs : informe de fidelidad GENERALIZADO (F4.15 del
//  Plan de Ultra Implementación — «Obligatorio en toda importación/exportación»).
//
//  Categorías normadas del informe:
//    * unidades convertidas    (slides / canciones / páginas / recursos)
//    * herencia resuelta       (propiedades que bajaron del tema/plantilla)
//    * elementos omitidos      (lo que NO viajó, con detalle por ítem)
//    * efectos no soportados   (animaciones, transiciones, …)
//    * macros detectadas       (ppt/vbaProject.bin — JAMÁS se ejecutan, F4.16.3)
//    * rutas re-vinculadas     (media reubicada a rutas relativas, F4.14.5)
//    * advertencias            (todo lo que el operador deba saber)
//    * resultado final         (estado OK + conteos consolidados)
//
//  Salidas: ToJson() (MiniJson, trazable/log) y ToHumanText() en español con
//  la plantilla humanizer del repo (qué pasó → acción sugerida → «Copiar
//  detalles técnicos») para el diálogo al operador [SPEC §11.2].
//
//  Mapeadores: los importadores/exportadores existentes YA producían
//  resultados con la misma semántica (HolyricsReport, PptxImportResult, …);
//  FromHolyrics / etc. los TRADUCEN sin romper sus APIs.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Text;
using lumina.core.import;

namespace lumina.core
{
    /// <summary>Una entrada estructurada del informe (categoría + detalle).</summary>
    public sealed class FidelityEntry
    {
        public string Category = "";    // ver categorías normadas arriba
        public string Detail = "";      // texto humano es-VE
        public int Count = 1;           // cuántas unidades representa

        public FidelityEntry() {}

        public FidelityEntry(string category, string detail, int count)
        {
            Category = category ?? "";
            Detail = detail ?? "";
            Count = count;
        }
    }

    /// <summary>
    /// Informe de fidelidad de una importación/exportación (F4.15). Acumulativo
    /// y tolerante: ningún método lanza; los detalles nulos se ignoran.
    /// </summary>
    public sealed class FidelityReport
    {
        /// <summary>Origen/destino del proceso (p. ej. "pptx", "pdf", "holyrics").</summary>
        public string Source = "";

        /// <summary>Resultado final: true si el proceso terminó sin errores graves.</summary>
        public bool Ok = true;

        private int _unitsConverted;         // unidades convertidas
        private int _inheritanceResolved;    // herencia resuelta
        private int _pathsRelinked;          // rutas re-vinculadas
        private readonly List<FidelityEntry> _entries = new List<FidelityEntry>();
        private readonly List<string> _warnings = new List<string>();

        public int UnitsConverted { get { return _unitsConverted; } }
        public int InheritanceResolved { get { return _inheritanceResolved; } }
        public int PathsRelinked { get { return _pathsRelinked; } }

        /// <summary>Omitidos, efectos no soportados y macros (entradas con conteo).</summary>
        public List<FidelityEntry> Entries { get { return new List<FidelityEntry>(_entries); } }
        public List<string> Warnings { get { return new List<string>(_warnings); } }

        /// <summary>Total de elementos omitidos (suma de entradas «omision»).</summary>
        public int OmittedCount { get { return SumOf("omision"); } }
        /// <summary>Total de efectos no soportados (suma de entradas «efecto»).</summary>
        public int UnsupportedCount { get { return SumOf("efecto"); } }
        /// <summary>Total de macros detectadas (suma de entradas «macro»).</summary>
        public int MacrosCount { get { return SumOf("macro"); } }

        private int SumOf(string category)
        {
            int n = 0;
            lock (_entries)
            {
                foreach (FidelityEntry e in _entries)
                    if (e.Category == category) n += e.Count;
            }
            return n;
        }

        // ------------------------------------------------------- acumuladores --
        /// <summary>Registra unidades convertidas (slide/canción/página) con detalle opcional.</summary>
        public void AddConverted(int count, string detail)
        {
            if (count <= 0) return;
            _unitsConverted += count;
            Add("unidad", detail, count);
        }

        /// <summary>Registra propiedades resueltas por herencia (tema→plantilla→…).</summary>
        public void AddInheritance(int count, string detail)
        {
            if (count <= 0) return;
            _inheritanceResolved += count;
            Add("herencia", detail, count);
        }

        /// <summary>Registra un elemento OMITIDO (tabla, gráfico, fila inválida…).</summary>
        public void AddOmission(string detail)
        {
            Add("omision", detail, 1);
        }

        /// <summary>Registra un efecto no soportado (animación, transición…).</summary>
        public void AddUnsupported(string detail)
        {
            Add("efecto", detail, 1);
        }

        /// <summary>Registra una macro DETECTADA (solo detección: nunca se ejecuta).</summary>
        public void AddMacro(string detail)
        {
            Add("macro", detail, 1);
        }

        /// <summary>Registra rutas re-vinculadas (media copiada a rutas relativas).</summary>
        public void AddRelinked(int count, string detail)
        {
            if (count <= 0) return;
            _pathsRelinked += count;
            Add("ruta", detail, count);
        }

        public void AddWarning(string detail)
        {
            if (string.IsNullOrEmpty(detail)) return;
            lock (_warnings) { _warnings.Add(detail); }
        }

        private void Add(string category, string detail, int count)
        {
            if (string.IsNullOrEmpty(detail)) return;
            lock (_entries) { _entries.Add(new FidelityEntry(category, detail, count)); }
        }

        // ------------------------------------------------------------- ToJson --
        /// <summary>JSON del informe (MiniJson): trazable, guardable en log.</summary>
        public string ToJson()
        {
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["format"] = "fidelity.v1";
            o["source"] = Source ?? "";
            o["ok"] = Ok;
            o["resultado"] = ResultDict();
            List<object> omit = new List<object>();
            List<object> efectos = new List<object>();
            List<object> macros = new List<object>();
            List<object> unidades = new List<object>();
            List<object> herencia = new List<object>();
            List<object> rutas = new List<object>();
            lock (_entries)
            {
                foreach (FidelityEntry e in _entries)
                {
                    switch (e.Category)
                    {
                        case "omision": omit.Add(e.Detail); break;
                        case "efecto": efectos.Add(e.Detail); break;
                        case "macro": macros.Add(e.Detail); break;
                        case "unidad": unidades.Add(e.Detail); break;
                        case "herencia": herencia.Add(e.Detail); break;
                        case "ruta": rutas.Add(e.Detail); break;
                    }
                }
            }
            o["unidadesConvertidas"] = unidades;
            o["herenciaResuelta"] = herencia;
            o["elementosOmitidos"] = omit;
            o["efectosNoSoportados"] = efectos;
            o["macrosDetectadas"] = macros;
            o["rutasRevinculadas"] = rutas;
            lock (_warnings) o["advertencias"] = new List<object>(_warnings.ToArray());
            return MiniJson.Serialize(o);
        }

        private Dictionary<string, object> ResultDict()
        {
            Dictionary<string, object> r = new Dictionary<string, object>();
            r["estado"] = Ok ? "ok" : "con-errores";
            r["unidadesConvertidas"] = (long)_unitsConverted;
            r["herenciaResuelta"] = (long)_inheritanceResolved;
            r["elementosOmitidos"] = (long)OmittedCount;
            r["efectosNoSoportados"] = (long)UnsupportedCount;
            r["macrosDetectadas"] = (long)MacrosCount;
            r["rutasRevinculadas"] = (long)_pathsRelinked;
            lock (_warnings) r["advertencias"] = (long)_warnings.Count;
            return r;
        }

        // -------------------------------------------------------- ToHumanText --
        /// <summary>
        /// Texto en español para el diálogo al operador, con la plantilla
        /// humanizer del repo: qué pasó → acción sugerida → «Copiar detalles
        /// técnicos» [SPEC §11.2]. Nunca vacío (aunque no haya nada que
        /// reportar, dice que el proceso fue fiel 1:1).
        /// </summary>
        public string ToHumanText()
        {
            StringBuilder sb = new StringBuilder();
            string verbo = string.Equals(Source, "holyrics", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Source, "pptx-import", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Source, "pptm", StringComparison.OrdinalIgnoreCase)
                ? "importación" : "exportación";
            // — Qué pasó (lenguaje no técnico) —
            sb.Append(verbo).Append(" terminada");
            if (!Ok) sb.Append(" CON PROBLEMAS");
            sb.Append(": ").Append(_unitsConverted)
              .Append(_unitsConverted == 1 ? " unidad convertida" : " unidades convertidas")
              .Append(".\n");
            int omit = OmittedCount, efectos = UnsupportedCount, macros = MacrosCount;
            if (_inheritanceResolved > 0)
                sb.Append("Herencia de tema resuelta en ").Append(_inheritanceResolved)
                  .Append(" punto(s) de estilo.\n");
            if (_pathsRelinked > 0)
                sb.Append("Rutas de medios re-vinculadas: ").Append(_pathsRelinked).Append(".\n");
            if (omit > 0)
                sb.Append("Se omitieron ").Append(omit)
                  .Append(" elemento(s) que este formato no puede representar.\n");
            if (efectos > 0)
                sb.Append("Efectos no soportados: ").Append(efectos)
                  .Append(" (no se pierde el texto ni el orden).\n");
            if (macros > 0)
                sb.Append("MACROS DETECTADAS: ").Append(macros)
                  .Append(" — fueron detectadas y NO se ejecutaron.\n");
            List<string> ws;
            lock (_warnings) { ws = new List<string>(_warnings); }
            if (ws.Count > 0)
            {
                sb.Append("Advertencias:\n");
                foreach (string w in ws) sb.Append("  • ").Append(w).Append("\n");
            }
            List<FidelityEntry> det;
            lock (_entries) { det = new List<FidelityEntry>(_entries); }
            if (det.Count > 0)
            {
                sb.Append("Detalle:\n");
                foreach (FidelityEntry e in det)
                    sb.Append("  [").Append(e.Category).Append("] ").Append(e.Detail).Append("\n");
            }
            if (omit == 0 && efectos == 0 && macros == 0 && ws.Count == 0 && Ok)
                sb.Append("Todo el contenido viajó fiel (1:1), sin omisiones.\n");
            // — Acción sugerida —
            sb.Append("\nPuedes continuar: ");
            if (macros > 0)
                sb.Append("revisa el archivo en PowerPoint si necesitas las macros (aquí nunca se ejecutan). ");
            else if (omit > 0 || efectos > 0)
                sb.Append("verifica la vista previa; lo omitido quedó listado en el detalle. ");
            else
                sb.Append("no se requiere ninguna acción. ");
            sb.Append("Si algo no se ve como esperabas, vuelve a importar/exportar desde el archivo original.\n");
            // — Copiar detalles técnicos —
            sb.Append("— Copiar detalles técnicos para soporte —\n");
            sb.Append(ToJson());
            return sb.ToString();
        }

        // ---------------------------------------------------------- mapeadores --
        /// <summary>
        /// F4.15 aplicado a Holyrics (F4.14): TRADUCE el HolyricsReport que el
        /// importador ya produce, sin tocar su API.
        /// </summary>
        public static FidelityReport FromHolyrics(HolyricsReport rep, string sourceFile)
        {
            FidelityReport fr = new FidelityReport();
            fr.Source = "holyrics";
            if (rep == null) { fr.Ok = false; return fr; }
            fr.Ok = true;
            fr.AddConverted(rep.Imported, "canciones convertidas a texto formateado");
            if (rep.Skipped > 0)
                fr.AddOmission(rep.Skipped + " canción(es) omitida(s) por existir ya en la biblioteca local sin autorización de sobrescritura");
            if (rep.Duplicated > 0)
                fr.AddOmission(rep.Duplicated + " canción(es) duplicada(s) dentro del lote");
            if (rep.BackgroundsLinked > 0)
                fr.AddRelinked(rep.BackgroundsLinked, "fondos copiados y vinculados con ruta relativa (media/)");
            foreach (string w in rep.Warnings) fr.AddWarning(w);
            return fr;
        }
    }
}
