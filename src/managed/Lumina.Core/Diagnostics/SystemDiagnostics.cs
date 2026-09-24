// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Diagnostics/SystemDiagnostics.cs : «Ayuda → Estado del sistema» +
//  «Verificar entorno» (F5.10) y medición continua (F6.01 — Sección 10).
//
//  F5.10 — Estado del sistema muestra: SO, arquitectura, perfil A/B/C,
//  monitores y destino, API, Triggers, Drive, Planning Center, OBS, NDI,
//  JSLib, RAM, render, importaciones, cola de render, estado de logs.
//  Botón «Verificar entorno»: render, multimedia, red local, permisos de
//  carpetas, API, pipes, runtime, recursos → verde/ámbar/rojo por componente.
//
//  F6.01 — instrumentación: RAM por subsistema, working set, memoria pico,
//  tiempo de render por frame, tiempo de importación, tamaño de cola,
//  comandos descartados, latencia de transición, latencia IPC, conexiones
//  API, scripts JSLib, errores — volcado de contadores cada 60 s en Live.
//
//  net35-compatible (C# 7.3): PerformanceCounter NO se usa (no portable);
//  el working set llega por Process (System.Diagnostics) y las métricas del
//  núcleo por el estado JSON del motor (ipc/render/log ya lo exponen).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using lumina.core;

namespace lumina.core.diagnostics
{
    /// <summary>Color semáforo de cada verificación (F5.10: verde/ámbar/rojo).</summary>
    public enum DiagLevel { Green = 0, Amber = 1, Red = 2 }

    /// <summary>Una comprobación del «Verificar entorno».</summary>
    public sealed class DiagCheck
    {
        public string Name = "";         // render, multimedia, red local…
        public DiagLevel Level = DiagLevel.Green;
        public string Detail = "";       // humano, accionable (§11)
    }

    /// <summary>Estado completo del sistema (F5.10 lista completa).</summary>
    public sealed class SystemStateInfo
    {
        public string Os = "", Arch = "", Profile = "";    // F0.01-F0.04
        public string EnvJson = "";                        // lumina_env_detect completo
        public string Monitors = "";                       // provee la UI (Screen)
        public string Api = "", Triggers = "", Drive = "", PlanningCenter = "";
        public string Obs = "", Ndi = "", Jslib = "";
        public double WorkingSetMb, PeakMb;                // F6.01
        public string RenderJson = "";                     // metrics del projector
        public string IpcJson = "";                        // cola/latencia
        public string LogJson = "";
        public string Imports = "";                        // último informe
        public long ErrorsCount;
    }

    /// <summary>Medición continua (F6.01): contadores + volcado cada 60 s.</summary>
    public sealed class MetricsCollector
    {
        private readonly object _gate = new object();
        private readonly Dictionary<string, long> _counters =
            new Dictionary<string, long>();
        private readonly Dictionary<string, double> _gauges =
            new Dictionary<string, double>();
        private readonly List<string> _dumps = new List<string>();
        private DateTime _lastDumpUtc = DateTime.UtcNow;
        private double _renderMsSum, _renderMsMax;
        private long _renderFrames;
        private long _ipcLatencySum, _ipcLatencyN;
        private double _importMsSum;
        private long _importN;

        public void Count(string key, long delta)
        {
            lock (_gate)
            {
                long v;
                _counters.TryGetValue(key, out v);
                _counters[key] = v + delta;
            }
        }
        public void Gauge(string key, double value)
        {
            lock (_gate) { _gauges[key] = value; }
        }
        /// <summary>F6.01: tiempo de render por frame (del estado del motor).</summary>
        public void ObserveRenderMs(double ms)
        {
            lock (_gate)
            {
                _renderMsSum += ms; if (ms > _renderMsMax) _renderMsMax = ms;
                _renderFrames++;
            }
        }
        /// <summary>F6.01: latencia IPC (PING/PONG del cliente).</summary>
        public void ObserveIpcLatencyMs(int ms)
        {
            lock (_gate) { _ipcLatencySum += ms; _ipcLatencyN++; }
        }
        /// <summary>F6.01: tiempo de importación.</summary>
        public void ObserveImportMs(double ms)
        {
            lock (_gate) { _importMsSum += ms; _importN++; }
        }

        /// <summary>F6.01: volcado cada 60 s durante Live. Devuelve el JSON del
        /// volcado si tocó (cada 60 s); null si aún no toca.</summary>
        public string Tick60(string context)
        {
            lock (_gate)
            {
                if ((DateTime.UtcNow - _lastDumpUtc).TotalSeconds < 60) return null;
                _lastDumpUtc = DateTime.UtcNow;
                string dump = SnapshotJson(context);
                _dumps.Add(dump);
                if (_dumps.Count > 240) _dumps.RemoveRange(0, _dumps.Count - 240);
                return dump;
            }
        }

