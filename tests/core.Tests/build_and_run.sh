#!/usr/bin/env bash
# ============================================================================
#  build_and_run.sh — arnés portable del núcleo (tests/core.Tests)
#  Fusion-HP reestructuración v1.0.0-beta.1 · Task ID 4
#
#  Compila con g++ el SUBSET PORTABLE del núcleo (los .cpp que no requieren
#  Win32 — sus partes Win32 ya están tras #ifdef) + sqlite3 amalgamado + este
#  arnés, enlaza y ejecuta. Verde = "CORE TESTS PASS n/n" y exit 0.
#
#  Evidencia y limitación documentada: docs/verification/core-local.md
#  (los gates MSVC/Win32 — vcxproj D2D/Proyector/VideoDS/Win7 — corren en CI
#  windows-latest contra src/core/core.vcxproj).
# ============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
OUT="${SCRIPT_DIR}/build"
CXX="${CXX:-g++}"

STD="-std=c++17"
WARN="-Wall -Wextra -Wpedantic"
INC="-I${ROOT}/native/core/include -I${ROOT}/native/core/src -I${ROOT}/third_party/sqlite3 -I${ROOT}/third_party/nlohmann -I${ROOT}/src/core/src"
# LUMINA_CORE_STATIC: consumidores internos (lumina.h) sin dllexport.
DEFS="-DLUMINA_CORE_STATIC=1 -DSQLITE_ENABLE_FTS5=1 -DSQLITE_ENABLE_JSON1=1 -DSQLITE_THREADSAFE=1 -DSQLITE_OMIT_LOAD_EXTENSION=1"

mkdir -p "${OUT}"

echo "== g++ $( ${CXX} -dumpversion ) · subset portable del núcleo (F0/F1/F6 gates locales) =="
FILES=(
  # ipc.v1 (F0.05): códec puro + cola no bloqueante (servidor Win32 queda tras #ifdef)
  native/core/src/IpcV1.cpp
  # bootstrap (F0.01-F0.03): clasificación pura + sondas tras #ifdef _WIN32
  native/core/src/Bootstrap.cpp
  # nlog (F0.09): log estructurado §10.1 (portable)
  native/core/src/NativeLog.cpp
  # informe de entorno (F0.09/F6.01) — módulo nuevo de src/core/src
  src/core/src/EnvironmentReport.cpp
  # dominio portable: escritura, canciones, cifrado, resaltado
  native/core/src/Scripture.cpp
  native/core/src/BibleRef.cpp
  native/core/src/BibleBib.cpp
  native/core/src/Chords.cpp
  native/core/src/Lyrics.cpp
  native/core/src/SongModel.cpp
  native/core/src/Highlight.cpp
  # almacenamiento SQLite FTS5 + motor (Win32 tras LUMINA_HAS_WIN32)
  native/core/src/Storage.cpp
  native/core/src/Engine.cpp
)

echo "-- sqlite3 amalgamation (C compiler, aparte con -w: sus warnings no cuentan)"
CC_BIN="${CC:-gcc}"
if ! command -v "${CC_BIN}" >/dev/null 2>&1; then CC_BIN="${CXX}"; fi
# -x c fuerza el lenguaje C aunque el driver sea g++ (sqlite3.c no es C++).
"${CC_BIN}" -w -x c ${DEFS} -O2 -pthread -c "${ROOT}/third_party/sqlite3/sqlite3.c" -o "${OUT}/sqlite3.o"

OBJECTS=("${OUT}/sqlite3.o")
for src in "${FILES[@]}"; do
  obj="${OUT}/$(basename "${src}" .cpp).o"
  echo "-- ${WARN}  ${src}"
  ${CXX} ${STD} ${WARN} -pthread ${DEFS} ${INC} -c "${ROOT}/${src}" -o "${obj}"
  OBJECTS+=("${obj}")
done

echo "-- arnés: core_portable_tests.cpp"
${CXX} ${STD} ${WARN} -pthread ${DEFS} ${INC} -c "${SCRIPT_DIR}/core_portable_tests.cpp" -o "${OUT}/core_portable_tests.o"
OBJECTS+=("${OUT}/core_portable_tests.o")

echo "-- link (libpthread/dl del anfitrión, igual que el CMake heredado en no-Windows)"
${CXX} ${STD} -pthread "${OBJECTS[@]}" -o "${OUT}/core_portable_tests" -ldl

cd "${OUT}"
./core_portable_tests
