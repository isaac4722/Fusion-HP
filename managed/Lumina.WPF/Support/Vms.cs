// ============================================================================
//  LuminaPresentation Suite v5.4.0 «ESTUDIO» — managed/Lumina.WPF/Support/Vms.cs
//  Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Vms.cs : pequeñas vistas de fila para las listas (sin MVVM completo:
//  code-behind deliberado, misma filosofía de la capa WinForms heredada).
// ============================================================================
namespace lumina.wpf
{
    /// <summary>Fila de resultado de búsqueda de canciones.</summary>
    public sealed class SongRowVm
    {
        public string Id = string.Empty;
        public string Title = string.Empty;
        public string Author = string.Empty;
        public string Snippet = string.Empty;
    }

    /// <summary>Fila de resultado de búsqueda bíblica (FTS5).</summary>
    public sealed class BibleRowVm
    {
        public string Ref = string.Empty;
        public string Text = string.Empty;
        /// <summary>Referencia canónica para «Cargar al escenario».</summary>
        public string LoadRef = string.Empty;
        public string Version = string.Empty;
        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #4): texto de la SEGUNDA versión al
        /// comparar (vacío = sin comparación).
        /// </summary>
        public string TextB = string.Empty;
        /// <summary>v7.1.0: nombre de la segunda versión (etiqueta de la fila).</summary>
        public string VersionB = string.Empty;
    }

    /// <summary>Fila de regla de activadores.</summary>
    public sealed class TriggerRowVm
    {
        public string Id = string.Empty;
        public string Name = string.Empty;
        public string Event = string.Empty;
        public string Match = string.Empty;
        public string Actions = string.Empty;
        public bool Enabled;

        /// <summary>«Activa»/«Inactiva» para la lista (color + texto, no solo color).</summary>
        public string EnabledLabel
        {
            get { return Enabled ? "Activa" : "Inactiva"; }
        }
    }

    /// <summary>Fila de la biblioteca de temas.</summary>
    public sealed class ThemeRowVm
    {
        public string Name = string.Empty;
        public bool InUse;

        /// <summary>Visibilidad de la marca «En uso» (binding directo, sin conversor).</summary>
        public System.Windows.Visibility InUseVisibility
        {
            get { return InUse ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed; }
        }
    }
}
