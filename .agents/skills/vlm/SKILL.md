---
name: vlm
description: En 3.5_VISUAL_GATE, verificar capturas de la app (Live, Editor, Multiview, Stage View) contra slop/BASURA con análisis visual. No rediseñar.
---

# vlm — Puerta visual (3.5_VISUAL_GATE)

Solo aplica cuando una pieza pasa de lógica a full stack o GUI.

## Procedimiento

1. Precompilar la app y capturar pantallas: Modo Live, Editor, Multiview,
   Stage View (las cuatro vistas normativas).
2. Verificar contra slop/BASURA: texto cortado, controles solapados,
   contrastes ilegibles, jerarquía rota, artefactos de render.
3. Descartar/borrar las capturas al terminar (son temporales).
4. NO rediseñar: la corrección se limita a lo que rompe la norma del kit vigente.

## Registro

Si hay hallazgos bloqueantes → route a `6_RETRY` con el hallazgo específico
(archivo:línea). Si no, route a `4_AUDIT`.

Nota de entorno: en hosts sin sesión gráfica (CI headless) esta puerta se
documenta como limitación y se sustituye por la revisión de layout en código
+ el arnés de render a PNG del núcleo (`lumina_render_preview_png`).
