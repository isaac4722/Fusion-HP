---
name: performance-budget
description: Presupuesto de rendimiento de la tabla 10.1 — RAM, arranque, latencias, importaciones y búsquedas. Verificar al cerrar cualquier pieza que toque render, media o arranque.
---

# performance-budget — Tabla 10.1 [SPEC §10]

## Objetivos medibles (F6.02)

| Métrica | Objetivo |
|---|---:|
| RAM reposo (Modo Live, proyecto típico) | ≤ 300 MB |
| RAM pico con video 1080p | ≤ 700 MB |
| Arranque en frío hasta control operativo | ≤ 3 s |
| Transición entre elementos | ≤ 16 ms, sin frame negro |
| Cambio de línea | ≤ 1 frame (~16 ms) |
| Importación PPTX (100 diapositivas) | ≤ 10 s |
| Búsqueda bíblica por palabra | ≤ 200 ms |

Condiciones de referencia: Win7 x86, 4 GB RAM, HDD mecánico, salida activa
30 min. Mediciones reales o `TODO:` — prohibido inventar cifras.

## Estrategias vinculantes (F6.03)

1. Carga diferida estricta: solo elemento activo + siguiente residentes.
2. Un render, tres consumos (pública, retorno, Multiview comparten buffers).
3. Disciplina del heap x86: video por bloques, texturas comprimidas,
   liberación determinística.
4. Concurrencia acotada: cola sin bloqueo, thread pool limitado, red/disco
   con prioridad BelowNormal durante la proyección.
5. Inicio diferido de API, Triggers, Drive y editor.
6. Medición continua: contadores visibles en Diagnóstico y volcados al log
   cada 60 s durante Live.

## Prohibiciones

- La ligereza NO autoriza recortar funcionalidad (los 200 MB son ejemplo
  ilustrativo, no cuota).
- Ante falta de memoria en x86: streaming + degradación controlada — nunca
  eliminar la función.
