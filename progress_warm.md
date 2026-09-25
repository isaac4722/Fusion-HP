# progress_warm.md — Bitácora WARM (últimos ~10 turnos cerrados; rotar desde PROGRESS.md)

(nueva línea reestructurada v1.0.0: sin turnos previos rotados aún — el historial
de la línea v7.x del prototipo anterior vive en el historial de commits y en
los mensajes de release del repo)
2026-09-24 · DONE · skills instaladas (6 reales + 12 propias, catálogo + lock) · build local · 7_PERSIST
2026-09-24 · DONE · reestructuración managed/ → src/managed + mapa core|data|services|state|features + StateProvider F5.01.2 · dotnet build net35+net48+net8 VERDE · 7_PERSIST
2026-09-24 · DONE · versión 1.0.0-beta.1 (version.props + Directory.Build.props) + spec/ + README + docs/agent · n/a · 7_PERSIST
2026-09-24 · DONE · investigación PLAN duckduckgo_search (8 consultas, 32 resultados únicos) → docs/research/plan-busquedas-ddg.md · n/a · 7_PERSIST
2026-09-24 · TODO: registro de la siguiente pieza (núcleo MSBuild src/core) — añadir aquí, nunca reescribir
2026-09-24 · DONE · core.vcxproj + RendererD2D (F0.06.1) + EnvironmentReport (F0.09) + arnés portable g++ · CORE TESTS PASS 13/13 · 7_PERSIST
2026-09-24 · DONE · F1.05 atajos JSON · F4.15 FidelityReport · F5.09 JsBudget · F6.04 DriveSyncEngine+SetSession · F6.08 driver sesión · TESTS PASS 42/48 (6 skips CI) · 7_PERSIST

2026-09-24 · DONE · installer/AppHibrida.iss dual per-user + package_portable.sh SHA256 12/12 + docs/verification + CI 11 jobs (core-msvc, core-portable, installer-windows) + AppHibrida.sln raíz · YAML válido · 7_PERSIST
2026-09-24 · DONE · quality_gate.sh local VERDE (auditoría F6.09 sin violaciones; saltos MSVC/clang-tidy documentados como degradación de entorno) · bash scripts/quality_gate.sh · 7_PERSIST
2026-09-24 · RUN  · campaña F6.08 sesión 60 min en tiempo real (fondo local) → docs/verification/sesion60.md al cierre · LUMINA_SESSION_MINUTES=60 · 8_CI
2026-09-24 · DONE · CI 7 rondas depuradas → VERDE (vcxproj SDK 10.0 + /utf-8, iscc [Code] // + [Components] sin unchecked + rutas absolutas + dist/installer, clrhost informativo no-bloqueante 0x80040154 imagen runner) · run 36037503568 · 7_PERSIST
2026-09-24 · DONE · F6.08: campaña completa al job sesion60 del CI (sandbox local mata procesos largos — documentado) + sesion60.md con criterio 11.4/11/10 · dispatch/tags · 7_PERSIST
2026-09-24 · DONE · F6.08 campaña completa 60 min EN CI: success (run 36040245651, 33k ops, 0 errores no justificados, RAM pico 52 MB) + evidencia en docs/verification/sesion60.md · artefacto sesion60 · 9_CLOSE
2026-09-24 · DONE · auditoría 80/80: F1.06 último proyecto · F1.07 búsqueda caliente (Ctrl+K, FtsQuery) · F2.09 ElementHost net48 · F2.12 uso/anotaciones · F2.14 recursos+relink · F4.13 ImageExporter · fix triggers FTS5 preexistente · tests 44/53 + g++ 15/15 · matriz en docs/verification · 10_AUDIT
2026-09-24 · DONE · experiencia PowerStudio (ref H-P-Web-Version-Ref): Inicio (mosaicos+continuar) + Estudio (cinta, miniaturas, lienzo 16:9 drag/resize, propiedades, notas) + Ctrl+N · beta.4 · 11_RELEASE
2026-09-25 · RUN  · F0-F6 reescritura completa v2 (núcleo C++ /MT + Studio net48 + Lite net35 + ipc.v1 + importadores Zefania/e-Sword Twofish/JSON/TSV/PPTX + exportadores PPTX/PDF/PNG + API 6 endpoints + OBS ws 5.x + Triggers + instalador dual + portable + CI 5 jobs + tests nativos/admin con fixtures reales) · 3_IMPLEMENT
2026-09-25 · DONE · F0-F6 CI VERDE run 36093930867 (núcleo x86+x64 + tests 24/24 · C# net35+net48 + tests 22/22 · gate prohibiciones · instalador dual + portable x2 + SHA256) · 9_CLOSE
2026-09-25 · RUN  · v2.1 fusión web+betas: assets GUI (7 fuentes woff2→ttf, 6 fondos JPG, 50 iconos Tabler trazo 1.7, logo Lumina) + 4 biblias completas empaquetadas (RV1960 31036 · NVI 31103 · RVG 31102 · RVR1909 31084, texto verbatim) + SongStore cancionero.fdb único con guardado atómico .tmp y dedupe título+artista · 3_IMPLEMENT
