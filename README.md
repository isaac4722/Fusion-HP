# LuminaPresentation Suite — Aplicación Híbrida de Presentación Litúrgica y Multimedia

**v1.0.0-beta.1 «REESTRUCTURA»** · Línea normativa del documento técnico v1.1
(`spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`) y del Plan de Ultra
Implementación (`spec/plan-ultra-implementacion.md`, fixtures F0.01–F6.10).

## Qué es

Programa de escritorio nativo para Windows (Win7 x86 SP1 → Win11 x64) que
**fusiona el presentador en vivo de Holyrics con la potencia de creación de
PowerPoint**, en una sola aplicación ligera, offline primero y sin dependencias
frágiles (cero Java, cero hacks del Registro).

- **Modo Live/Presentación** (arranque por defecto): salida borderless a
  pantalla completa, sincronización línea por línea, cero parpadeo, atajos
  personalizables, pantalla de reposo (negro/logo/fondo).
- **Modo Creación**: editor de Escenarios (WinForms shell + editor WPF),
  herencia de estilos de 4 niveles (Tema→Plantilla→Escenario→Elemento),
  biblioteca de canciones/Biblias/recursos con búsqueda instantánea.
- **Automatización**: API HTTP local con token Bearer (6 endpoints), Triggers
  evento→condición→acción, OBS WebSocket, salida NDI, control remoto móvil con
  código QR, Planning Center, Google Drive (F6) y sandbox JSLib.
- **Interoperabilidad**: importadores (Zefania XML, `.bib` e-Sword, JSON,
  PPTX/OPC, datos Holyrics) y exportadores (PPTX, PDF etiquetado, imágenes).
- **Modelo nativo**: `ahp.v1` (JSON versionado, ZIP opcional con `media/`).

## Arquitectura (dual normativa)

```
├─ Núcleo nativo C++ (Win32/CRT, /MT, x86+x64)   — lo que NO puede fallar
│    bootstrap + detección (SO/arq/.NET → perfil A/B/C), render (Direct2D/
│    GDI+), DirectShow, salida borderless, IPC ipc.v1, log binario, UI de
│    emergencia (perfil C: proyecta sin .NET).
├─ Capa gestionada C# (.NET Framework 3.5 SP1→4.8, WinForms + WPF)
│    shell, editor, modelo ahp.v1, importadores/exportadores, API HTTP,
│    Triggers, OBS/NDI/PCO/Drive, JSLib, diagnóstico.
└─ Persistencia: ahp.v1 + SQLite/FTS5 + configuración JSON + logs rotativos.
```

## Estructura del repo (mapa normativo de `AGENT.md`)

| Ruta | Contenido |
|---|---|
| `src/core/` | Proyecto MSBuild del núcleo C++ (`.vcxproj`, x86+x64, `/MT`) + módulos nuevos de la línea reestructurada |
| `native/core/` | Fuentes base del núcleo (ruta congelada por la restricción CMake del plan; compiladas por el `.vcxproj` y por el CMake heredado) |
| `src/managed/` | Capa C# reestructurada: `core/ data/ services/ state/ features/` dentro de `Lumina.Core`, más `Lumina.Api`, `Lumina.Bridge`, `Lumina.UI` (WinForms), `Lumina.WPF` (editor), `Tests`, `PoC` |
| `tests/core.Tests/` | Arnés portátil g++ del núcleo (verificación local sin MSVC) |
| `installer/` | Inno Setup dual (x86/x64) + empaquetado portable con SHA256SUMS |
| `spec/` | Documento técnico v1.1 + plan de implementación (fuente normativa) |
| `docs/agent/` | Documentación con alcance para agentes (AGENT.md §Progressive Disclosure) |
| `docs/research/` | Búsquedas `duckduckgo_search` del estado PLAN |
| `docs/verification/` | Evidencias de verificación local (builds, tests, métricas) |
| `.agents/skills/` | Skills del agente (catálogo y procedencia) |

## Compilación (mecanismos permitidos — el plan prohíbe tocar CMake)

| Objetivo | Comando |
|---|---|
| Solución gestionada completa (net35+net48+net8) | `dotnet build src/managed/Lumina.sln -c Release` |
| Tests gestionados (arnés propio) | `LUMINA_SKIP_NATIVE=1 dotnet run --project src/managed/Tests -f net8.0 -c Release` |
| Núcleo nativo x86/x64 (MSBuild, `/MT`) | `msbuild src/core/core.vcxproj /p:Configuration=Release /p:Platform=Win32\|x64` |
| Arnés portable del núcleo (g++ local) | `bash tests/core.Tests/build_and_run.sh` |
| Gate de calidad completo | `bash scripts/quality_gate.sh` |
| Empaquetado portable + SHA256SUMS | `bash scripts/package_portable.sh` |
| Instalador dual (Windows, Inno Setup) | `iscc installer/AppHibrida.iss` |

## Estado de la línea reestructurada (v1.0.0-beta.1)

- ✅ Reestructuración al mapa AGENT.md con historial preservado (`git mv`).
- ✅ Build local verde: net35+net48+net8.0 · tests gestionados 37/43 (los 6
  skips requieren la DLL nativa Windows — se verifican en CI).
- ✅ CI híbrido: auditoría de prohibiciones (F6.09), núcleo x86/x64, port
  Linux, capa gestionada con gate de exportadores (python-pptx + pypdf),
  interop PoC, empaquetado y release.
- 📄 Evidencias y limitaciones de entorno: `docs/verification/`.

## Licencia

Ver `LICENSE.md` (View-Only, Copyright (c) 2026 Isaac).
