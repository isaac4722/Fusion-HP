#!/usr/bin/env bash
# ============================================================================
#  package_portable.sh — Portable packaging for LuminaPresentation Suite
#  (Fixture F6.06.6, reestructuracion v1.0.0-beta.1)
# ----------------------------------------------------------------------------
#  Builds the portable layout  dist/portable/{x86,x64}/  and one ZIP per
#  architecture (dist/LuminaPresentation-<ver>-portable-<arch>.zip), plus a
#  per-variant SHA256SUMS file verifiable with `sha256sum -c`.
#
#  Layout per variant (flat, F6.06 contract):
#    LuminaPresentation.exe       (net48 WPF shell)          [+ .exe.config]
#    LuminaPresentation35.exe     (net35 WinForms baseline)  [+ .exe.config]
#    Lumina.Core.dll / Lumina.Api.dll / Lumina.Bridge.dll   (managed, net35)
#    Lumina.WPF.dll               (optional: current build merges the WPF
#                                  shell INTO LuminaPresentation.exe — v5.4.0)
#    Lumina.PocFacade.dll         (optional: PoC artifact, only via --staging)
#    LuminaCore.dll               (NATIVE C++ core, per architecture)
#    LuminaLauncher.exe           (optional native launcher)
#    app.net35.config / app.net48.config (optional template configs)
#    data/ (empty) + data/sample/ (factory resources, when present)
#    README.txt (es-VE) / LICENSE.md / SHA256SUMS
#
#  Sources of the binaries (in order):
#    1. --staging <dir>   : CI-assembled layout with <dir>/{x86,x64}/
#    2. auto-detect       : src/managed/*/bin/Release/** + native build
#                           outputs present locally.
#  NOTHING is silently dropped: every expected file is reported as PACKED,
#  MISSING (native -> the producing CI command is printed) or SKIPPED
#  (deliberate, with the reason). Exit 1 ONLY when there is nothing at all
#  to package.  --allow-missing-native acknowledges a native-less rehearsal
#  (managed-only ZIP) without the loud "INCOMPLETE" verdict banner.
#
#  NOTE: *.pdb are deliberately excluded from the payload (report shows it).
#
#  Usage:
#    scripts/package_portable.sh [--staging DIR] [--version X.Y.Z]
#                                [--out DIR] [--allow-missing-native] [--help]
# ============================================================================
set -u
cd "$(dirname "$0")/.."
REPO_ROOT="$PWD"

# ---------------------------------------------------------------------------
# Pretty output helpers (Spanish es-VE: operator-facing evidence lines)
# ---------------------------------------------------------------------------
if [ -t 1 ]; then
  C_H="\033[1;36m"; C_OK="\033[1;32m"; C_W="\033[1;33m"; C_B="\033[1;31m"; C_D="\033[2m"; C_0="\033[0m"
else
  C_H=""; C_OK=""; C_W=""; C_B=""; C_D=""; C_0=""
fi
HDR()   { printf "\n${C_H}== %s ==${C_0}\n" "$1"; }
OK()    { printf "${C_OK}[OK]${C_0} %s\n" "$1"; }
WARN()  { printf "${C_W}[AVISO]${C_0} %s\n" "$1"; }
BAD()   { printf "${C_B}[FALTA]${C_0} %s\n" "$1"; }
NOTE()  { printf "${C_D}[info]${C_0} %s\n" "$1"; }

usage() {
  sed -n '2,40p' "$0" | sed 's/^# \{0,1\}//'
}

# ---------------------------------------------------------------------------
# Defaults
# ---------------------------------------------------------------------------
STAGING=""                 # --staging DIR (CI layout with x86/ and/or x64/)
VERSION=""                 # --version overrides version.props
OUT_DIR="dist"             # --out DIR
ALLOW_MISSING_NATIVE=0
ARCHS="x86 x64"

while [ $# -gt 0 ]; do
  case "$1" in
    --staging)              STAGING="${2:-}"; shift 2 ;;
    --version)              VERSION="${2:-}"; shift 2 ;;
    --out)                  OUT_DIR="${2:-}"; shift 2 ;;
    --allow-missing-native) ALLOW_MISSING_NATIVE=1; shift ;;
    -h|--help)              usage; exit 0 ;;
    *) printf "Argumento desconocido: %s (use --help)\n" "$1" >&2; exit 2 ;;
  esac
