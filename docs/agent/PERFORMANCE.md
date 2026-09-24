# PERFORMANCE.md — Rendimiento y presupuesto

**Fuente:** documento técnico v1.1 §10 (tabla 10.1, estrategias vinculantes).

## Objetivos medibles (cifras de la SPEC — no inventar mediciones propias)

| Métrica | Objetivo | Condición |
|---|---:|---|
| RAM reposo (Live, proyecto típico) | ≤ 300 MB | Win7 x86, 4 GB, salida 30 min |
| RAM pico (video 1080p) | ≤ 700 MB | ídem, con precarga |
| Arranque en frío | ≤ 3 s | HDD mecánico, Win7 x86 |
| Transición entre elementos | ≤ 16 ms sin frame negro | cualquier par de tipos |
| Cambio de línea | ≤ 1 frame (~16 ms) | texto sobre fondo estático |
| Importación PPTX (100 slides) | ≤ 10 s | HDD, perfil A |
| Búsqueda bíblica por palabra | ≤ 200 ms | RV1960 indexada |
| Tamaño instalado | ~200 MB referencial (sin límite duro) | prioridad: funcionalidad |

## Estrategias vinculantes

1. Carga diferida estricta: solo elemento activo + siguiente en memoria;
   Biblias por índice en disco.
2. Un render, tres consumos (pública/retorno/Multiview desde los mismos buffers).
3. Disciplina del heap x86: video por bloques, texturas comprimidas,
   liberación determinística (sin depender del GC para recursos nativos).
4. Concurrencia acotada: cola de render sin bloqueo; red/disco con prioridad
   BelowNormal durante la proyección.
5. Inicio diferido: API, Triggers, Drive y editor se inicializan bajo demanda.
6. Medición continua: contadores por subsistema en Diagnóstico + volcado al
   log cada 60 s en Live (F6.01).

## Mediciones de esta reestructuración

Las evidencias reales (builds, tiempos de importación/búsqueda medidos en el
arnés local, memoria de la sesión de 60 min) viven en `docs/verification/`.
Toda cifra allí es resultado medido; las limitaciones del entorno (sin Win7
x86 ni HDD mecánico) se declaran junto a cada medición.
