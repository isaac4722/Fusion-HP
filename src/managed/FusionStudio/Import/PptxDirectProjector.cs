// ============================================================================
//  Fusion-HP · Import/PptxDirectProjector.cs — proyección del PPTX ORIGINAL
//  tal cual, SIN PowerPoint instalado [v3.0.0 — bug «extrae en vez de cargar
//  el original», SPEC §9.2].
//
//  Qué hace: lee el archivo .pptx original (contenedor OPC, System.IO.
//  Packaging), resuelve la herencia Diapositiva→Diseño→Maestro→Tema y entrega
//  el programa AL MOTOR directamente. Nada se convierte ni se guarda: el .pptx
//  sigue siendo la única fuente; cada carga re-lee el original. Las imágenes
//  incrustadas se materializan en una CACHÉ de medios (datos/media-cache/),
//  jamás en el proyecto del usuario — es un detalle técnico para que el Motor
//  pueda decodificarlas, y así se declara en el informe de fidelidad.
//
//  Si PowerPoint está instalado, el flujo de Inicio prefiere el COM
//  (PptxComLoader, fidelidad 100 %); este proyector es la vía nativa que
//  elimina la dependencia dura de PowerPoint.
// ============================================================================
using System;
using System.IO;
using Fusion.Shared;
using Fusion.Shared.Model;
using Fusion.Studio.Services;

namespace Fusion.Studio.Import
{
    /// <summary>Informe de fidelidad de la proyección directa [SPEC §9.3.4].</summary>
    public class PptxDirectReport
    {
        public string SourcePath;
        public int Slides;
        public List<string> Warnings = new List<string>();
    }

    public static class PptxDirectProjector
    {
        /// <summary>Proyecta el PPTX original tal cual (sin PowerPoint, sin
        /// conversión persistente). Devuelve el informe de fidelidad.</summary>
        public static PptxDirectReport ProjectOriginal(string pptxPath, LiveOrchestrator live)
        {
            if (live == null) throw new ArgumentNullException("live");
            if (string.IsNullOrEmpty(pptxPath) || !File.Exists(pptxPath))
                throw new FileNotFoundException("No se encontró el archivo PPTX.", pptxPath);

            // Contenedor EN MEMORIA: nunca se guarda como .ahp del usuario.
            var proj = AhpProject.CreateDefault();
            proj.Name = Path.GetFileNameWithoutExtension(pptxPath);

            // Caché de medios por archivo (hash corto del nombre+tamaño): la
            // proyección re-lee el original cada vez; la caché solo materializa
            // imágenes para el decodificador del Motor.
            string hash = StableHash(pptxPath + "|" + new FileInfo(pptxPath).Length);
            string cacheRoot = Path.Combine(live.Settings.DataDir,
                Path.Combine("media-cache", "pptx-" + hash));
            Directory.CreateDirectory(cacheRoot);

            var importer = new PptxOpenXmlImporter();
            var importRep = importer.Import(pptxPath, proj, Path.Combine(cacheRoot, "media"));

            var rep = new PptxDirectReport { SourcePath = pptxPath, Slides = importRep.Scenarios };
            rep.Warnings.AddRange(importRep.Warnings);
            if (importRep.Scenarios == 0)
                rep.Warnings.Add("El archivo no contiene diapositivas proyectables.");
            rep.Warnings.Add("Proyección directa: el programa en vivo proviene SIEMPRE del archivo original «" +
                Path.GetFileName(pptxPath) + "»; las imágenes incrustadas se materializan en la caché de medios.");

            live.ProjectExternal(proj, pptxPath, cacheRoot);
            return rep;
        }

        /// <summary>Hash determinista corto (FNV-1a 32 bits, sin dependencias).</summary>
        static string StableHash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h.ToString("x8");
        }
    }
}
