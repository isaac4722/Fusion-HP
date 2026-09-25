#!/usr/bin/env bash
# ============================================================================
#  package_portable.sh — construye el paquete portable [SPEC §4.4]:
#  ejecución desde USB sin escribir fuera de su carpeta (datos junto al exe).
#  Uso (después de compilar):
#    bash scripts/package_portable.sh <arquitectura: x86|x64>
# ============================================================================
set -eu
cd "$(dirname "$0")/.."
ARCH="${1:-x86}"
PLATFORM="$([ "$ARCH" = x64 ] && echo x64 || echo Win32)"

SRC_CORE="dist/$PLATFORM/FusionHP.exe"
SRC_SHARED="src/managed/FusionShared/bin/Release/FusionShared.dll"
SRC_STUDIO="src/managed/FusionStudio/bin/Release/FusionStudio.exe"
SRC_LITE="src/managed/FusionStudio.Lite/bin/Release/FusionStudio.Lite.exe"

for f in "$SRC_CORE" "$SRC_SHARED" "$SRC_STUDIO" "$SRC_LITE"; do
  if [ ! -f "$f" ]; then
    echo "FALTA: $f (compila primero ambas capas)" >&2
    exit 1
  fi
done

OUT="dist/portable/FusionHP-Portable"
rm -rf "$OUT"
mkdir -p "$OUT/resources/img" "$OUT/datos"

cp "$SRC_CORE"  "$OUT/FusionHP.exe"
cp "$SRC_SHARED" "$OUT/FusionShared.dll"
cp "$SRC_STUDIO" "$OUT/FusionStudio.exe"
cp "$SRC_LITE"  "$OUT/FusionStudio.Lite.exe"
cp -r resources/img/* "$OUT/resources/img/" 2>/dev/null || true
printf '# Fusion HP modo portable: los datos viven en la carpeta datos/\n' > "$OUT/portable.flag"
cat > "$OUT/LEEME.txt" << 'EOF'
Fusion HP — versión portable
============================
Ejecuta FusionHP.exe desde esta carpeta (desde USB si quieres).
Todos los datos (cantos, biblias, proyectos, logs) se guardan en "datos".
No instala nada, no escribe en el Registro de Windows y no necesita permisos
de administrador.
EOF

ZIP="dist/FusionHP-Portable-$ARCH.zip"
rm -f "$ZIP"
(cd dist/portable && zip -qr "../$(basename "$ZIP")" FusionHP-Portable)

# Checksums
if command -v sha256sum >/dev/null 2>&1; then
  (cd dist && sha256sum "$(basename "$ZIP")" > "SHA256SUMS-portable-$ARCH.txt")
fi
echo "Portable listo: $ZIP"