done

# ---------------------------------------------------------------------------
# Version: --version > version.props (AppVersionFull)
# ---------------------------------------------------------------------------
if [ -z "$VERSION" ]; then
  if [ -f version.props ]; then
    VERSION="$(sed -n 's/.*<AppVersionFull>\(.*\)<\/AppVersionFull>.*/\1/p' version.props | head -1 | tr -d '[:space:]')"
  fi
  [ -z "$VERSION" ] && VERSION="1.0.0-beta.1" && \
    NOTE "version.props sin AppVersionFull — usando valor por defecto $VERSION"
fi
printf "${C_H}== Empaquetado portable LuminaPresentation Suite v%s ==${C_0}\n" "$VERSION"
[ -n "$STAGING" ] && NOTE "modo STAGING: $STAGING" || NOTE "modo AUTO-DETECCIÓN (builds locales)"

# ---------------------------------------------------------------------------
# Pick first existing file from a candidate list (auto-detect precedence)
# ---------------------------------------------------------------------------
find_first() {
  local f
  for f in "$@"; do
    if [ -s "$f" ]; then printf '%s' "$f"; return 0; fi
  done
  return 1
}

# Managed net35 DLLs: the flat layout ships the net35 builds (same public API,
# no #if LUMINA_NET35 differences in public surface; a CLR2 assembly loads on
# CLR4 thanks to .NET 4 in-place compatibility, and is the ONLY choice that
# also boots on a Win7 SP1 machine with just .NET 3.5).
core_dll()    { find_first "${STAGING:+$STAGING/$1/Lumina.Core.dll}" \
  src/managed/Lumina.Core/bin/Release/net35/Lumina.Core.dll \
  src/managed/Lumina.Core/bin/Release/net48/Lumina.Core.dll ; }
api_dll()     { find_first "${STAGING:+$STAGING/$1/Lumina.Api.dll}" \
  src/managed/Lumina.Api/bin/Release/net35/Lumina.Api.dll \
  src/managed/Lumina.Api/bin/Release/net48/Lumina.Api.dll ; }
bridge_dll()  { find_first "${STAGING:+$STAGING/$1/Lumina.Bridge.dll}" \
  src/managed/Lumina.Bridge/bin/Release/net35/Lumina.Bridge.dll \
  src/managed/Lumina.Bridge/bin/Release/net48/Lumina.Bridge.dll ; }

# Flagship WPF exe (net48): Lumina.WPF output wins over the superseded
# Lumina.UI net48 WinForms build (csproj comment v5.4.0 «ESTUDIO»).
wpf_exe()     { find_first "${STAGING:+$STAGING/$1/LuminaPresentation.exe}" \
  src/managed/Lumina.WPF/bin/Release/net48/LuminaPresentation.exe \
  src/managed/Lumina.UI/bin/Release/net48/LuminaPresentation.exe ; }
wpf_cfg()     { find_first "${STAGING:+$STAGING/$1/LuminaPresentation.exe.config}" \
  src/managed/Lumina.WPF/bin/Release/net48/LuminaPresentation.exe.config \
  src/managed/Lumina.UI/bin/Release/net48/LuminaPresentation.exe.config ; }
win35_exe()   { find_first "${STAGING:+$STAGING/$1/LuminaPresentation35.exe}" \
  src/managed/Lumina.UI/bin/Release/net35/LuminaPresentation35.exe ; }
win35_cfg()   { find_first "${STAGING:+$STAGING/$1/LuminaPresentation35.exe.config}" \
  src/managed/Lumina.UI/bin/Release/net35/LuminaPresentation35.exe.config ; }

cfg_net48()   { find_first "${STAGING:+$STAGING/$1/app.net48.config}" \
  src/managed/Lumina.WPF/app.net48.config \
  src/managed/Lumina.UI/app.net48.config ; }
cfg_net35()   { find_first "${STAGING:+$STAGING/$1/app.net35.config}" \
  src/managed/Lumina.UI/app.net35.config ; }

