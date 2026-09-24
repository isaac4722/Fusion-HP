---
name: directshow-media
description: Reproducción de video con DirectShow en el núcleo C++ — volumen inicial, startAt, loop, posición, carga diferida estricta y fail-safe. Usar siempre que se toque la reproducción de video.
---

# directshow-media — Video en el núcleo [SPEC §6.4, §6.5, F3.01, F3.02]

## Filter Graph DirectShow (F3.01)

1. `IGraphBuilder` + `IMediaControl`/`IMediaSeeking`/`IBasicAudio`/`IMediaEventEx`.
2. Renderizar en la ventana de salida (proyección), no en ventana aparte.
3. Aplicar: volumen inicial, `startAt` (seek inicial), `loop` (re-seek al
   llegar al fin vía evento EC_COMPLETE), posición persistida.
4. Liberar filtros y buffers DETERMINÍSTICAMENTE (RAII) — sin depender del GC.
5. Decodificación por bloques: nunca frames completos en RAM (heap x86 ~2 GB).
6. No cargar todos los videos del proyecto — carga diferida estricta:
   solo el elemento activo y el siguiente residen en memoria.

## Fail-safe de video (F3.02)

Ante error de carga/reproducción:
1. La salida NUNCA se cierra ni queda negra sin aviso.
2. Sustituir el video por el fondo del tema.
3. Aviso en el monitor del operador con acción sugerida
   ("Puedes convertirlo a MP4/H.264 o elegir otro archivo").
4. Log técnico: formato, ruta, error, componente — sin contenido de medios.

## Integración con el pipeline

El video es una capa del pipeline (fondo → contenido → lower third →
indicadores). Cambiar de LÍNEA de texto no debe tocar la memoria del video.
