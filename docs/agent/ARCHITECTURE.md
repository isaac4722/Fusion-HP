# ARCHITECTURE.md — Arquitectura dual y IPC

**Fuente normativa:** `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md` §3–§5.

## Dos capas por criticidad

1. **Núcleo nativo C++ (Win32/CRT, `/MT`, x86+x64)** — lo que *no puede fallar*:
   bootstrap con detección (SO por `RtlGetVersion`, arquitectura por
   `IsWow64Process2`, .NET por claves NDP de solo lectura), perfiles A/B/C,
   render (Direct2D cuando disponible / GDI+ fallback), DirectShow, salida
   borderless persistente, IPC `ipc.v1`, log binario y UI de emergencia (perfil C).
2. **Capa gestionada C# (.NET Framework 3.5 SP1→4.8, WinForms + WPF)** — lo que
   *gana productividad*: shell WinForms, editor WPF vía `ElementHost`, modelo
   `ahp.v1`, importadores/exportadores, servidor API (`HttpListener`), Triggers,
   OBS/NDI/PCO/Drive, JSLib y diagnóstico.

## Frontera nativo/gestionado

- IPC por **pipes con nombre**, mensajes binarios con longitud prefijada,
  protocolo versionado `ipc.v1` (frame: magic u32, version u16, type u16, length u32).
- **Cola sin bloqueo** de comandos hacia render con límite de latencia explícito.
- El núcleo opera íntegro sin C# (perfil C). El hosting CLR en proceso es el
  objetivo del producto; el P/Invoke existente es la vía A documentada.
- API C plana del núcleo (`native/core/include/lumina/lumina.h`): texto UTF-8,
  sin excepciones cruzando la frontera, patrón de búfer `out/cap/needed`.

## Modelo de datos compartido

`ahp.v1` (JSON versionado): Proyecto → Escenarios → Elementos (exactamente
cinco tipos: Texto Formateado, Versículo Bíblico, Imagen, Video, Lower Third).
Herencia de estilos: Tema → Plantilla de Escenario → Escenario → Elemento
(`null` = heredar). IDs estables entre sesiones.

## Mapa de carpetas (reestructuración v1.0.0)

- `src/core/`: proyecto MSBuild del núcleo + módulos nuevos.
- `native/core/`: fuentes base del núcleo (ruta congelada por la restricción
  CMake del plan — el `.vcxproj` las compila juntas con los módulos nuevos).
- `src/managed/Lumina.Core/`: `core/` (dominio), `data/` (persistencia e
  índices bíblicos), `services/` (OBS/NDI/PCO/Drive/diagnóstico),
  `state/` (StateProvider), `features/` (interop, triggers, scripting).
- `src/managed/Lumina.UI` (WinForms) y `Lumina.WPF` (editor): las dos caras
  del shell; el editor comparte el contrato de coordenadas del motor (WYSIWYG).

## Compilación permitida

MSBuild es el mecanismo normativo de la línea reestructurada
(`src/core/core.vcxproj`, `src/managed/Lumina.sln`). El CMake heredado de
`native/` **no se crea, restaura, regenera ni modifica** (restricción del
plan); compila el subset portable del núcleo en el CI Linux.