poc_dll()     { find_first "${STAGING:+$STAGING/$1/Lumina.PocFacade.dll}" ; }   # staging-only by design
wpf_dll()     { find_first "${STAGING:+$STAGING/$1/Lumina.WPF.dll}" ; }         # only if a build splits it
launcher()    { find_first "${STAGING:+$STAGING/$1/LuminaLauncher.exe}" \
  native/launcher/build/Release/LuminaLauncher.exe blaunch/Release/LuminaLauncher.exe ; }

# NATIVE core per architecture — CI (job native-windows) produces it with:
#   cmake -S . -B build -A Win32 -DCMAKE_BUILD_TYPE=Release   (x86)
#   cmake -S . -B build -A x64   -DCMAKE_BUILD_TYPE=Release   (x64)
#   cmake --build build --config Release
#   -> build\native\Release\LuminaCore.dll
native_dll() {
  case "$1" in
    x86) find_first "${STAGING:+$STAGING/x86/LuminaCore.dll}" \
        build/native/Release/LuminaCore.dll \
        dist/x86/LuminaCore.dll \
        artifacts/native-x86/LuminaCore.dll \
        build-x86/native/Release/LuminaCore.dll ;;
    x64) find_first "${STAGING:+$STAGING/x64/LuminaCore.dll}" \
        build/native/Release/LuminaCore.dll \
        dist/x64/LuminaCore.dll \
        artifacts/native-x64/LuminaCore.dll \
        build-x64/native/Release/LuminaCore.dll ;;
  esac
}

# ---------------------------------------------------------------------------
# Per-variant state
# ---------------------------------------------------------------------------
ANY_PACKED=0                  # becomes 1 when at least one file is packaged
NATIVE_WARNED=0

copy_in() {  # copy_in <src> <dst-rel>  — returns 0 and prints src when copied
  local src="$1" dst="$2"
  if [ -n "$src" ] && [ -s "$src" ]; then
    cp -f "$src" "$dst"; printf '%s' "$src"; return 0
  fi
  return 1
}

report_native_gap() {
  # Native core missing: NEVER silent — print exactly how CI produces it.
  if [ "$ALLOW_MISSING_NATIVE" -eq 1 ]; then
    NOTE "LuminaCore.dll ($1) ausente — reconocido con --allow-missing-native (empaquetado gestionado-only)"
  else
    BAD "LuminaCore.dll ($1) AUSENTE — núcleo nativo C++ no compilado en este entorno."
    printf '       Se empaqueta la parte gestionada con aviso (el paquete queda INCOMPLETO).\n'
    printf '       El CI lo produce así (.github/workflows/ci.yml — job native-windows):\n'
    if [ "$1" = "x86" ]; then
      printf '         cmake -S . -B build -A Win32 -DCMAKE_BUILD_TYPE=Release\n'
    else
      printf '         cmake -S . -B build -A x64   -DCMAKE_BUILD_TYPE=Release\n'
    fi
    printf '         cmake --build build --config Release\n'
    printf '         #  ->  build\\native\\Release\\LuminaCore.dll\n'
  fi
}

