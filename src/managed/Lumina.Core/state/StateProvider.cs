// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  StateProvider.cs : proveedor de estado central de la capa gestionada
//  (mapa normativo de AGENT.md: src/managed/state/).
//
//  Función (F5.01.2 — comunicación bidireccional): exponer un RETRATO
//  inmutable del estado operativo actual (modo, elemento activo, línea
//  activa, tema, pantalla de reposo, flags de perfil) para:
//    - el servidor API (GET /api/v1/state),
//    - el Stage View y el control remoto móvil,
//    - la ventana de diagnóstico (Ayuda → Estado del sistema).
//
//  El estado se publica desde el hilo de la UI y se consulta desde hilos de
//  red; el acceso es thread-safe por intercambio de referencia atómica
//  (el retrato es inmutable una vez publicado — sin bloqueos en la lectura).
// ============================================================================
namespace lumina.core
{
    /// <summary>Retrato inmutable del estado operativo (F5.01.2).</summary>
    public sealed class StateSnapshot
    {
        public string Mode = "live";              // live | creacion
        public string ScenarioId = "";            // escenario activo (ID estable)
        public string ScenarioTitle = "";
        public int ElementIndex = -1;             // elemento activo
        public int LineIndex = -1;                // línea activa (sync por línea)
        public int ElementCount = 0;
        public int LineCount = 0;
        public string ThemeRef = "";              // tema activo (herencia 4 niveles)
        public string RestScreen = "black";       // black | logo | fondo-fijo
        public bool RestActive = false;           // pantalla de reposo activa
        public bool OutputVisible = false;        // salida proyectando
        public string Profile = "A";              // perfil A/B/C activo
        public bool VideoPlaying = false;
        public double Volume = 100.0;

        /// <summary>Serialización JSON del estado (MiniJson, sin dependencias).</summary>
        public string ToJson()
        {
            var o = new System.Collections.Generic.Dictionary<string, object>
            {
                { "mode", Mode },
                { "scenario", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "id", ScenarioId },
                        { "title", ScenarioTitle },
                        { "elementIndex", ElementIndex },
                        { "elementCount", ElementCount },
                    } },
                { "line", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "index", LineIndex },
                        { "count", LineCount },
                    } },
                { "themeRef", ThemeRef },
                { "rest", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "screen", RestScreen },
                        { "active", RestActive },
                    } },
                { "output", new System.Collections.Generic.Dictionary<string, object>
                    {
                        { "visible", OutputVisible },
                        { "videoPlaying", VideoPlaying },
                        { "volume", Volume },
                    } },
                { "profile", Profile },
            };
            return MiniJson.Encode(o);
        }
    }

    /// <summary>
    /// Publicador/consultor thread-safe del estado operativo. La UI publica
    /// (Publish), los consumidores leen (Current) — retratos inmutables.
    /// </summary>
    public sealed class StateProvider
    {
        private volatile StateSnapshot _current = new StateSnapshot();

        /// <summary>Retrato actual (inmutable; nunca null).</summary>
        public StateSnapshot Current { get { return _current; } }

        /// <summary>Publica un nuevo retrato (la UI decide la cadencia).</summary>
        public void Publish(StateSnapshot snapshot)
        {
            if (snapshot != null)
                _current = snapshot;
        }

        /// <summary>Actualiza campos sobre el retrato actual y republica.</summary>
        public void Update(System.Action<StateSnapshot> mutate)
        {
            StateSnapshot next = Clone(Current);
            if (mutate != null) mutate(next);
            Publish(next);
        }

        private static StateSnapshot Clone(StateSnapshot s)
        {
            return new StateSnapshot
            {
                Mode = s.Mode,
                ScenarioId = s.ScenarioId,
                ScenarioTitle = s.ScenarioTitle,
                ElementIndex = s.ElementIndex,
                LineIndex = s.LineIndex,
                ElementCount = s.ElementCount,
                LineCount = s.LineCount,
                ThemeRef = s.ThemeRef,
                RestScreen = s.RestScreen,
                RestActive = s.RestActive,
                OutputVisible = s.OutputVisible,
                Profile = s.Profile,
                VideoPlaying = s.VideoPlaying,
                Volume = s.Volume,
            };
        }
    }
}
