---
name: ui-nice-skill
description: Auditar pantallas y flujos de UI (Shell WinForms, Editor WPF, Multiview, Stage View) contra el sistema de diseño vigente del repo. No inventa estética.
---

# ui-nice-skill — Auditoría de UI contra el sistema vigente

Uso en `1_ANALYZE` y `4_AUDIT` cuando el encargo toca UI.

## Reglas de auditoría

1. **Fuente de estética**: los tokens/kit vigentes del repo (`UiKit.cs` en
   WinForms, `Theme/*.xaml` en WPF). Prohibido introducir una segunda estética.
2. **Consistencia de idioma**: toda etiqueta visible en español es-VE, tono
   operativo directo ("Proyectar", "Pantalla de reposo"), sin anglicismos
   innecesarios.
3. **Estados visibles**: todo control de Live refleja el estado REAL
   (sondeo, no suposición) — lección v7.1.0 del interruptor F5.
4. **Densidad operativa**: los paneles del Modo Presentación priorizan
   información accionable; nada decorativo sin función (principio "sin relleno").
5. **Coherencia dual**: el editor WPF y la salida comparten el mismo contrato
   de coordenadas (WYSIWYG real, anti-patrón PowerPoint).
6. **Accesibilidad**: contraste, foco visible, `AutomationProperties`
   (complementa `dotnet-accessibility`).

## Salida

Lista de hallazgos con archivo:línea, gravedad (bloqueante/mejora) y
corrección propuesta contra el kit vigente.