        public string SnapshotJson(string context)
        {
            lock (_gate)
            {
                Dictionary<string, object> o = new Dictionary<string, object>();
                o["ts"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ",
                    System.Globalization.CultureInfo.InvariantCulture);
                o["context"] = context ?? "live";
                o["ramWorkingSetMb"] = Math.Round((double)WorkingSetMb(), 1);
                o["ramPeakMb"] = Math.Round((double)PeakMb(), 1);
                o["renderFrames"] = _renderFrames;
                o["renderAvgMs"] = _renderFrames > 0
                    ? Math.Round(_renderMsSum / _renderFrames, 3) : 0.0;
                o["renderMaxMs"] = Math.Round(_renderMsMax, 3);
                o["ipcLatencyAvgMs"] = _ipcLatencyN > 0
                    ? Math.Round((double)_ipcLatencySum / _ipcLatencyN, 2) : 0.0;
                o["importAvgMs"] = _importN > 0 ? Math.Round(_importMsSum / _importN, 0) : 0.0;
                Dictionary<string, object> ctr = new Dictionary<string, object>();
                foreach (KeyValuePair<string, long> kv in _counters)
                    ctr[kv.Key] = (double)kv.Value;
                o["counters"] = ctr;
                Dictionary<string, object> gg = new Dictionary<string, object>();
                foreach (KeyValuePair<string, double> kv in _gauges)
                    gg[kv.Key] = kv.Value;
                o["gauges"] = gg;
                return MiniJson.Serialize(o);
            }
        }

        /// <summary>Últimos volcados (F6.08: log sin ERROR injustificado).</summary>
        public List<string> Dumps()
        {
            lock (_gate) { return new List<string>(_dumps); }
        }

        public static double WorkingSetMb()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                    return p.WorkingSet64 / (1024.0 * 1024.0);
            }
            catch (Exception) { return 0; }
        }
        public static double PeakMb()
        {
            try
            {
                using (Process p = Process.GetCurrentProcess())
                    return p.PeakWorkingSet64 / (1024.0 * 1024.0);
            }
            catch (Exception) { return 0; }
        }
    }

    /// <summary>
    /// «Verificar entorno» (F5.10): comprobaciones locales con proveedores
    /// inyectables (la UI aporta pantallas; el motor aporta render/pipes;
    /// los integradores su estado). Todo en lenguaje humano.
    /// </summary>
    public static class EnvironmentVerifier
    {
        /// <summary>Ejecuta las 8 verificaciones obligatorias del plan.</summary>
        public static List<DiagCheck> Verify(
            Func<DiagCheck> render,        // 1 render (motor)
            Func<DiagCheck> multimedia,    // 2 multimedia (DirectShow/códecs)
            Func<DiagCheck> network,       // 3 red local (ping a la puerta)
            string dataDir,                // 4 permisos de carpetas
            Func<DiagCheck> api,           // 5 API (listener activo)
            Func<DiagCheck> pipes,         // 6 pipes (IPC ipc.v1)
            Func<DiagCheck> runtime,       // 7 runtime (.NET real)
            Func<DiagCheck> resources)     // 8 recursos (biblia/bibliotecas)
        {
            List<DiagCheck> checks = new List<DiagCheck>();
            checks.Add(render != null ? render() : Pending("render"));
            checks.Add(multimedia != null ? multimedia() : Pending("multimedia"));
            checks.Add(network != null ? network() : Pending("red local"));
            checks.Add(CheckFolder(dataDir));
            checks.Add(api != null ? api() : Pending("API"));
            checks.Add(pipes != null ? pipes() : Pending("pipes"));
            checks.Add(runtime != null ? runtime() : Pending("runtime"));
            checks.Add(resources != null ? resources() : Pending("recursos"));
            return checks;
        }

        private static DiagCheck Pending(string name)
        {
            DiagCheck c = new DiagCheck();
            c.Name = name;
            c.Level = DiagLevel.Amber;
            c.Detail = "sin comprobar en este entorno";
            return c;
        }

        /// <summary>4: permisos de escritura en la carpeta de datos.</summary>
        public static DiagCheck CheckFolder(string dataDir)
        {
            DiagCheck c = new DiagCheck();
            c.Name = "permisos de carpetas";
            if (string.IsNullOrEmpty(dataDir))
            {
                c.Level = DiagLevel.Red;
                c.Detail = "No hay carpeta de datos configurada.";
                return c;
            }
            try
            {
                string probe = Path.Combine(dataDir, ".lumina-probe");
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                c.Level = DiagLevel.Green;
                c.Detail = "Escritura correcta en " + dataDir;
            }
            catch (UnauthorizedAccessException)
            {
                c.Level = DiagLevel.Red;
                c.Detail = "Sin permiso de escritura en " + dataDir +
                    ". Ejecute desde una carpeta donde su usuario pueda escribir " +
                    "(o use la versión instalada).";
            }
            catch (IOException)
            {
                c.Level = DiagLevel.Amber;
                c.Detail = "El disco no respondió al probar " + dataDir + ".";
            }
            return c;
        }

        /// <summary>3: red local — puerta por defecto alcanzable (sin Internet).</summary>
        public static DiagCheck CheckLocalNetwork(Func<bool> probe)
        {
            DiagCheck c = new DiagCheck();
            c.Name = "red local";
            bool ok = probe != null && probe();
            c.Level = ok ? DiagLevel.Green : DiagLevel.Amber;
            c.Detail = ok
                ? "Red local disponible (los mandos remotos pueden conectarse)."
                : "No se pudo comprobar la red local (los mandos remotos no " +
                  "conectarán, el resto del programa funciona).";
            return c;
        }
    }
}
