# Verificación local del núcleo — arnés portable g++

**Fecha:** 2026-09-24 · **Entorno:** contenedor Linux (Debian trixie),
g++ (Debian 14.2.0) · **Fixture gates:** F0.05 (ipc.v1), F0.09 (log),
F0.03/F0.04 (perfil vía camino puro), F1 (dominio), F2.13 (Storage FTS5),
F6.01 (informe de entorno).

## Comando y resultado

```text
$ bash tests/core.Tests/build_and_run.sh
== g++ 14.2 · subset portable del núcleo (F0/F1/F6 gates locales) ==
...
 [ OK ] ipc.v1: encode/decode roundtrip keeps magic+version+type
 [ OK ] ipc.v1: byte-by-byte fragmentation reassembles two frames
 [ OK ] ipc.v1: protocol errors (bad magic / unknown version / oversized) never crash
 [ OK ] bootstrap: A/B/C classification over injected NDP facts (461808 -> A, 35sp1 -> B, none -> C)
 [ OK ] nlog F0.09: daily file, severities, ts fields, redaction, stats
 [ OK ] ahp.v1: minimal JSON parse builds slides (SongModel)
 [ OK ] scripture: Jn 3:16 resolves (book 43) and builds ref-labeled slides
 [ OK ] highlight: accent/case-insensitive with word boundaries
 [ OK ] chords: transpose latin/anglo lines and chord-line detection
 [ OK ] storage: SQLite FTS5 insert/select/MATCH with bound params
 [ OK ] environment F0.09/F6.01: ToJson carries osLabel/profile/bootDecision
CORE TESTS PASS 13/13
```

**Veredicto: VERDE 13/13** (arnés propio del repo, no xUnit).

## Alcance compilado (subset portable)

`IpcV1.cpp` (códec+cola, transporte Win32 tras `#ifdef`), `Bootstrap.cpp`
(clasificación pura A/B/C), `NativeLog.cpp` (§10.1/§10.3/§10.4),
`EnvironmentReport.cpp` (nuevo, camino puro con hechos inyectados),
`Scripture.cpp`, `BibleRef.cpp`, `BibleBib.cpp`, `Chords.cpp`,
`Lyrics.cpp`, `SongModel.cpp`, `Highlight.cpp`, `Storage.cpp` (SQLite FTS5),
`Engine.cpp` (win32 tras `LUMINA_HAS_WIN32`) + sqlite3 amalgamation (gcc, C).

## Correcciones aplicadas durante la verificación

1. `sqlite3.c` se compila con `gcc -x c` (no g++): el amalgamado es C y g++
   rechazaba conversiones válidas en C (`-fpermissive` no es la vía).
2. `core_portable_tests.cpp`: `Log::Options` (anidado), y el contrato real
   del FrameDecoder en fragmentación byte-a-byte (`consumed` = frame completo,
   coherente con el roundtrip de frame entero).
3. `EnvironmentReport.cpp` (Linux): con `probeJson` no se pisa `netVersion`
   ni `profile` — es el camino puro de hechos inyectados, mismo contrato que
   las sondas Win32 (una sola política A/B/C, F0.03/F0.04).

## Limitación documentada (degradación controlada, sin fallo silencioso)

Este anfitrión no tiene MSVC ni Windows SDK: los gates Win32 del núcleo
(`core.vcxproj`: Renderer GDI+/D2D, Projector borderless, VideoDS DirectShow,
Win7Compat/MASM, selftest completo) se ejecutan en el CI `windows-latest`
(job `native-windows` heredado + job MSVC del árbol reestructurado).
El arnés local cubre la totalidad de la lógica portable; la cobertura Win32
queda evidenciada por el CI (runs de GitHub Actions en el PR y el tag).
