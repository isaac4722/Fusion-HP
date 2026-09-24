# DEPENDENCIAS.md — Dependencias y toolset

## Núcleo nativo (C++)

- MSVC toolset compatible con Win7 (`_WIN32_WINNT=0x0601`), CRT estático `/MT`.
- `third_party/sqlite3` (amalgamado, FTS5+JSON1 habilitados por definición) y
  `third_party/nlohmann` (JSON header-only) — empaquetados, sin descargas.
- `third_party/miniz` — contenedor ZIP/OPC (importación PPTX y respaldos).
- System libs Win7 RTM permitidas: kernel32, user32, gdi32, gdiplus, d2d1,
  ole32, oleaut32, shell32, commdlg32, avrt.
- TODO: confirmar con el propietario si la salida NDI (F5.06) usa el SDK
  oficial de NDI (redistribución sujeta a licencia) — mientras tanto la
  integración queda con degradación controlada documentada.

## Capa gestionada (C#)

- **Cero paquetes NuGet funcionales** — el OPC/ZIP es motor propio
  (`ZipWriter`/`ZipBackup`); JSON es `MiniJson`.
- `Microsoft.NETFramework.ReferenceAssemblies` (PrivateAssets) para compilar
  net35/net48 sin targeting pack de Windows (Linux/CI).
- TFMs: `net35;net48` producto · `net8.0` arnés de tests/validación.
- WinForms (net35+net48) y WPF (net48) — referencia del propio Framework.

## Tooling del agente

- Local: g++ (arnés portable), dotnet SDK 8 (builds+tests), python
  (`duckduckgo_search`/`ddgs` para el estado PLAN).
- CI: windows-latest (MSVC, .NET SDK, Inno Setup no instalado — el .iss se
  compila en un job dedicado si se habilita choco install innosetup),
  ubuntu-latest (port GCC, exportadores python).
- TODO: confirmar con el propietario si el job de release debe firmar los
  instaladores (no hay certificado en el repo).
