// ============================================================================
//  Fusion-HP · FusionShared/Model/AhpProjectInfo.cs — lector RÁPIDO de la
//  cabecera de un proyecto .ahp [v3.0.0 — bug «el cargador de Escenarios no
//  muestra nombres»]: antes el usuario elegía un archivo a ciegas. Ahora el
//  cargador (C# y pruebas) muestra el nombre del proyecto, cuántos escenarios
//  tiene y los TÍTULOS de cada uno antes de abrirlo [SPEC §5.3, §11.2.3].
//  Net35-compatible (perfil B).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using Fusion.Shared;

namespace Fusion.Shared.Model
{
    public class AhpProjectInfo
    {
        public string SourcePath;
        public string Name;
        public string ThemeRef;
        public List<string> ScenarioTitles = new List<string>();

        public int ScenarioCount { get { return ScenarioTitles.Count; } }

        /// <summary>Lee nombre y títulos de escenario sin cargar el modelo completo.
        /// Lanza InvalidDataException con mensaje humano si el archivo no es ahp.v1.</summary>
        public static AhpProjectInfo ReadHeader(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                throw new FileNotFoundException("No se encontró el proyecto.", path);
            JsonValue root;
            try
            {
                root = Json.ParseFile(path);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("El archivo no es un proyecto legible (JSON inválido). " + ex.Message);
            }
            string fmt = root.GetStr("format", "");
            if (fmt != "ahp.v1")
                throw new InvalidDataException("El archivo no es un proyecto Fusion HP (formato «" + fmt + "», se espera «ahp.v1»).");

            var info = new AhpProjectInfo { SourcePath = path };
            var proj = root.Get("project");
            info.Name = proj.GetStr("name", Path.GetFileNameWithoutExtension(path));
            info.ThemeRef = proj.GetStr("themeRef", null);
            var scns = proj.GetArray("scenarios");
            if (scns != null)
                foreach (var s in scns)
                    info.ScenarioTitles.Add(s.GetStr("title", "(sin título)"));
            return info;
        }
    }
}
