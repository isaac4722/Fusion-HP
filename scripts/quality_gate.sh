#!/usr/bin/env bash
# ============================================================================
#  quality_gate.sh — Gate de calidad [AGENT.md]: auditoría de prohibiciones
#  [SPEC §3.5, §11.4]. En runners Linux (CI) se ejecuta completo; en equipos
#  sin herramientas informa y degrada sin fallo silencioso.
#  1) Cero Java/JRE en código
#  2) Cero .NET Core/.NET 5+ como runtime obligatorio (archivos de proyecto)
#  3) Cero escritura al Registro de Windows
#  4) Cero regedit/líneas de comando/permisos elevados exigidos al usuario
#  5) Cero dependencias NuGet de runtime (solo herramientas de build)
# ============================================================================
set -u
cd "$(dirname "$0")/.."
FAIL=0
VIOL=0

STEP() { printf '\n== %s ==\n' "$1"; }
OK()   { printf '[OK]   %s\n' "$1"; }
BAD()  { printf '[FALLO] %s\n' "$1"; FAIL=1; VIOL=$((VIOL+1)); }
SKIP() { printf '[SALTA] %s\n' "$1"; }

HAS_MATCH() { # patrón, archivos… — devuelve coincidencias reales (excluye docs de la propia prohibición)
  # v4.1.0: excluye third_party (cabeceras vendorizadas — WIL/spdlog/sqlite/
  # doctest citan APIs prohibidas en sus propias capas internas; el producto
  # no las usa y cada librería entra auditada por hash en DEPENDENCIAS.md)
  local pat="$1"; shift
  grep -rniE --exclude-dir=third_party "$pat" "$@" 2>/dev/null \
    | grep -v "audit-allow" \
    | grep -viE "prohibid|cero |nunca|jamás|sin jvm|sin java|no usa|no se usa|never |removida|retirad|descargado|comentari" \
    || true
}

STEP "1) Cero Java/JRE [SPEC §3.5]"
if [ -n "$(HAS_MATCH 'java\.|javax\.|jvm\b|/jre\b' src/ --include='*.cs' --include='*.cpp' --include='*.h')" ]; then
  BAD "dependencia Java detectada"
else
  OK "sin Java"
fi

STEP "2) Sin .NET Core/.NET 5+ obligatorio [SPEC §3.5]"
BADNET=$(grep -rE 'TargetFramework>net[5-9]|TargetFramework>netcoreapp' src/ tests/ --include='*.csproj' 2>/dev/null || true)
if [ -n "$BADNET" ]; then BAD "TFM moderno como objetivo: $BADNET"; else OK "solo net35/net48"; fi

STEP "3) Sin escritura al Registro [SPEC §11.4]"
if [ -n "$(HAS_MATCH 'Registry(Key)?\.(SetValue|CreateSubKey|DeleteValue|DeleteSubKey)' src/ --include='*.cs')" ]; then
  BAD "escritura al Registro en capa C#"
elif [ -n "$(HAS_MATCH 'RegSetValueEx|RegCreateKeyEx' src/core/)" ]; then
  BAD "escritura al Registro en núcleo C++"
else
  OK "sin escritura al Registro"
fi

STEP "4) Sin regedit/consola/elevación para el usuario [SPEC §11.4]"
if [ -n "$(HAS_MATCH 'regedit|runas|runasas|ShellExecute.*(cmd\.exe|powershell)' src/ --include='*.cs' --include='*.cpp' --include='*.h')" ]; then
  BAD "invocación de herramientas del sistema detectada"
else
  OK "nada que exigir regedit/consola/elevación"
fi

STEP "5) Dependencias NuGet solo de build [SPEC §10.2]"
BADPKG=$(grep -rh 'PackageReference Include' src/ tests/ --include='*.csproj' 2>/dev/null \
  | grep -v 'Microsoft.NETFramework.ReferenceAssemblies' || true)
if [ -n "$BADPKG" ]; then BAD "paquete de runtime: $BADPKG"; else OK "sin paquetes de runtime"; fi

STEP "6) Manifest sin elevación"
if grep -q 'requestedExecutionLevel level="requireAdministrator"' -r src/ installer/ 2>/dev/null; then
  BAD "el manifiesto exige administrador"
else
  OK "asInvoker"
fi

printf '\n'
if [ "$FAIL" -eq 0 ]; then
  printf 'GATE DE CALIDAD: VERDE (0 violaciones)\n'
  exit 0
else
  printf 'GATE DE CALIDAD: ROJO (%d violaciones)\n' "$VIOL"
  exit 1
fi
