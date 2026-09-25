# TESTING.md — estrategia de pruebas

## Arnés nativo (`tests/native/CoreTests.vcxproj`)
Consola auto-hospedada: JSON, contrato ipc.v1 (parseo de Slide), sesión ahp.v1
del perfil C con herencia, bucle IPC real (pipe con nombre + hello/respuesta),
detección de entorno. Corre en x86 y x64.

## Arnés administrado (`tests/managed/FusionTests.csproj`, net48)
- JSON propio: round-trip, escapes, números, tolerancia.
- Modelo ahp.v1: persistencia, IDs estables, herencia 4 niveles, tema en caliente.
- Twofish-128: vectores ECB oficiales I=1, I=2, I=3 del cifrado.
- Lector SQLite puro + importador e-Sword completo (fixture cifrado real:
  blobs Twofish+SQLitePlus+zlib generados con el código de referencia).
- Zefania XML, TSV legado, JSON de himnario, búsqueda ≤ 200 ms (criterio F4).
- API HTTP: 6 endpoints con token, 401 sin token (criterio F5), goto/message.
- Triggers: etiqueta "lento" → tema "calma" (criterio F5) y acción OBS.
- Exportadores: PPTX (ZIP válido + reimportable), PDF (cabecera/EOF), PNG 1080p.

## En CI
- Núcleo y tests nativos se compilan y ejecutan para Win32 y x64.
- La capa gestionada compila net35 + net48 y corre los tests en net48.
- El gate de calidad audita prohibiciones en cada push.

## Fuera de alcance del sandbox
Pruebas visuales (60 fps de transición) y rendimiento en Win7 real: se
documentan procedimientos de verificación manual en docs/verification/.
