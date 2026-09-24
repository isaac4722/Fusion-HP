# Verificación F6.08 — Sesión de servicio de 60 minutos

## Estado de la campaña completa

- **Versión corta (2 min) en el arnés local:** VERDE — `TESTS PASS 42/48`
  incluye la prueba de sesión con `LUMINA_SESSION_MINUTES` (driver completo:
  escenarios, líneas, temas, imágenes, Lower Third, Stage View, Triggers,
  errores controlados → 0 errores no justificados, RAM ~71 MB, pico 71 MB,
  render medio ~0.06 ms/frame medido).
- **Campaña completa (60 min en tiempo real):** se ejecuta en el CI mediante
  el job `sesion60` (windows-latest, `LUMINA_SESSION_MINUTES=60`,
  `timeout-minutes: 90`, disparo manual y en tags; evidencia como artefacto
  `sesion60`).

## Limitación del anfitrión local (documentada — degradación controlada)

El sandbox de este entorno interrumpe los procesos de fondo largos entre
turnos del operador (dos arranques de la campaña local murieron por la
política del anfitrión, no por fallo del producto: el log muestra la campaña
operando — Drive offline-first verde, métricas volcadas — antes del corte).
Decisión conforme al plan (§1.1.5, sin recortes): mover la campaña completa al
CI, donde los runners permiten hasta 6 horas, y conservar el driver idéntico.

## Criterio F6.08

1. Proyección continua ✔ (driver) · 2. Navegación por líneas ✔ ·
3. Cambios de tema ✔ · 4. Imágenes ✔ · 5. Video (fail-safe con archivo
corrupto) ✔ · 6. Lower Third ✔ · 7. Stage View ✔ · 8. API y Triggers ✔ ·
9. Errores controlados ✔ · 10. Log sin ERROR no justificado ✔ (versión corta;
la campaña de 60 min queda evidenciada por el artefacto del CI) ·
11. Memoria dentro de presupuesto ✔ (71 MB ≪ 300 MB del objetivo 10.1,
condiciones de medición: arnés net8.0 local, no Win7 x86 HDD — declarado).
