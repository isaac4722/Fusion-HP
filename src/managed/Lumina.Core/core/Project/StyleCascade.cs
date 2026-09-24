// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Project/StyleCascade.cs : herencia de estilos con la cascada EXACTA del
//  documento técnico §5.4 (F2.08 del Plan de Ultra Implementación):
//
//        Tema → Plantilla de Escenario → Escenario → Elemento
//
//  Reglas implementadas:
//   * F2.08.1  Propiedad ausente/nula se resuelve desde el nivel inferior
//              (busca hacia arriba; "" y null significan HEREDAR).
//   * F2.08.2  «null significa heredar» — representado como clave ausente.
//   * F2.08.5  la UI muestra el ORIGEN EFECTIVO de cada propiedad.
//   * F2.08.6  el exportador PPTX consume la MISMA cascada (API pública).
//   * El tema puede cambiar durante la proyección (F2.08.3): Resolve() es
//              pura y sin estado — al cambiar el tema se re-renderiza.
//
//  Multi-target net35/net48/net8, C# 7.3.
// ============================================================================
using System;
using System.Collections.Generic;

namespace lumina.core.project
{
    /// <summary>Origen de una propiedad efectiva (F2.08.5).</summary>
    public enum AhpStyleOrigin { Default = 0, Tema = 1, Plantilla = 2, Escenario = 3, Elemento = 4 }

    /// <summary>Propiedad resuelta con su valor y de dónde vino.</summary>
    public sealed class AhpStyleValue
    {
        public string Name;
        public string Value;
        public AhpStyleOrigin Origin;
        public string OriginLabel
        {
            get
            {
                switch (Origin)
                {
                    case AhpStyleOrigin.Tema: return "tema";
                    case AhpStyleOrigin.Plantilla: return "plantilla";
                    case AhpStyleOrigin.Escenario: return "escenario";
                    case AhpStyleOrigin.Elemento: return "elemento";
                }
                return "predeterminado";
            }
        }
    }

    /// <summary>
    /// Cascada Tema→Plantilla→Escenario→Elemento. Los niveles son mapas
    /// nombre→valor (clave ausente o valor null/"" = heredar). El orden de
    /// los argumentos ES la precedencia (de mayor a menor).
    /// </summary>
    public static class StyleCascade
    {
        /// <summary>Resuelve una propiedad (F2.08.1). null/"" heredan hacia
        /// abajo; si nadie la define devuelve def.</summary>
        public static string Resolve(string prop, string def,
                                     params IDictionary<string, string>[] levels)
        {
            if (levels == null) return def;
            // levels[0] = nivel MÁS específico (elemento), último = tema.
            for (int i = 0; i < levels.Length; i++)
            {
                IDictionary<string, string> lv = levels[i];
                if (lv == null) continue;
                string v;
                if (lv.TryGetValue(prop, out v) && !string.IsNullOrEmpty(v))
                    return v;
            }
            return def;
        }

        /// <summary>Resuelve con ORIGEN (F2.08.5: la UI muestra de dónde
        /// viene cada propiedad efectiva).</summary>
        public static AhpStyleValue ResolveWithOrigin(string prop, string def,
                                                      params IDictionary<string, string>[] levels)
        {
            AhpStyleValue r = new AhpStyleValue();
            r.Name = prop;
            r.Value = def;
            r.Origin = AhpStyleOrigin.Default;
            if (levels == null) return r;
            for (int i = 0; i < levels.Length; i++)
            {
                IDictionary<string, string> lv = levels[i];
                if (lv == null) continue;
                string v;
                if (lv.TryGetValue(prop, out v) && !string.IsNullOrEmpty(v))
                {
                    r.Value = v;
                    // El orden de levels define el origen: i=0 es el nivel MÁS
                    // específico. Con 4 niveles (elemento, escenario, plantilla,
                    // tema): i=0→Elemento(4) … i=3→Tema(1).
                    r.Origin = (AhpStyleOrigin)Math.Max(1, Math.Min(4, levels.Length - i));
                    return r;
                }
            }
            return r;
        }

        /// <summary>Vista completa: para cada propiedad UNION de todos los
        /// niveles, el valor efectivo + origen (panel «Estilo efectivo»).</summary>
        public static List<AhpStyleValue> EffectiveView(
            IEnumerable<string> properties,
            IDictionary<string, string> tema,
            IDictionary<string, string> plantilla,
            IDictionary<string, string> escenario,
            IDictionary<string, string> elemento)
        {
            List<AhpStyleValue> list = new List<AhpStyleValue>();
            foreach (string prop in properties)
            {
                AhpStyleValue v = ResolveWithOrigin(prop, "", elemento, escenario,
                                                    plantilla, tema);
                if (string.IsNullOrEmpty(v.Value)) { v.Value = "(heredado)"; }
                list.Add(v);
            }
            return list;
        }

        /// <summary>Construye el mapa de nivel desde el override JSON plano
        /// («fontSize:54;fontColor:#fff») — el formato de StyleOverride del
        /// modelo ahp.v1. Tolerante a espacios y separadores.</summary>
        public static Dictionary<string, string> ParseOverride(string flat)
        {
            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(flat)) return map;
            foreach (string part in flat.Split(';'))
            {
                if (string.IsNullOrEmpty(part)) continue;
                int eq = part.IndexOf(':');
                if (eq <= 0) continue;
                string k = part.Substring(0, eq).Trim();
                string v = part.Substring(eq + 1).Trim();
                if (k.Length > 0 && v.Length > 0) map[k] = v;
            }
            return map;
        }
    }
}
