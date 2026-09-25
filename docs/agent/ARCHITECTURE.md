# ARCHITECTURE.md — Arquitectura dual de Fusion-HP

Verdad funcional: `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md` [SPEC §3].

## Visión general

```
FusionHP.exe (C++ /MT, Win32, x86 y x64)
 ├─ Bootstrap [SPEC §4.1]: detecta SO → arquitectura → .NET (filesystem, sin Registro)
 ├─ Salida de proyección: ventana borderless persistente en el monitor elegido
 │   ├─ Renderer: Direct2D (HwndRenderTarget) con fallback GDI+ doble buffer
 │   ├─ Capas: fondo (caché) → contenido → lower third [SPEC §6.4]
 │   └─ HWND hijo para video DirectShow (VMR9 windowless)
 ├─ IPC ipc.v1: \\.\pipe\FusionHP.ipc.v1 (longitud prefijada u32 LE + JSON UTF-8)
 ├─ UI de emergencia nativa (perfil C): abrir .ahp, navegar, reposo [SPEC §4.2]
 └─ Lanza FusionStudio.exe (perfil A, net48) o FusionStudio.Lite.exe (perfil B, net35)

FusionStudio.exe (C# net48, WinForms + WPF)
 ├─ MainForm: Inicio (mosaicos) · Estudio (editor embebido) · Presentación (por defecto)
 ├─ LiveOrchestrator: cerebro — resuelve herencia 4 niveles y envía el estado RESUELTO
 ├─ Biblioteca: Cantos (songs/*.json) + Biblia (índice en disco .fbi) + Medios
 ├─ Importadores: Zefania XML · e-Sword (.bib SQLite 9+ con Twofish) · JSON · TSV · PPTX (COM y OpenXML)
 ├─ Exportadores: PPTX (System.IO.Packaging) · PDF (escritor puro) · PNG (GDI+)
 ├─ ApiServer (HttpListener, 6 endpoints + token + /remote móvil)
 ├─ ObsClient (WebSocket RFC6455 propio + auth sha256)
 └─ TriggerEngine (evento → condiciones → acciones, JSON)

FusionStudio.Lite.exe (C# net35, WinForms, define LITE)
 └─ Mismas fuentes; editor funcional WinForms en lugar de WPF [SPEC §7.1.2]
```

## Contrato ipc.v1 (lo que C# envía al núcleo)

Mensaje: `[u32 LE longitud][JSON UTF-8]`. Respuestas llevan `id` de eco.

| Comando | Payload | Efecto |
|---|---|---|
| `hello` | `{}` | handshake; respuesta con `protocol:"ipc.v1"` |
| `show` | `{slide:{…}}` | proyecta el elemento resuelto |
| `preload` | `{slide:{…}}` | decodifica imágenes del siguiente (carga diferida) |
| `line` | `{index:n}` | línea activa (solo capa de texto) |
| `blank` | `{mode: none\|black\|logo\|theme}` | pantalla de reposo |
| `clear` | `{}` | negro |
| `video` | `{action, value}` | pause/resume/stop/volume |
| `monitors` / `monitor` | lista / `{output,index}` | gestión multi-pantalla |
| `env` / `state` | — | informe de entorno / estado |
| `quit` | — | apagado ordenado |

`slide` (RESUELTO por C#, dibujado tal cual por C++):
`id, kind(text|image|video|verse|lower3), lines[], activeLine, reference, style{font,size,bold,italic,color,activeColor,align,vAlign,shadow,outline,lineSpacing,box{x,y,w,h}}, bg{color,image,fit,opacity}, media{src,loop,volume,startAt}, overlay{present,lines,style,position,duration}`.

Eventos del núcleo → clientes: `{"v":1,"evt":"state","data":{…}}` y `{"evt":"warning", …}` (fail-safe de video).

## Reglas de oro

1. El núcleo **nunca** depende de .NET; el estudio **nunca** toca el render directamente.
2. La herencia de estilos se **resuelve en C#** (`ResolvedSlide.Resolve`) — el núcleo no conoce temas, solo estados resueltos. Esto hace posible el tema en caliente re-enviando el estado.
3. La ventana de salida **nunca se destruye** entre elementos [SPEC §6.3.1].
4. Todo archivo de proyecto es `ahp.v1` JSON versionado; campos nuevos deben ser ignorables [SPEC §5.3].
5. Cero dependencias de runtime no incluidas en el paquete [SPEC §3.5].