make_readme() {  # make_readme <variant-dir> <arch> <has_native 0|1>
  local dir="$1" arch="$2" nat="$3"
  cat > "$dir/README.txt" <<EOF
LuminaPresentation Suite v${VERSION} — PAQUETE PORTABLE (${arch})
================================================================

QUÉ ES
  Suite de presentación litúrgica y multimedia para Windows 7 SP1 x86 a
  Windows 11 x64, en su versión ${VERSION}. Este ZIP corre SIN instalación:
  descomprímalo en una carpeta (p. ej. una memoria USB) y ejecútelo desde allí.

CÓMO ARRANCAR (PORTABLE)
  * Equipo Windows 10/11 (o Win7/8 con .NET 4.8):  LuminaPresentation.exe
    (interfaz WPF moderna, .NET Framework 4.8).
  * Equipo Win7 SP1 solo con .NET 3.5 SP1:         LuminaPresentation35.exe
    (baseline WinForms; su configuración acepta CLR 2.0 y CLR 4.0).
  * Si su paquete incluye LuminaLauncher.exe, ese ejecutable nativo detecta
    el runtime y la arquitectura y lanza la variante adecuada por usted.

CONFIGURACIÓN Y DATOS (MODO PORTABLE)
  * Toda la configuración, los proyectos y los logs se guardan DENTRO de esta
    carpeta (config/ y logs/ se crean junto al programa). El modo portable no
    escribe fuera de su carpeta en operación normal.
  * No se usa el Registro de Windows — ni para configuración, ni para nada.
  * La carpeta data/ contiene los recursos de fábrica (data/sample/ trae una
    muestra de biblia .bib). Las biblias y proyectos que importe también
    quedan aquí.

REQUISITOS
  * Windows 7 SP1 x86 o superior, 32 o 64 bits según la variante de este
    paquete (${arch}).
  * .NET Framework 4.8 para la interfaz WPF (integrado en Win10 1903+/Win11;
    en Win7/8 instale el paquete oficial offline de Microsoft si lo necesita:
    https://go.microsoft.com/fwlink/?linkid=2088631 — nunca lo descargamos
    automáticamente). Sin 4.8, use la baseline .NET 3.5 o el perfil degradado.

CONTENIDO
  LuminaPresentation.exe / LuminaPresentation35.exe  — interfaces (WPF/WinForms)
  Lumina.Core.dll / Lumina.Api.dll / Lumina.Bridge.dll — capa gestionada C#
  LuminaCore.dll    — núcleo nativo C++ (${arch}) del motor de proyección
  data/             — datos y recursos de fábrica

LICENCIA
  Vea LICENSE.md (Copyright (c) 2026 Isaac — Licencia View-Only).
EOF
  # Honest packaging: an acknowledged native-less rehearsal must say so.
  if [ "$nat" = "0" ]; then
    printf '\n*** AVISO DE EMPAQUETADO (ensayo local): este paquete NO incluye\n*** LuminaCore.dll (núcleo nativo). Compilado por el CI windows-latest.\n' >> "$dir/README.txt"
  fi
}

