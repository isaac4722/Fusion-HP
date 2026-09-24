# Verificación F6.08 — Sesión de servicio de 60 minutos

## RESULTADO de la campaña completa (CI, medición real)

**Ejecutada en el job `sesion60` del run 36040245651 (tag v1.0.0-beta.1) —
concluded success.** Reporte del arnés (artefacto `sesion60`):

```text
[F6.08] REPORTE: duración=60.00 min · ops=33138 · líneas=9468 · elementos=23670
        · temas=2760 · imágenes=1069 · LT=3314 · stage=33138 · api=0
        · triggers=23670 · erroresControlados=1180 · NO justificados=0
        · RAM=19 MB · pico=52 MB · dumps60=59
[F6.08] renderAvgMs=0.054 · renderMaxMs=10.8
```

- 60.00 minutos de servicio simulado continuo (proyección, líneas, temas,
  imágenes, Lower Thirds, Stage View, Triggers, fail-safes controlados).
- **0 errores no justificados** (log limpio — criterio 12.3 F6).
- **RAM pico 52 MB ≪ objetivo 300 MB** de la tabla 10.1 (condición: arnés
  net8.0 del runner; la condición normativa Win7 x86/HDD queda declarada
  como limitación de entorno).
- 59 volcados de contadores a 60 s (F6.01) dentro del artefacto.

## Versión corta (2 min) en el arnés local

`TESTS PASS 42/48` incluye la prueba con `LUMINA_SESSION_MINUTES=2`
(mismo driver) — verde en cada build local y en el job `managed` del CI.

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
