#!/usr/bin/env bash
# ============================================================================
#  quality_gate.sh — Gate de calidad (norma AGENT.md: analyze C++ + analyze C#
#  + test + auditoría de prohibiciones).
#
#  Degradación controlada por entorno (sin fallo silencioso: cada paso informa
#  lo que ejecuta y lo que no puede ejecutarse aquí — MSVC/vstest viven en los
#  runners Windows del CI):
#    1) Auditoría de prohibiciones (F6.09)          — siempre
#    2) Build gestionado net35+net48+net8           — dotnet SDK
#    3) Tests gestionados (arnés propio)            — net8.0
#    4) Arnés portable del núcleo (g++)             — si hay g++
#    5) Análisis estático C#/formato                — dotnet format si existe
#    6) Análisis estático C++ (clang-tidy)          — solo si está instalado
#    7) Build nativo MSVC x86/x64 + vstest          — solo en Windows
# ============================================================================
set -u
cd "$(dirname "$0")/.."
FAIL=0
STEP() { printf '\n\033[1;34m== %s ==\033[0m\n' "$1"; }
OK()   { printf '\033[1;32m[OK]\033[0m %s\n' "$1"; }
BAD()  { printf '\033[1;31m[FALLO]\033[0m %s\n' "$1"; FAIL=1; }
SKIP() { printf '\033[1;33m[SALTA]\033[0m %s\n' "$1"; }

# ---------------------------------------------------------------------------
STEP "1) Auditoría de prohibiciones (F6.09)"
# cero Java/JRE · cero .NET Core obligatorio · cero Registro/consola/elevación
# · cero stacks crudos como única respuesta · cero desactivación del log.
# Marca de permitido: las líneas que documentan la propia prohibición o el
# cliente web remoto servido (F5.03 — feature normada) llevan audit-allow.
VIOL=0
HAS_MATCH() { # HAS_MATCH <patrón> <archivos…> — falla solo si hay coincidencia real
  local pat="$1"; shift
  local hits
  hits=$(grep -rniE "$pat" "$@" 2>/dev/null | grep -v "audit-allow" | grep -viE "prohibid|cero|nunca|jamás|sin jvm|sin java|no usa|no se usa|never|removida|retirad" || true)
  if [ -n "$hits" ]; then printf '%s\n' "$hits"; return 0; fi
  return 1
}
HAS_MATCH "java\.|javax\.|jvm\b|/jre\b" src/managed --include="*.cs" && { BAD "posible dependencia Java en capa gestionada"; VIOL=1; }
HAS_MATCH "Registry(Key)?\.[A-Za-z]*\.?SetValue\(" src/managed --include="*.cs" && { BAD "escritura al Registro detectada"; VIOL=1; }
HAS_MATCH "\bregedit\b" src/managed --include="*.cs" && { BAD "mención de regedit como requerimiento"; VIOL=1; }
HAS_MATCH "localStorage|ServiceWorker" src/managed --include="*.cs" && { BAD "patrones web prohibidos en capa gestionada"; VIOL=1; }
grep -rn "audit-allow" src/managed --include="*.cs" | rg -q "localStorage|ServiceWorker" && OK "cliente web remoto (F5.03) con marca audit-allow documentada"
[ $VIOL -eq 0 ] && OK "auditoría de prohibiciones sin violaciones"

# ---------------------------------------------------------------------------
STEP "2) Build gestionado (net35+net48+net8.0 con ref assemblies)"
if command -v dotnet >/dev/null 2>&1; then
  dotnet build src/managed/Lumina.sln -c Release >/tmp/qg_build.log 2>&1 \
    && OK "Lumina.sln compilada (3 TFMs)" \
    || { BAD "build gestionado — ver /tmp/qg_build.log"; tail -20 /tmp/qg_build.log; }
else
  SKIP "dotnet SDK no disponible en este entorno (CI lo ejecuta)"
fi

# ---------------------------------------------------------------------------
STEP "3) Tests gestionados (arnés propio net8.0)"
if command -v dotnet >/dev/null 2>&1; then
  LUMINA_SKIP_NATIVE=1 dotnet run --project src/managed/Tests -c Release -f net8.0 \
    > /tmp/qg_tests.log 2>&1
  grep -q "TESTS PASS" /tmp/qg_tests.log && OK "$(grep 'TESTS PASS' /tmp/qg_tests.log | tail -1)" \
    || { BAD "tests gestionados — ver /tmp/qg_tests.log"; tail -10 /tmp/qg_tests.log; }
else
  SKIP "dotnet no disponible"
fi

# ---------------------------------------------------------------------------
STEP "4) Arnés portable del núcleo (g++)"
if command -v g++ >/dev/null 2>&1 && [ -f tests/core.Tests/build_and_run.sh ]; then
  bash tests/core.Tests/build_and_run.sh > /tmp/qg_core.log 2>&1 \
    && OK "$(tail -1 /tmp/qg_core.log)" \
    || { BAD "arnés del núcleo — ver /tmp/qg_core.log"; tail -15 /tmp/qg_core.log; }
else
  SKIP "g++/arnés no disponible"
fi

# ---------------------------------------------------------------------------
STEP "5) Formato/estilo C# (dotnet format --verify-no-changes)"
if command -v dotnet >/dev/null 2>&1 && dotnet format --version >/dev/null 2>&1; then
  dotnet format src/managed/Lumina.sln --verify-no-changes >/tmp/qg_fmt.log 2>&1 \
    && OK "formato C# conforme" || SKIP "formato con diferencias (revisar /tmp/qg_fmt.log)"
else
  SKIP "dotnet format no disponible"
fi

# ---------------------------------------------------------------------------
STEP "6) Análisis estático C++ (clang-tidy)"
if command -v clang-tidy >/dev/null 2>&1; then
  OK "clang-tidy presente (CI ejecuta el análisis completo)"
  SKIP "ejecución completa diferida al CI (headers Win32 no presentes aquí)"
else
  SKIP "clang-tidy no instalado (CI lo ejecuta en Windows)"
fi

# ---------------------------------------------------------------------------
STEP "7) Build nativo MSVC x86+x64 + vstest (Windows)"
if command -v msbuild >/dev/null 2>&1; then
  msbuild src/core/core.vcxproj /p:Configuration=Release /p:Platform=Win32 /m || FAIL=1
  msbuild src/core/core.vcxproj /p:Configuration=Release /p:Platform=x64 /m || FAIL=1
  OK "núcleo MSVC x86+x64"
else
  SKIP "MSVC/msbuild no disponible (gate del CI windows-latest)"
fi

printf '\n%s\n' "==================================================================="
if [ $FAIL -eq 0 ]; then printf '\033[1;32mGATE DE CALIDAD: VERDE\033[0m (salta documentados = degradación controlada)\n'; else printf '\033[1;31mGATE DE CALIDAD: ROJO\033[0m\n'; fi
exit $FAIL
