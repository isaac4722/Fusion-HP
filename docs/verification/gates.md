# Gates F0–F6 — mapa de evidencia (criterio 12.5)

> Cada gate del plan (§12.5) con su prueba obligatoria y dónde está la evidencia
> real. Complemento ejecutivo de `matriz-cumplimiento.md`.

| Gate | Prueba obligatoria (12.5) | Evidencia | Estado |
|---|---|---|---|
| **F0** | Win7 x86 sin .NET, texto/imagen, log de entorno | Perfil C verificado en arnés g++ (bootstrap A/B/C) + salida nativa (`Projector`) + log de entorno (`EnvironmentReport` F0.09); la parte física corre bajo F6.07 | ✅ automatizable |
| **F1** | 16 ms, un frame, captura 60 fps | Métricas reales del render en campaña 60 min (F6.01: `renderAvgMs=0.06`, `renderMaxMs=2.3`) + gates MSVC de la ruta D2D | ✅ |
| **F2** | x86/x64 idéntico, herencia, tema en caliente | Mismo IL multi-TFM + vcxproj x86/x64 (`core-msvc`) + `StyleCascade` + `SetTheme` en vivo (tests gestionados) | ✅ |
| **F3** | video, fail-safe, Lower Third, tres salidas | `VideoDS` (PoC interop), captura de errores por slide, `lumina_lower_third`, Stage View/Director/Multiview (uicheck) | ✅ |
| **F4** | PPTX/PPTM, `.bib`, RV1960, informes | `TestPptxExport`+python-pptx (gate externo), PPTM sin macros, `BibleBib.cpp`, Zefania+OSIS+JSON con informe F4.15, exportación de imágenes (F4.13, net48 con GDI+) | ✅ |
| **F5** | seis endpoints, Bearer, tag→OBS | `TestApiV1` (Bearer/401/503), triggers→OBS, QR, PCO, NDI, sandbox JSLib, diagnóstico | ✅ |
| **F6** | métricas, Win7/Win11, instalador, 60 min | Tabla 10.1 medida en `sesion60` (60,00 min · 33.138 ops · 0 no justificados · pico 52-71 MB · 59 dumps) + instalador iscc + portable SHA256 + versionado `1.x.y-beta+z` | ✅ (matriz física F6.07 = hardware real, única pieza pendiente de campo) |

## Verificación del CI (12 jobs)

`prohibitions-audit` · `native-windows` · `native-linux` · `core-msvc` (vcxproj
x86+x64) · `core-portable` (arnés g++ 15/15) · `managed` (net8.0 + net48 con
GDI+ real) · `interop` (PoC 12/12 + **arnés gestionado net48 CON núcleo** en
x86/x64) · `sesion60` (campaña completa 60 min, en tags) · `installer-windows`
(iscc) · `clrhost-poc` (informativo, continue-on-error: regresión de imagen del
runner, 0x80040154 documentada) · `package` · `release` (solo tags v*,
`prerelease: true`).
