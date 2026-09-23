# AGENT.md · Contrato de trabajo (LuminaPresentation Suite)

Aplica a TODO agente (IA o humano automatizado). Sin excepciones.

## Stack

Núcleo **C++17** (`native/`, Win32+GDI, SQLite+FTS5 embebido, /MT) +
capa gestionada **C#** (`managed/`, **WPF net48** = interfaz principal v5.4.0
«ESTUDIO» + **WinForms net35;net48** = línea base Win7 SP1; Core añade net8.0
solo para el arnés de tests). ABI C plana (`lumina.h`).
Versión actual: **6.1.0 «GUION» BETA** (ver `README.md`). Todo lo publicado
es BETA (pre-release) — nunca «release» final.

## Ciclo obligatorio — 9 pasos

Cada encargo se ejecuta en este orden. Si el paso 5 falla, vuelve a 1→3→4→5
hasta pasarlo.

| # | Paso | Salida mínima |
|---|------|---------------|
| 1 | Analiza el encargo | objetivo, módulos tocados, reglas que aplican |
| 2 | Consulta la web / docs del repo | mejores prácticas vigentes + `docs/` |
| 3 | Aplica lo pedido | 1 pieza por commit, sin features no pedidas |
| 4 | Audita | diff vs encargo + gates del paso 5 |
| 5 | Control | pasa → paso 6; falla → volver a 1 |
| 6 | Documenta | entrada en `progress.md` + `docs/` si cambió comportamiento |
| 7 | Persiste | conventional commits + push a `main` |
| 8 | CI verde | sigue el run de Actions; corrige hasta verde |
| 9 | Cierra | repo sincronizado + estado real registrado |

El paso 2 es opcional en reintentos cuando ya se consultó en el ciclo original.

## Comandos

| Tarea | Comando |
|-------|---------|
| Núcleo nativo (Linux) | `cmake -S . -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build` |
| Gates nativos | `./build/native/lumina_selftest && ./build/native/lumina_poc_native` |
| Capa gestionada | `dotnet build managed/Lumina.sln -c Release` (incluye `managed/Lumina.WPF`) |
| Suite de tests | `dotnet run --project managed/Tests -f net8.0` (con `libLuminaCore.so` junto al exe) |
| Muestras + gate exportadores | `LUMINA_EXPORT_SAMPLES=/tmp/s LUMINA_SKIP_NATIVE=1 dotnet run --project managed/Tests -f net8.0` → `python3 tools/validate_pptx.py /tmp/s/muestra.pptx /tmp/s/muestra.pdf` |
| Interop | `dotnet build managed/PoC/PoC.Managed -f net48 -c Release` (gate: «POC PASS 12/12») |

## Reglas inquebrantables

- Lee `AGENT.md` y `progress.md` completos antes de empezar tu turno.
- Cada turno AÑADE (nunca reescribe) tu entrada en `progress.md` con la plantilla de abajo.
- `dotnet build managed/Lumina.sln` **0 errores/0 warnings** (net35+net48+net8),
  `lumina_selftest` y `lumina_poc_native` en verde, y `Lumina.Tests`
  **n/n** son obligatorios ANTES de cada commit. Prohibido debilitar tests o
  relajar reglas para ponerse verde.
- La ABI del núcleo (`lumina.h`) es contractual: cambios solo aditivos y
  documentando la evolución (`structSize`/versión).
- Prohibido en los binarios distribuidos: dependencias NuGet funcionales,
  JVM/Electron, ediciones de registro del usuario, operaciones manuales de
  administrador. El paquete portable debe abrirse en Win7 SP1 x32 → Win11 x64
  sin instalar nada (verificar con `tools/verify_portable.py`).
- Idioma: español (es-VE) en UI, docs y commits (conventional commits).
- Si te bloqueas >2 intentos en algo: regístralo en `progress.md §Bloqueos`
  y continúa con otra pieza. No inventes soluciones mudas.
- Cambios de arquitectura solo evolucionando lo existente — nunca reemplazo
  (ver `docs/architecture-hybrid.md`).

## Definición de hecho (por pieza)

Código + test que lo cubre + `build/selftest/tests` verdes + entrada en
`progress.md` + `docs/` actualizado si cambió comportamiento + run de Actions
verde (paso 8).

## Plantilla `progress.md`

```
---
## [TASK-ID] <título> · <fecha UTC>
- Agente: <nombre/versión>
- Hecho: <lista concreta>
- Decisiones: <qué decidiste y por qué>
- Gates: build=0 err/0 warn · selftest=N/N · poc=N/N · tests=N/N
- Bloqueos: <ninguno | descripción>
- Siguiente: <qué toca ahora al flujo principal>
```

## Referencias externas

| Necesidad | Archivo |
|-----------|---------|
| Arquitectura híbrida y ABI | `docs/architecture-hybrid.md` |
| Exportación PPTX/PDF | `docs/export.md` |
| OBS/MIDI/mando remoto | `docs/integrations.md` |
| Activadores | `docs/triggers.md` |
| Formato .BIB | `docs/bib-format.md` |
| Diferido y evolución | `docs/roadmap.md` |
| Bitácora de turnos | `progress.md` |
| Política de seguridad | `SECURITY.md` |
