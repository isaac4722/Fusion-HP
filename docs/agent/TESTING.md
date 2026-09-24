# TESTING.md — Estrategia de testing

**Fuente:** plan de implementación §12 (unitarias/integración/seguridad/rendimiento)
y `AGENT.md` (arnés propio, no xUnit: salidas "TESTS PASS n/n").

## Arnés gestionado (`src/managed/Tests`, net8.0 + net48)

- Cada pieza: `Run("nombre", TestFn)` — el test debe FALLAR sin la implementación.
- Ejecución: `LUMINA_SKIP_NATIVE=1 dotnet run --project src/managed/Tests -f net8.0`.
- Gate externo de exportadores en CI: `tools/validate_pptx.py` (python-pptx +
  pypdf) valida que muestra.pptx/muestra.pdf sean archivos válidos de verdad.

## Arnés portable del núcleo (`tests/core.Tests`, g++)

- Compila el subset portable del núcleo (sin Win32) con una lista explícita de
  archivos — el CMake heredado no se toca.
- Cubre: framing `ipc.v1` (fragmentación, payload grande, versión desconocida),
  `ahp.v1`, bootstrap de clasificación (entradas sintéticas), log rotativo,
  parsers bíblicos, ZIP/OPC con miniz.

## Cobertura mínima (plan §12.1)

Bootstrap SO/arquitectura · clasificación A/B/C · IPC · ahp.v1 · IDs · herencia ·
syncMark · render · lazy loading · API/Bearer · Triggers · OBS · JSLib · cada
importador/exportador · informes de fidelidad · logs y rotación · recuperación.

## Seguridad (plan §12.3)

XXE · billion laughs · DTD · ZIP bombs · paths maliciosos · archivos corruptos ·
PPTM · token ausente/inválido · exceso de clientes · payload grande · sandbox
JSLib (disco/CPU/memoria) — cada una con test que demuestre la mitigación.

## Rendimiento (plan §12.4)

Métricas de la tabla 10.1 con mediciones REALES registradas en
`docs/verification/` — prohibido inventar cifras [AGENT.md].

## Plataformas

- Local (Linux): g++ + dotnet SDK — arnés portable + gestionado completo.
- CI windows-latest: MSVC x86/x64 + selftest nativo + vstest + gates de
  bitness del PE (las 6 pruebas de la DLL nativa que saltan en local).
