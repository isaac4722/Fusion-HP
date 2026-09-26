---
name: humanizer
description: Pulir todo texto visible al usuario (diálogos de error, etiquetas, tooltips) antes de cerrar 3_IMPLEMENT — lenguaje no técnico, acción sugerida, sin jerga.
---

# humanizer — Texto visible al operador [SPEC §11.2]

Pasar por esta skill TODO texto que el usuario vea, justo antes de cerrar
`3_IMPLEMENT` de una pieza con UI.

## Plantilla de diálogo de error (normativa)

1. **Título concreto**: qué módulo falló ("No se pudo abrir el video").
2. **Qué ocurrió**, en lenguaje no técnico (sin stack, sin HRESULT crudo).
3. **Qué puede hacer el usuario**: acción sugerida única y realista.
4. **Botón "Copiar detalles técnicos"**: el detalle va al portapapeles,
   nunca en pantalla.

## Checklist de pulido

- [ ] ¿Se entiende sin saber programar?
- [ ] ¿Propone una acción útil (no "Error inesperado" a secas)?
- [ ] ¿Español es-VE consistente con el resto de la UI?
- [ ] ¿El detalle técnico está disponible SIN ensuciar la pantalla?
- [ ] ¿Nunca exige regedit/consola/elevación como remedio? (prohibición 11.4)

## Prohibido

Stack traces como única respuesta · pantallas en blanco · cierres
silenciosos · mensajes que culpan al usuario.
