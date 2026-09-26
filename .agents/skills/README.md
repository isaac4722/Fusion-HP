# Catálogo de Skills del Agente

Ubicación canónica: `.agents/skills/<skill-name>/SKILL.md` (norma de `AGENT.md`).
Procedencia registrada en `skills-lock.json` (instalación vía `npx skills add`).

## Instaladas (origen público verificado)

| Skill | Origen | Estado de uso (AGENT.md) |
|---|---|---|
| `cpp-core-guidelines` | `ariaszzzhc/cpp-core-guidelines` | 3_IMPLEMENT, 4_AUDIT — C++ de `src/core` |
| `cpp-core-guidelines-review` | `openharmonyinsight/openharmony-skills` | 4_AUDIT — revisión por secciones |
| `dotnet-accessibility` | `wshaddix/dotnet-skills` | 4_AUDIT — semántica, contraste, AutomationProperties |
| `dotnet-testing-strategy` | `wshaddix/dotnet-skills` | 4_AUDIT — pruebas de la capa C# |
| `dotnet-wpf-modern` | `wshaddix/dotnet-skills` | 2_PLAN/3_IMPLEMENT — patrones WPF/MVVM del Editor |
| `rtk-tdd` | `rtk-ai/rtk` | Transversal — reducir tokens de comandos ruidosos |

## Propias del repo (dominio Aplicación Híbrida)

Autoría local: los principios se derivan del documento técnico v1.1 (`spec/`)
y del Plan de Ultra Implementación; no hay origen público que instalar.

| Skill | Estados | Alcance normativo |
|---|---|---|
| `win7-compat` | 2_PLAN, 4_AUDIT | `_WIN32_WINNT=0x0601`, /MT, sin imports Win8+ [SPEC §3.1, §4] |
| `openxml-pptx` | 3_IMPLEMENT, 4_AUDIT | OPC, herencia 4 niveles, EMU, centésimas de punto, XXE [SPEC §9.2] |
| `zefania-xml` | 3_IMPLEMENT, 4_AUDIT | Biblias Zefania/.bib/JSON, indexación, búsqueda ≤200 ms [SPEC §9.1] |
| `directshow-media` | 3_IMPLEMENT, 4_AUDIT | Carga diferida estricta, fail-safe de video [SPEC §6.4, §6.5] |
| `ipc-bridge` | 3_IMPLEMENT, 4_AUDIT | `ipc.v1`, cola sin bloqueo, latencia ≤16 ms [SPEC §3.4] |
| `performance-budget` | 4_AUDIT | Tabla 10.1 (RAM, arranque, transición, búsquedas) [SPEC §10.1] |
| `win32-native-design` | 3_IMPLEMENT | UI nativa de emergencia, borderless, DPI awareness [SPEC §6.1] |
| `desktop-design` | 2_PLAN | Convenciones de escritorio (targets táctiles ≥32 px) — solo consulta |
| `ui-nice-skill` | 1_ANALYZE, 4_AUDIT | Auditar UI contra el sistema de diseño vigente (UiKit) |
| `humanizer` | 3_IMPLEMENT post-UI | Pulir todo texto visible (diálogos [SPEC §11.2]) |
| `vlm` | 3.5_VISUAL_GATE | Capturas Live/Editor/Multiview; descartar slop — no rediseñar |
| `taste-skill` | 4_AUDIT | Evaluación estética opcional contra "AI slop" |

## Ausencias registradas

- `caveman` y `cpp-coding-standards` sin origen público confiable al
  2026-09-24: se aplican sus principios manualmente (compresión de prosa solo
  conversacional; convenciones isocpp) y la ausencia queda registrada en
  `PROGRESS.md`. Ningún flujo se bloquea por ello (norma AGENT.md).
