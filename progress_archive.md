# progress_archive.md — Bitácora COLD (todo lo anterior, comprimido por hito)

## Hito v7.1.0 «OPERADOR» (línea histórica del prototipo, congelada)

- Ciclo completo con CI verde y release v7.1.0-beta.1 (zips x86/x64 + SHA256SUMS).
- Lección permanente: render compuesto requiere `GdiplusStartup` ANTES de crear
  objetos GDI+ (crash demostrado en selftest; fix con arranque perezoso +
  flush por sección en el selftest).
- Lección permanente: en x86 el STL moderno arrastra imports Win8+
  (`std::thread`, `GetSystemTimePreciseAsFileTime`) — usar Win32 puro +
  shim MASM `Win7CompatX86.asm`.
- Detalle: ver mensajes de release/tags v7.x del repo.

## Hito v1.0.0 «REESTRUCTURA» (línea normativa actual)

- Arranque de la línea: reorganización al mapa AGENT.md, versión 1.x.y-beta+z,
  skills instaladas, spec/ normativa, verificación local máxima documentada.
