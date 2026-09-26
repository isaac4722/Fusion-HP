---
name: win32-native-design
description: Diseño de UI nativa Win32 — salida borderless a pantalla completa, UI mínima de emergencia, diálogos nativos y DPI awareness. Usar al implementar UI nueva en el núcleo C++.
---

# win32-native-design — UI nativa del núcleo [SPEC §6.1, F0.07, F0.08]

## Salida borderless

1. Ventana `WS_POPUP` sin bordes a pantalla completa sobre el monitor elegido
   (`EnumDisplayMonitors` + `MonitorFromPoint`).
2. Cursor oculto; sin barras ni controles.
3. `TOPMOST` opcional; el operador nunca pierde el control (la ventana de
   control vive en el monitor principal y puede ir a bandeja sin cerrar la salida).
4. La salida es PERSISTENTE: nunca se destruye entre elementos (cero parpadeo,
   F1.04); `WM_CLOSE` destruye de verdad y apaga el estado del show.

## UI mínima de emergencia (perfil C, F0.08)

1. Ejecutable/ventana nativa mínima autónoma (sin .NET).
2. Abrir proyecto/sesión empaquetada (`GetOpenFileNameW`).
3. Navegar texto, imagen y video; Negro/fondo fijo/logo.
4. Leyenda clara de modo emergencia en pantalla.
5. La proyección no depende de C# — si C# falla, esto sostiene el servicio.

## Convenciones

- DPI awareness declarado en manifiesto; escalado de fuentes coherente.
- Diálogos nativos con mensaje humano + botón "Copiar detalles técnicos"
  [SPEC §11.2]; jamás stack traces crudos.
- GDI+ se inicia con `GdiplusStartup` ANTES de crear objetos GDI+ (lección
  del crash v7.1.0 — arranque perezoso + verificación de `GetLastStatus`).
