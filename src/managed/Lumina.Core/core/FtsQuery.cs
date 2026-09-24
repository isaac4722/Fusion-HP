// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  FtsQuery.cs — saneamiento de términos para MATCH de FTS5 (F1.07).
//
//  El usuario escribe «juan 3:16» o «gloria "dios» en la búsqueda en caliente:
//  FTS5 interpreta  :  como filtro de columna y las comillas como frases, y
//  devuelve un error de sintaxis (o resultados erróneos). El contrato del
//  buscador es tratar el término completo como TEXTO: cada token se limpia y
//  se entrecomilla ("token"), unidos por espacios (AND implícito).
//
//  Es dominio PURO de Core (sin UI, sin núcleo) para que el arnés lo pruebe
//  sin nativo y ambas shells (WPF/WinForms) compartan el mismo comportamiento.
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;

namespace lumina.core
{
    public static class FtsQuery
    {
        /// <summary>
        /// Convierte el término del usuario en una consulta FTS5 segura:
        /// tokens entrecomillados unidos por espacios. Vacío si no queda
        /// nada útil (el llamador omite la búsqueda).
        /// </summary>
        public static string Sanitize(string term)
        {
            // net35: sin IsNullOrWhiteSpace → equivalente con Trim.
            if (term == null || term.Trim().Length == 0) return string.Empty;
            // Los separadores FTS5 (: filtro de columna, () agrupación) se
            // sustituyen por espacio ANTES de trocear: «juan 3:16» debe dar
            // tokens «juan» «3» «16», no la frase «3 16».
            term = term.Replace("\"", " ").Replace("'", " ").Replace("*", " ")
                       .Replace("(", " ").Replace(")", " ").Replace(":", " ");
            string[] parts = term.Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            List<string> quoted = new List<string>(parts.Length);
            foreach (string p in parts)
            {
                string clean = p.Trim();
                if (clean.Length > 0) quoted.Add("\"" + clean + "\"");
            }
            return string.Join(" ", quoted.ToArray());
        }

        private static readonly char[] Separators = new char[] { ' ', '\t', '\r', '\n' };

        /// <summary>Cultura invariante para las comparaciones (norma del repo).</summary>
        public static bool IsUseful(string sanitized)
        {
            return !string.IsNullOrEmpty(sanitized);
        }

        /// <summary>Comparador invariante expuesto para tests coherentes.</summary>
        public static StringComparer Comparer { get { return StringComparer.OrdinalIgnoreCase; } }

        /// <summary>Convenience: minúsculas invariante (sin efectos en FTS5 con unicode61).</summary>
        public static string Fold(string term)
        {
            return term == null ? string.Empty : term.ToLower(CultureInfo.InvariantCulture);
        }
    }
}
