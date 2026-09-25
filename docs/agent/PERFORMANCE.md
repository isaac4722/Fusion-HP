# PERFORMANCE.md — presupuesto y medición [SPEC §10.1]

| Métrica | Objetivo | Dónde se mide |
|---|---|---|
| RAM en reposo (Live) | ≤ 300 MB | diagnóstico + log cada 60 s |
| RAM pico con video 1080p | ≤ 700 MB | diagnóstico |
| Arranque en frío | ≤ 3 s | log bootstrap→primer show |
| Transición entre elementos | ≤ 16 ms | render marca ms/frame en DEBUG |
| Cambio de línea | ≤ 1 frame | comando `line` no re-renderiza fondo |
| Importación PPTX (100 láminas) | ≤ 10 s | informe del importador |
| Búsqueda bíblica | ≤ 200 ms | test automatizado (FusionTests) |

Estrategias implementadas [SPEC §10.3]:
1. Carga diferida: `preload` del siguiente elemento; liberación del anterior.
2. Un modelo resuelto, tres consumos (pública/retorno/multiview).
3. Ventana persistente + fondo en caché → transición = redraw, no recarga.
4. Servicios (API/OBS/triggers) arrancan bajo demanda y en hilos de fondo.
5. Índice bíblico en disco (.fbi): solo el índice (16 B/verso) en memoria.
