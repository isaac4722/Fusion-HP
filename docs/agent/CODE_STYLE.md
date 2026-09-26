# CODE_STYLE.md — estilo de código (resumen vinculante)

- **Idioma**: español (es-VE) en UI, mensajes, commits y documentación [AGENT.md].
- **C++ (src/core)**: C++17, RAII, sin excepciones en el camino crítico del render,
  prefijo de namespaces `fusion`. Formato Allman. Comentarios con referencia
  normativa [SPEC §x]. MSVC `/W3 /utf-8 /permissive-` (`ConformanceMode`).
- **C# (src/managed)**: C# 7.3 máximo (debe compilar en net35 y net48). Sin
  interpolación de cadenas cuando se pueda, sin `dynamic`, sin APIs post-3.5 en
  FusionShared. `null` = "hereda" en estilos [SPEC §5.4].
- **Errores**: mensajes humanos + acción sugerida; el detalle técnico al log
  [SPEC §11.2.3]. Prohibido mostrar stack traces crudos como única respuesta.
- **Log**: `timestamp | utc | SEVERIDAD | módulo | mensaje`; nunca contenido de
  letras/versículos ni credenciales [SPEC §11.1.5].
- **Tests**: arnés propio (`tests/`), nombre `Test*`, verificación por código de
  salida. Un test por criterio verificable de [SPEC §12.3] siempre que sea posible.
