# Fusion HP v4.1.1 — hotfix de distribución: el portable arranca de nuevo

**Fusion HP** es un presentador litúrgico híbrido (núcleo nativo C++ + capa C#/.NET Framework) que opera desde **Windows 7 SP1 x86** hasta **Windows 11 x64**, sin Java, sin .NET Core como runtime obligatorio y sin escribir en el Registro de Windows.

## Qué fallaba en v4.1.0

**El portable no abría el programa** (FusionStudio.exe moría en silencio al arrancar). Causa exacta: el paso de empaquetado del CI construía el zip portable **sin las DLL de terceros** que la v4.1.0 introdujo — el instalador sí las llevaba, el portable no. Al arrancar, la primera línea de `Program.Main` inicializa el registro (NLog) y el CLR no encontraba `NLog.dll`: excepción de carga antes de crear cualquier ventana, sin mensaje visible.

## Corregido en v4.1.1

### 1. Portable completo (x86 y x64)
El portable lleva ahora **las mismas 15 DLL de terceros + interop SQLite x86/x64 + PDFsharp dual** que el instalador. Nueva **verificación dura en CI**: si falta una sola pieza crítica del zip (19 comprobadas por nombre), el job de empaquetado cae y la release no se genera. Esta clase de fallo **no puede repetirse**.

### 2. Cadena transitiva de PDFsharp 6.1.1 vendorizada (4 DLL, MIT)
La exportación PDF de la v4.1.0 instalada también hubiera fallado: `Microsoft.Extensions.Logging.Abstractions` y `System.Memory` piden piezas que no viajaban en el paquete. Vendorizadas y documentadas con hash en DEPENDENCIAS.md:
- System.Buffers 4.5.1 · System.Memory 4.5.4 · System.Numerics.Vectors 4.5.0 · System.Runtime.CompilerServices.Unsafe 6.0.0 (todas MIT, builds net461/net46).

### 3. Resolución de terceros sin app.config (regla de oro intacta)
`Program.cs` instala un resolvedor de último recurso (solo actúa si el enlace estándar falla) que cubre:
- **PDFsharp dual**: Studio (net48) usa la 6.1.1 junto al exe; **Lite (net35)** recibe su copia 1.50 desde la subcarpeta `PdfSharp-1.50\` (CLR2 no puede leer el 6.1.1 netstandard2.0).
- Versiones menores pedidas por la cadena `System.Memory → CompilerServices.Unsafe` que difieren de las vendorizadas.

### 4. Verificaciones
- Compilación completa: FusionShared (net35) + FusionStudio (net48) + FusionStudio.Lite (net35) + FusionTests (net48) — 0 errores.
- Gate de calidad: 0 violaciones.
- CI: núcleo C++ x86+x64 + tests nativos + tests administrados net48 + verificación de imports Win8+ (Win7 SP1) + verificación de 19 piezas críticas en ambos portables.
- Instalador y portable quedan **en paridad de contenido**.

**Instalación**: misma mecánica de siempre — `FusionHP-4.1.1-setup.exe` (dual x86/x64) o el portable (`FusionHP-Portable-x86.zip` universal / `FusionHP-Portable-x64.zip`), con checksums SHA256. Si la v4.1.0 instalada funcionaba pero fallaba al exportar PDF, esta versión lo corrige; los datos y la configuración se conservan.
