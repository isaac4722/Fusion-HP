# Fusion HP v4.1.0 — dependencias permisivas verificadas · licencia v1.1 · GUI pulida

**Fusion HP** es un presentador litúrgico híbrido (núcleo nativo C++ + capa C#/.NET Framework) que opera desde **Windows 7 SP1 x86** hasta **Windows 11 x64**, sin Java, sin .NET Core como runtime obligatorio y sin escribir en el Registro de Windows.

## Novedades v4.1.0

### 1. Lista cerrada de librerías (11, todas permisivas — ver DEPENDENCIAS.md)
Cada librería = una pieza = un commit, con pruebas que cubren la sustitución:

**Núcleo C++:**
- **Microsoft WIL 1.0.240803.1** (MIT): RAII de COM/HANDLE (`WIL_EXCEPTION_MODE=1`, sin excepciones). VideoPlayer (DirectShow), mutex del bootstrap, handles de procesos y búsqueda de fuentes ya no pueden fugar. `TestWil` en CoreTests.
- **doctest 2.4.11** (MIT): aserciones idiomáticas dentro de CoreTests **sin tocar** el arnés propio, la convención `Test*` ni el código de salida (handler + `setAsDefaultForAssertsOutOfTestCases`).
- **SQLite amalgamation 3.45.1** (dominio público): lector nativo fdb/biblias con SQL real. `SqliteDb` RAII + 4 escenarios: B-tree con 30 000 filas, WAL entre conexiones, UTF-16 con texto español y **módulo e-Sword real del repo** (Génesis 1:1, Scripture cifrada). `FusionHP.exe` x64: **2,24 MB**.
- **spdlog 1.12.0** (MIT): `Logger.cpp` con sink diario síncrono (retención 14 días, hilo-seguro), formato SPEC §11.1.5 byte a byte y `SPDLOG_NO_EXCEPTIONS`. **Verificación Win7 automatizada**: gate CI `dumpbin /IMPORTS` (x86+x64, exe+tests) — **cero imports Win8+**.

**Capa C#:**
- **Newtonsoft.Json 13.0.3** (MIT, net35): motor detrás de la fachada `JsonValue` — API pública intacta, `FormatException` preservado, round-trip ahp.v1 estable; corrige escapes sustitutos, números exóticos y BOM.
- **NLog 4.7.15** (BSD-3): `LogService` con rotación diaria; los hooks `cs.ui`/`cs.domain` migrados al mismo `studio-errors.log`. **Regla dura: nunca se registran letras ni versículos.**
- **PDFsharp** (MIT): exportación PDF con **Unicode real** y **fuentes del producto incrustadas** (Outfit, Cormorant Garamond, Libre Baskerville vía `IFontResolver` — `/FontFile2` verificado por prueba). FusionStudio usa **6.1.1** (desviación documentada: la 1.50 trae `XPrivateFontCollection` stub y no puede incrustar fuentes privadas — verificado y registrado en DEPENDENCIAS.md); Lite conserva la 1.50.
- **System.Data.SQLite.Core 1.0.118** (PD/MIT): vía ADO.NET (Read Only) como lector primario de módulos e-Sword con **fallback intacto al lector puro de B-Tree** del perfil B; interop x86/x64 incluido.
- **DocumentFormat.OpenXml 2.7.2** (MIT): importador PPTX con modelo tipado, herencia Run→cuerpo→layout→maestro→tema real y anti-XXE por construcción. **Solo FusionStudio net48**; Lite conserva la vía Packaging. `PptxDirectProjector` intacto.
- **Ookii.Dialogs 1.0.0** (BSD-3): botón «Carpeta» en Medios (importa carpetas completas) con diálogo nativo de Vista+ y fallback clásico. Sin JumpLists.
- **Portable.BouncyCastle 1.8.9** (MIT): vendida como motor disponible para formatos futuros; `Twofish.cs` propio sigue en uso (vectores oficiales I=1,2,3 vigentes). Vector SHA-256 NIST cubierto por prueba.

### 2. El SDK destapó dos bugs reales del contenedor PPTX (y se corrigieron)
- La relación raíz se escribía con `Type="officeDocument"` en vez del URI completo.
- Las relaciones del paquete se escribían como **partes sueltas** (`_rels` a mano) en vez de `CreateRelationship`: el paquete exportado tenía las relaciones raíz vacías (PowerPoint real también lo rechazaría). Ahora el contenedor es OPC estricto: presentación→slides/master, master→theme/layout, slide→layout/medios.

### 3. GUI: estados y organización
- **Estados `Enabled=false` completos** en toda la superficie modelada: FusionInput/FusionSearchBox (borde tenue, fondo gris, TextBox interno sincronizado, icono atenuado), FusionIconButton (icono al 35 % con ColorMatrix), FusionTabs (sin hover/selección por ratón, título e iconos atenuados, re-medición al cambiar DPI/fuente).
- Guardas de navegación: `FusionTabs.Add(null)` → contrato claro; layout sano en transiciones disabled↔enabled.
- Pruebas: `V41Tests` extendido (estados, navegación entre pestañas, transiciones de habilitación) y verificación de que **los 42 nombres de iconos usados existen** en los 4 sets — cero botones sin glifo.
- Iconos: inventario completo — 62 glifos × 4 tintas ya presentes, 0 huecos.

### 4. Licencia v1.1 — autoridad del autor + abiertas con atribución
- **Se mantienen íntegras** las cláusulas de autoridad: copyright exclusivo, solo-lectura del código, uso libre ministerial de los binarios, prohibida la ingeniería inversa.
- **§4 reescrita con la realidad**: se eliminan las entradas fantasma (Qt 5.15, LibVLC, miniz — nunca existieron en el árbol) y se declara la lista real de componentes permisivos.
- **Regla de oro permanente**: solo permisivos (MIT/BSD/Apache-2.0/Zlib/PD/OFL); prohibido GPL/AGPL/LGPL enlazado estático, salvo licencia comercial o LGPL con enlace dinámico + reemplazo.
- **Nueva cláusula**: `DEPENDENCIAS.md` es normativa, se actualiza con cada release y prevalece sobre §4.

## Qué incluye (resumen v4.0.x, vigente)
- Núcleo C++ (`FusionHP.exe`, /MT, x86+x64): bootstrap con perfiles A/B/C, salida borderless sin parpadeo, sincronización línea por línea, video DirectShow fail-safe, Stage View y servidor IPC `ipc.v1`.
- 4 biblias completas en español (RV1960, NVI, RVG, RVR1909) autoinstaladas; canciones BD→proyección sin generar PPTX; PPTX solo cuando el operador lo pide.
- GUI web consolidada en C++/C#: Temas en caliente, Biblia rápida G con favoritos y Tercio, miniaturas, recientes con nombre.
- Instalador dual (x86+x64) con presupuesto **≤ 10 MB**, portables por arquitectura y SHA256SUMS. El módulo de muestra e-Sword (RVR1909, 4,4 MB) queda en el repo para pruebas e importación y no viaja en el instalador; las 4 biblias completas sí van incluidas.

## Verificación
- CI: núcleo x86+x64 (144 checks nativos) · C# net35+net48 (58 pruebas) · gate de prohibiciones 0 violaciones · gate de imports Win8+ en verde · instalador dual + portables + SHA256SUMS.
- La verificación manual final en Win7 x86 real sin .NET (perfil C) queda para el despliegue del propietario; el gate de imports es el proxy automatizado en CI.