# ---------------------------------------------------------------------------
# Package one architecture
# ---------------------------------------------------------------------------
package_arch() {
  local arch="$1"
  local vdir="$OUT_DIR/portable/$arch"
  mkdir -p "$vdir/data"

  HDR "Variante $arch → $vdir"
  local src
  local count=0

  # ---- managed layer -----------------------------------------------------
  for pair in "LuminaPresentation.exe:wpf_exe" \
              "LuminaPresentation.exe.config:wpf_cfg" \
              "LuminaPresentation35.exe:win35_exe" \
              "LuminaPresentation35.exe.config:win35_cfg" \
              "Lumina.Core.dll:core_dll" \
              "Lumina.Api.dll:api_dll" \
              "Lumina.Bridge.dll:bridge_dll" ; do
    local dst="${pair%%:*}" fn="${pair##*:}"
    if src="$("$fn" "$arch" 2>/dev/null)" && [ -n "$src" ]; then
      src="$(copy_in "$src" "$vdir/$dst")" && OK "$(printf '%-34s ← %s' "$dst" "$src")" && count=$((count+1)) && ANY_PACKED=1
    else
      BAD "$dst — no se encontró en builds locales ni en staging"
    fi
  done

  # ---- optional managed bits ----------------------------------------------
  if src="$(cfg_net48 "$arch" 2>/dev/null)" && [ -n "$src" ]; then
    copy_in "$src" "$vdir/app.net48.config" >/dev/null && OK "$(printf '%-34s ← %s' "app.net48.config" "$src")"
  else NOTE "app.net48.config — omitido (no existe fuente)"; fi
  if src="$(cfg_net35 "$arch" 2>/dev/null)" && [ -n "$src" ]; then
    copy_in "$src" "$vdir/app.net35.config" >/dev/null && OK "$(printf '%-34s ← %s' "app.net35.config" "$src")"
  else NOTE "app.net35.config — omitido (no existe fuente)"; fi
  if src="$(wpf_dll "$arch" 2>/dev/null)" && [ -n "$src" ]; then
    copy_in "$src" "$vdir/Lumina.WPF.dll" >/dev/null && OK "$(printf '%-34s ← %s' "Lumina.WPF.dll" "$src")"
  else
    NOTE "Lumina.WPF.dll — omitido: la build actual fusiona el shell WPF DENTRO de LuminaPresentation.exe (v5.4.0)"
  fi
  if [ -n "$STAGING" ]; then
    if src="$(poc_dll "$arch" 2>/dev/null)" && [ -n "$src" ]; then
      copy_in "$src" "$vdir/Lumina.PocFacade.dll" >/dev/null && OK "$(printf '%-34s ← %s' "Lumina.PocFacade.dll" "$src")"
    else NOTE "Lumina.PocFacade.dll — omitido (el staging no lo trae)"; fi
  else
    NOTE "Lumina.PocFacade.dll — omitido por diseño (artefacto PoC; solo entra vía --staging)"
  fi

  # ---- native core --------------------------------------------------------
  local nat=0
  if src="$(native_dll "$arch" 2>/dev/null)" && [ -n "$src" ]; then
    copy_in "$src" "$vdir/LuminaCore.dll" >/dev/null && OK "$(printf '%-34s ← %s' "LuminaCore.dll ($arch)" "$src")" \
      && count=$((count+1)) && nat=1 && ANY_PACKED=1
  else
    report_native_gap "$arch"; NATIVE_WARNED=1
  fi
  if src="$(launcher "$arch" 2>/dev/null)" && [ -n "$src" ]; then
    copy_in "$src" "$vdir/LuminaLauncher.exe" >/dev/null && OK "$(printf '%-34s ← %s' "LuminaLauncher.exe" "$src")"
  else NOTE "LuminaLauncher.exe — omitido (opcional; el CI windows-latest lo compila)"; fi

  # ---- docs, data, license -------------------------------------------------
  cp -f LICENSE.md "$vdir/LICENSE.md" 2>/dev/null \
    && OK "$(printf '%-34s ← %s' "LICENSE.md" "LICENSE.md")" || BAD "LICENSE.md — ausente en la raíz del repo"
  make_readme "$vdir" "$arch" "$nat"
  OK "$(printf '%-34s ← %s' "README.txt" "generado (es-VE)")"
  if [ -d resources/data/sample ] && [ -n "$(ls -A resources/data/sample 2>/dev/null)" ]; then
    mkdir -p "$vdir/data/sample"
    cp -f resources/data/sample/* "$vdir/data/sample/" 2>/dev/null \
      && OK "$(printf '%-34s ← %s' "data/sample/*" "resources/data/sample/")"
  else
    mkdir -p "$vdir/data"; NOTE "data/sample/ — omitido (no hay recursos en resources/data/sample/)"
  fi

  # ---- drop *.pdb deliberately (kept out of the payload, reported) --------
  local pdbs
  pdbs="$(find "$vdir" -name '*.pdb' -type f 2>/dev/null || true)"
  if [ -n "$pdbs" ]; then
    printf '%s\n' "$pdbs" | while IFS= read -r p; do NOTE "excluido del paquete: $(basename "$p") (símbolos de depuración)"; rm -f "$p"; done
  fi

  # ---- verification: expected files present + size > 0 ---------------------
  local expect="LuminaPresentation.exe LuminaPresentation.exe.config LuminaPresentation35.exe LuminaPresentation35.exe.config Lumina.Core.dll Lumina.Api.dll Lumina.Bridge.dll"
  [ "$nat" = "1" ] && expect="$expect LuminaCore.dll"
  local f problems=0
  for f in $expect; do
    if [ -s "$vdir/$f" ]; then :; else BAD "verificación: $f ausente o tamaño 0"; problems=$((problems+1)); fi
  done
  while IFS= read -r f; do
    [ -s "$f" ] || { BAD "verificación: tamaño 0 — ${f#$vdir/}"; problems=$((problems+1)); }
  done < <(find "$vdir" -type f ! -name 'SHA256SUMS' 2>/dev/null)
  [ $problems -eq 0 ] && OK "verificación de contenido: todos los archivos presentes con tamaño > 0" \
                      || WARN "verificación de contenido: $problems problema(s)"

  # ---- SHA256SUMS per variant + sha256sum -c -------------------------------
  ( cd "$vdir" && find . -type f ! -name 'SHA256SUMS' -printf '%P\n' 2>/dev/null | LC_ALL=C sort | xargs -r sha256sum > SHA256SUMS )
  if ( cd "$vdir" && sha256sum -c --quiet SHA256SUMS >/dev/null 2>&1 ); then
    OK "SHA256SUMS generado y verificado con \`sha256sum -c\` ($(wc -l < "$vdir/SHA256SUMS") entradas)"
  else
    BAD "SHA256SUMS no pasó \`sha256sum -c\`"; problems=$((problems+1))
  fi

  # ---- zip ------------------------------------------------------------------
  local zip_base="LuminaPresentation-${VERSION}-portable-${arch}.zip"
  local zip
  zip="$(mkdir -p "$OUT_DIR" && cd "$OUT_DIR" && pwd)/$zip_base"
  rm -f "$zip"
  if command -v zip >/dev/null 2>&1; then
    ( cd "$vdir" && zip -r -X -q "$zip" . ) && OK "ZIP: $zip"
  elif command -v python3 >/dev/null 2>&1; then
    python3 - "$vdir" "$zip" <<'PY' && OK "ZIP: $zip (python zipfile)"
import os, sys, zipfile
src, dst = sys.argv[1], sys.argv[2]
with zipfile.ZipFile(dst, 'w', zipfile.ZIP_DEFLATED, compresslevel=9) as z:
    for root, dirs, files in os.walk(src):
        for d in dirs:
            full = os.path.join(root, d)
            z.write(full, os.path.relpath(full, src) + os.sep)
        for fn in sorted(files):
            full = os.path.join(root, fn)
            z.write(full, os.path.relpath(full, src))
PY
  else
    BAD "ni \`zip\` ni \`python3\` disponibles — no se pudo crear el ZIP"; return 1
  fi

  if command -v unzip >/dev/null 2>&1 && [ -s "$zip" ]; then
    unzip -t -qq "$zip" >/dev/null 2>&1 && OK "integridad del ZIP: \`unzip -t\` OK" || BAD "ZIP corrupto según \`unzip -t\`"
  fi
  [ -s "$zip" ] && printf "       SHA256(%s) = %s\n" "$(basename "$zip")" "$(sha256sum "$zip" | cut -d' ' -f1)"
  [ $problems -eq 0 ] && printf '       variante %s: %d binarios/datos esenciales empaquetados\n' "$arch" "$count"
  return 0
}

# ---------------------------------------------------------------------------
# Main
# ---------------------------------------------------------------------------
HDR "Inventario de fuentes"
[ -n "$STAGING" ] && { [ -d "$STAGING" ] || { BAD "staging inexistente: $STAGING"; exit 2; }; }

for arch in $ARCHS; do
  if [ -n "$STAGING" ] && [ ! -d "$STAGING/$arch" ]; then
    NOTE "staging sin variante $arch ($STAGING/$arch) — se omite esa variante"
    continue
  fi
  package_arch "$arch"
done

# ---------------------------------------------------------------------------
# Final verdict (NO silent failure: explicit summary)
# ---------------------------------------------------------------------------
HDR "Resumen"
if [ "$ANY_PACKED" -eq 0 ]; then
  BAD "no se empaquetó NADA — sin builds locales ni staging utilizable (exit 1)"
  printf '       Compile primero: dotnet build src/managed/Lumina.sln -c Release\n'
  printf '       o pase --staging <dir> con el layout del CI.\n'
  exit 1
fi
if [ "$NATIVE_WARNED" -eq 1 ]; then
  if [ "$ALLOW_MISSING_NATIVE" -eq 1 ]; then
    WARN "paquete(s) SIN núcleo nativo (--allow-missing-native). El paquete final de CI (windows-latest) SÍ incluye LuminaCore.dll."
  else
    WARN "PAQUETE INCOMPLETO: sin LuminaCore.dll nativo (ver comandos CI impresos arriba). Use --allow-missing-native para silenciar el veredicto en ensayos locales."
  fi
else
  OK "paquete(s) completo(s): nativo + gestionado por arquitectura"
fi
printf '       ZIPS y SHA256SUMS en: %s/portable/{x86,x64}/ y %s/*.zip\n' "$OUT_DIR" "$OUT_DIR"
exit 0
