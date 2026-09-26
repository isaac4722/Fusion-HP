---
name: taste-skill
description: Evaluación estética opcional en 4_AUDIT cuando la puerta visual detecta riesgo de "AI slop" — exceso de ornamento, gradientes sin función, iconografía incoherente.
---

# taste-skill — Evaluación contra AI slop (opcional, 4_AUDIT)

Se activa SOLO si `vlm` detectó riesgo estético.

## Señales de slop a descartar

1. Ornamento sin función: gradientes, sombras o esquinas que no comunican
   estado ni jerarquía.
2. Iconografía incoherente (mezcla de familias visuales) o emojis en UI seria.
3. Texto decorativo en lugar de operativo ("¡Bienvenido a bordo!").
4. Densidad falsa: relleno visual que no reduce el tiempo de decisión del
   operador.
5. Inconsistencia con el kit vigente (`UiKit`/`Theme/*.xaml`).

## Veredicto

- **Slop confirmado** → hallazgo bloqueante a `6_RETRY` con corrección puntual.
- **Gusto personal** → NO bloquea; se registra como mejora opcional.
- La norma manda: "eliminación de relleno" [SPEC §2.3] — si duda, quitar.
