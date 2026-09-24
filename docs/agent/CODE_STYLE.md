# CODE_STYLE.md — Estilo de código C++ y C#

**Norma:** UI y documentación en español es-VE; comentarios de código y tests
en inglés (decisión del propietario, 2026-09). Commits convencionales.

## C++ (núcleo nativo)

- C++17 donde el toolset MSVC compatible con Win7 lo permita; sin dependencias
  externas fuera de `/MT` (SQLite amalgamado y nlohmann/json viven en `third_party/`).
- RAII y semántica de valores (skill `cpp-core-guidelines`); errores por
  códigos de estado cruzando la ABI (jamás excepciones a través del límite C#/C++).
- UTF-8 en toda la frontera; `Utf8.h` como única vía de conversión.
- Cabeceras Win32 con `WIN32_LEAN_AND_MEAN` + `NOMINMAX`; `_WIN32_WINNT=0x0601`.
- Un módulo = un par `.h/.cpp` en `native/core/src/` o `src/core/src/`.
- Sin warnings nuevos (`/W4` MSVC, `-Wall -Wextra -Wpedantic` GCC).

## C# (capa gestionada)

- Multi-target `net35;net48` (+`net8.0` para el arnés de tests), `LangVersion 7.3`,
  nulabilidad desactivada con contratos explícitos (`Directory.Build.props`).
- Guardas de TFM con `LUMINA_NET35` — APIs exclusivas de 4.x siempre detrás de
  guard clause (sin fallo silencioso, SPEC §11.2).
- Espacios de nombres planos `lumina.*` (la carpeta NO define el namespace);
  la organización física sigue el mapa AGENT.md (`core|data|services|state|features`).
- JSON siempre por `MiniJson` (cero dependencias NuGet funcionales).
- Mensajes de usuario en español con la plantilla del skill `humanizer`
  (qué pasó → qué puede hacer → "Copiar detalles técnicos").

## Prohibiciones transversales [SPEC §3.5, §11.4]

Registro de Windows (escritura) · Java/JRE · .NET Core como runtime obligatorio ·
mocks/cifras inventadas en la capa gestionada · `localStorage`/`ServiceWorker` ·
stack traces crudos como única respuesta.
