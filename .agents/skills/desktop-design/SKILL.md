---
name: desktop-design
description: Convenciones de escritorio Windows para consulta en PLAN — targets táctiles, teclado, DPI, comportamiento de ventanas. Solo consulta: no dicta estética.
---

# desktop-design — Convenciones de consulta [AGENT.md: solo consulta]

Aplicable a los shells WinForms/WPF y a la UI nativa. No define estética
(eso lo fija `UiKit`/tokens del repo); fija comportamientos de plataforma.

## Puntos de verificación

1. **Targets**: controles interactivos ≥ 32 px en modos táctiles; separación
   suficiente para operar de pie con proyector encendido.
2. **Teclado**: toda acción operativa accesible por teclado (el operador no
   puede depender del ratón durante el servicio); atajos personalizables
   persistidos en JSON (F1.05).
3. **DPI**: `PerMonitorV2` en manifiestos; sin blurring en monitores mixtos.
4. **Ventanas**: minimizar la ventana de control NO cierra la salida (F0.07);
   cambio de monitor RECOLOCA la ventana en lugar de duplicarla.
5. **Foco**: los diálogos de error no roban el foco de la proyección de forma
   destructiva; "Copiar detalles técnicos" disponible.
6. **Bandeja**: icono de bandeja para la ventana de control (F0.07.6).
