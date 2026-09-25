# DEPENDENCIAS.md — dependencias y toolset

## Núcleo C++ (todas incluidas en el paquete)
- Win32: user32, gdi32, gdiplus, d2d1, dwrite, windowscodecs, strmiids, quartz,
  comctl32, comdlg32, ole32, uuid, shell32, shlwapi — todas del sistema (Win7+).
- `third_party/nlohmann/json.hpp` — header-only, MIT, incluida en el repo.

## Capa C# (todas del framework, cero NuGet de runtime)
- net35/net48: System.Windows.Forms, System.Drawing, System.Xml, System.Core.
- net48 (Studio): PresentationFramework/PresentationCore/WindowsBase (WPF),
  WindowsFormsIntegration (ElementHost), WindowsBase (System.IO.Packaging).
- TcpClient/HttpListener/SHA256/DeflateStream — parte del framework.

## Toolset de compilación (no se distribuye)
- MSVC v143 (WindowsTargetPlatformVersion 10.0), `/MT`.
- .NET SDK (para `dotnet build` de proyectos SDK-style net35/net48) +
  `Microsoft.NETFramework.ReferenceAssemblies` (solo build, PrivateAssets).
- Inno Setup 6 (instalador), zip (portable).

## Prohibido [SPEC §3.5]
Java/JRE · .NET Core/.NET 5+ como runtime · escritura al Registro ·
cualquier dependencia no incluida en el paquete.
