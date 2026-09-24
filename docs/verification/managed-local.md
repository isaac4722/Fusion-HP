# Verificación local de la capa gestionada — dotnet SDK 8 (Linux)

**Fecha:** 2026-09-24 · **Entorno:** contenedor Linux, dotnet SDK 8.0.425
instalado en el perfil del operador (`dotnet-install.sh`), ref assemblies
NuGet para net35/net48 (norma `Directory.Build.props`).

## Build (los 3 TFMs)

```text
$ dotnet build src/managed/Lumina.sln -c Release
  Lumina.Core -> .../net48/Lumina.Core.dll      (+ net35 + net8.0)
  Lumina.Bridge -> .../net48 Lumina.Bridge.dll  (+ net35)
  Lumina.Api -> .../net48 Lumina.Api.dll        (+ net35)
  Lumina.UI -> .../net35/LuminaPresentation35.exe · net48/LuminaPresentation.exe
  Lumina.WPF -> .../net48/LuminaPresentation.exe
  Tests -> .../net8.0/Lumina.Tests.dll          (+ net48)
Build succeeded.  0 errores · 0 warnings nuevos
```

## Tests (arnés propio del repo)

```text
$ LUMINA_SKIP_NATIVE=1 dotnet run --project src/managed/Tests -c Release -f net8.0
TESTS PASS 42/48 (6 skips)
```

- Los 6 skips son los tests de la DLL nativa (P/Invoke real): requieren
  `LuminaCore.dll` Windows; sus gates corren en el CI `windows-latest`
  (jobs `native-windows` heredado y `core-msvc` del árbol reestructurado).
- Nuevas piezas verificadas en verde: F1.05 (atajos roundtrip/defaults),
  F4.15 (FidelityReport PPTX/PDF/imágenes/macros + texto humano),
  F5.09 (JsBudget: conexiones denegadas, ciclo normal de timers intacto),
  F6.04 (Drive offline-first: cola persistida, flush sin red, descarga al
  reconectar, conflicto ahp.v1 → local gana → copia de la nube + historial),
  F6.08 (sesión de servicio simulada, campaña completa aparte).

## Sesión de servicio simulada (F6.08)

Campaña completa de 60 minutos en tiempo real: ver
`docs/verification/sesion60.md` (resultado medido + nota de entorno).
