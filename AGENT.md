# AGENT.md · Contrato de Trabajo

Aplica para TODO agente (IA o humano). Lee este archivo, `docs/agent/` y la especificación técnica `Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md` antes de empezar.

---

## Comandos Ejecutables (Ejecutar en este orden)

| Tarea | Comando |
|---|---|
| Compilar núcleo nativo x86 | `msbuild src/core/FusionHP.vcxproj /p:Configuration=Release /p:Platform=Win32` |
| Compilar núcleo nativo x64 | `msbuild src/core/FusionHP.vcxproj /p:Configuration=Release /p:Platform=x64` |
| Compilar capa C# (net48) | `dotnet build src/managed/FusionStudio/FusionStudio.csproj -c Release` |
| Compilar variante B (net35) | `dotnet build src/managed/FusionStudio.Lite/FusionStudio.Lite.csproj -c Release` |
| Suite de tests nativos | `msbuild tests/native/CoreTests.vcxproj /p:Configuration=Release /p:Platform=x64 && dist/tests/x64/CoreTests.exe` |
| Suite de tests C# | `dotnet build tests/managed/FusionTests.csproj -c Release && tests/managed/bin/Release/FusionTests.exe` |
| Test de una pieza (C#) | `tests/managed/bin/Release/FusionTests.exe Test<Nombre>` |
| Build instalador dual | `iscc installer/FusionHP.iss` (staging preparado por el CI) |
| Paquete portable | `bash scripts/package_portable.sh x86` |
| **Gate de Calidad** | `bash scripts/quality_gate.sh` (auditoría de prohibiciones + degradación informada) |

---

## Stack y Versión

- **Núcleo nativo:** C++ (Win32/CRT), MSVC toolset compatible con Win7 (`_WIN32_WINNT=0x0601`), enlazado estático `/MT`. Compilación dual obligatoria: **x86 (Win32)** y **x64** [SPEC §3.3]. Las CRT de Visual Studio 2015 soportan Windows 7, Windows 8/8.1 y Windows 10.
- **Capa administrada:** C# (.NET Framework 3.5 SP1 → 4.8, WinForms + WPF). Sin .NET Core/.NET 5+ como runtime obligatorio [SPEC §3.5].
- **Gestor de dependencias C#:** NuGet (paquetes compatibles desde net35; ver `docs/agent/DEPENDENCIAS.md`).
- **Formato de proyecto nativo:** `ahp.v1` (JSON versionado, ZIP opcional con `media/`).
- **Versión del prototipo:** `1.x.y-beta+z` (ver `version.props`). Prohibido cambiarla fuera de este esquema.
- **Prohibiciones tecnológicas:** Java/JRE, .NET Core obligatorio, acceso al Registro de Windows desde cualquier capa, dependencias no incluidas en el paquete [SPEC §3.5, §11.4].

---

## Estructura del Proyecto (Mapa)

- `src/core/`: Núcleo nativo C++ (Win32/CRT). Bootstrap, detección de entorno, render (Direct2D/GDI+), media (DirectShow), gestor de pantallas, IPC puente, log binario. Prohibido: código administrado, dependencias externas no incluidas en `/MT`.
- `src/managed/FusionShared/`: Dominio compartido net35→net48 (`ahp.v1`: Escenario, Elemento, Tema, canciones, biblias, IPC, JSON propio, lector SQLite y Twofish para e-Sword). Sin UI.
- `src/managed/FusionStudio/`: App principal net48 (WinForms + editor WPF vía `ElementHost`): modos Inicio/Estudio/Presentación, biblioteca, API, OBS, Triggers, importadores/exportadores.
- `src/managed/FusionStudio.Lite/`: Variante de perfil B (net35, `LITE`): mismas fuentes, editor funcional WinForms. Prohibido: mocks, cifras inventadas, patrones web.
- `src/bridge/`: Puente de interoperabilidad C++/CLI (opcional, solo punteros de render). Protocolo IPC `ipc.v1` [SPEC §3.4].
- `src/managed/core/`: Dominio compartido (`Escenario`, `Elemento`, `Proyecto`, `Tema`) + sistema de diseño (`Theme.xaml`, tokens UI en `UiTokens.cs`).
- `src/managed/data/` + `src/managed/services/` + `src/managed/state/`: Persistencia (`ahp.v1`, índices bíblicos), servicios de plataforma (red, media, OBS/NDI, Planning Center, Drive) y estado (`provider`).
- `src/managed/features/`: Módulos funcionales (Live, Editor, API, Triggers, Biblioteca, Importadores). Cada uno con su UI, lógica y tests.
- `tests/core.Tests/`, `tests/managed.Tests/`: Tests nativos y administrados. Reflejan la estructura de `src/`.
- `installer/`: Inno Setup dual (instalador x86/x64 + modo portable).
- `docs/` y `docs/agent/`: Documentación con alcance para el agente (ver abajo).
- `.agents/skills/`: Ubicación estándar de skills del agente (ver abajo).
- `PROGRESS.md`, `progress_warm.md`, `progress_archive.md`: Bitácora de tres niveles (ver abajo).
- `spec/`: Documentos fuente (`especificacion-programa-completo.md`, `Requerimientos.md`, `holyrics-spec.md`, `powerpoint-spec.md`).

---

## Límites de Tres Niveles

**Siempre hacer:**
- Ejecutar el gate de calidad (`scripts/quality_gate.sh`) antes de cada commit.
- Compilar **ambas** arquitecturas (x86 y x64) antes de cerrar pieza [SPEC §3.3].
- Verificar que el binario corre en **Win7 x86 sin .NET** (perfil C) cuando la pieza toca el núcleo o el bootstrap [SPEC §4.2].
- Añadir (nunca reescribir) tu entrada en `PROGRESS.md`.
- Usar español (es-VE) en UI, docs y commits (conventional commits).
- Consultar la especificación técnica v1.1 como única fuente de verdad funcional, con trazabilidad a `[SPEC]`, `[REQ]`, `[HOLY]`, `[PPT]`.

**Preguntar primero:**
- Si un test falla y no entiendes la causa raíz tras 2 intentos.
- Antes de añadir una nueva dependencia a `pubspec`/NuGet o al toolset MSVC.
- Si necesitas modificar `src/core/` (afecta al arranque y al perfil C de emergencia).
- Si necesitas modificar `src/managed/core/` (afecta a ambos modos, Live y Creación).
- Si el encargo contradice la especificación técnica o la sección 4.2 (escalera de degradación A/B/C).
- Si el encargo implica tocar el formato `ahp.v1` (requiere análisis de compatibilidad hacia atrás [SPEC §5.3]).

**Nunca hacer:**
- Debilitar tests o relajar reglas de análisis para "ponerse verde".
- Introducir mocks, `localStorage`, `ServiceWorker` o patrones web en `src/managed/`.
- Requerir `regedit`, líneas de comando o permisos elevados para funciones normales [SPEC §11.4].
- Depender de Java/JRE, .NET Core obligatorio o runtimes no incluidos en el paquete [SPEC §3.5].
- Inventar cifras de rendimiento, tasas o datos de usuario.
- Reemplazar el sistema de diseño o la arquitectura dual; solo evolucionar lo existente.
- Commitear tokens, API keys, contraseñas de OBS, credenciales de Planning Center o secretos.
- Escribir en el Registro de Windows desde cualquier capa [SPEC §11.4].
- Inventar contenido en `docs/agent/` o en los `progress*.md`: si falta información, escribe `TODO:`.

---

## Flujo de Trabajo: Máquina de Estados (9.5 Pasos)

El ciclo se ejecuta como una máquina de estados. Cada paso emite un "route" que decide el siguiente.

| Estado | Acción Principal | Route si OK | Route si FALLA |
|---|---|---|---|
| **1. ANALYZE** | Analiza encargo (think ≥1 min).<br>Identifica módulos (Live/Editor/API), secciones de la especificación aplicables y **dirección estética** si toca UI. | `2_PLAN` | `9_BLOCKED` |
| **2. PLAN** | Define el plan de implementación.<br>**Paso obligatorio solo en el primer intento.**<br>En reintentos, se salta a `3_IMPLEMENT`.<br>(Investiga en la web mejores prácticas para implementar la fixture; adapta soluciones que funcionen.) | `3_IMPLEMENT` | `1_ANALYZE` |
| **3. IMPLEMENT** | Aplica lo pedido: 1 pieza por commit.<br>Compila x86 y x64.<br>Usa skills de diseño o instrucciones oficiales si aplica. | `3.5_VISUAL_GATE` | `9_BLOCKED` |
| **3.5 VISUAL GATE** | *(Solo si una función pasa de lógica a full stack o GUI.)*<br>Precompila la app, captura pantallas (Live + Editor + Multiview) y verifícalas contra slop/BASURA con VLM Skill.<br>Borra las capturas al terminar (temp). | `4_AUDIT` | `6_RETRY` |
| **4. AUDIT** | Audita el diff vs encargo.<br>Ejecuta `quality_gate.sh` (analyze C++/C# + test + auditoría de prohibiciones). | `5_CONTROL` | `6_RETRY` |
| **5. CONTROL** | **Gate principal.**<br>Verifica que `quality_gate.sh` pasó, que `PROGRESS.md` está actualizado y que **ambas arquitecturas compilan**. | `7_PERSIST` | `6_RETRY` |
| **6. RETRY** | **Sub-rama de recuperación.**<br>Diagnostica el fallo de `4` o `5` y decide a qué estado volver. | `1_ANALYZE` o `3_IMPLEMENT` | `9_BLOCKED` |
| **7. PERSIST** | Commit (conventional) y push a rama de trabajo. | `8_CI` | `6_RETRY` |
| **8. CI** | Espera a que GitHub Actions esté verde (build x86 + x64 + tests). | `9_CLOSE` | `6_RETRY` (si es de código) o `9_BLOCKED` (si es de entorno) |
| **9. CLOSE / BLOCKED** | Cierra el turno.<br>Registra estado real y rota `PROGRESS.md` → `progress_warm.md` → `progress_archive.md`. | FIN | FIN |

### Sub-rama de Recuperación (RETRY)

Cuando el flujo llega a `6_RETRY`, el agente **no vuelve ciegamente al paso 1**. Diagnostica:

- **Fallo de Análisis/Plan (viene de 1 o 2):** Vuelve a `1_ANALYZE`.
- **Fallo de Implementación (analyze/test fallan):** Vuelve a `3_IMPLEMENT`.
- **Fallo de Arquitectura (una sola plataforma compila):** Vuelve a `3_IMPLEMENT` revisando `docs/agent/ARCHITECTURE.md`.
- **Fallo de Auditoría/Control (el diff no coincide, falta doc):** Vuelve a `3_IMPLEMENT` o `4_AUDIT` según corresponda.
- **Regla de Oro:** En reintentos, el estado `2_PLAN` se salta por defecto. Solo se re-ejecuta si el fallo fue explícitamente por un plan incorrecto.

---

## Bitácora de Tres Niveles (Hot / Warm / Cold)

El registro vive en tres archivos. **Nunca reescribir entradas previas: solo añadir.**

| Archivo | Nivel | Contenido | Límite |
|---|---|---|---|
| `PROGRESS.md` | **Hot** | Solo el turno activo o el último cerrado. | ≤10 líneas |
| `progress_warm.md` | **Warm** | Últimos ~10 turnos cerrados. | ≤10 entradas |
| `progress_archive.md` | **Cold** | Todo lo anterior, comprimido por mes o hito. | Sin límite |

**Formato de línea (obligatorio, una línea por pieza):**
```
YYYY-MM-DD · <estado> · <pieza> · <gate> · <route>
```

**Rotación:**
1. Al cerrar turno: la línea activa de `PROGRESS.md` baja a `progress_warm.md`.
2. Si `progress_warm.md` supera 10 entradas: la más vieja baja a `progress_archive.md` (comprimida en bloque mensual o por hito).
3. Prohibido duplicar información entre los tres archivos.

**Prohibido en los tres archivos:** párrafos, justificaciones, "resumen de lo que hice", prosa.

---

## Integración de Skills (Capacidades del Agente)

Las skills se invocan **dentro** de los estados del flujo. El agente debe leer su `SKILL.md` antes de usarlas.

### Ubicación y Organización Estándar

- **Ubicación canónica:** `.agents/skills/<skill-name>/SKILL.md` (catálogo y procedencia en `.agents/skills/README.md`).
- **Verificación:** Si el repo ya contiene skills, verificar que estén en `.agents/skills/` y no dispersas en otras ubicaciones no estándar. Si hay duplicados, consolidar en la ubicación canónica.
- **Instalación:** `npx skills add <owner/repo> --agent universal --yes` (mantiene `skills-lock.json`). En entornos sin red npm, la vía alternativa probada es clonar y copiar a `.agents/skills/<skill>/`. Instalaciones y ausencias se registran en `PROGRESS.md`.

### Skills de Token-Efficiency (RTK + Caveman)

| Skill | Estado donde se usa | Cuándo Activarla |
|---|---|---|
| **`rtk`** (Rust Token Killer) | Transversal (todo estado que ejecute shell) | Siempre que se ejecuten comandos ruidosos: `rtk msbuild`, `rtk clang-tidy`, `rtk vstest.console`, `rtk git status`, `rtk git diff --stat`, `rtk grep`. **Reduce tokens de entrada.** |
| **`caveman`** | 3_IMPLEMENT, 4_AUDIT, 7_PERSIST (solo output al usuario) | Activar en modo **lite** o **full** para comprimir prosa del agente. **NUNCA para `docs/`, `PROGRESS.md`, `progress_warm.md`, `progress_archive.md`, commits ni documentación.** Solo para respuestas conversacionales. |

**Regla RTK vs Caveman:**
- RTK optimiza **input** (lo que el agente lee).
- Caveman optimiza **output** (lo que el agente dice).
- No son excluyentes; se usan en capas.
- Si una skill de diseño sugiere verbosidad y Caveman sugiere brevedad, **prevalece Caveman para output conversacional**; los `docs/` y commits se escriben en español normal.

### Skills de Diseño y Calidad (Núcleo nativo C++)

| Skill | Estado donde se usa | Cuándo Activarla | Referencia |
|---|---|---|---|
| **`cpp-core-guidelines`** | 3_IMPLEMENT, 4_AUDIT | Siempre que se escriba o revise código C++ en `src/core/`. Enforza RAII, semántica de valores, manejo de errores y concurrencia segura. | Skill basada en C++ Core Guidelines (Stroustrup/Sutter) |
| **`cpp-coding-standards`** | 3_IMPLEMENT, 4_AUDIT | Escribir, revisar o refactorizar código C++. Paquete de instrucciones y convenciones para ejecución consistente. | Basada en C++ Core Guidelines (isocpp.github.io) |
| **`cpp-core-guidelines-review`** | 4_AUDIT | Revisión paralela contra secciones específicas (Functions, Classes, Resource Management). Salida a `review/` con hallazgos consolidados. | Skill de revisión multi-agente |
| **`clang-tidy`** | 4_AUDIT | Análisis estático del núcleo nativo. Detecta violaciones antes del commit. | Integrado en `quality_gate.sh` |

### Skills de Diseño y Calidad (Capa administrada C#/.NET)

| Skill | Estado donde se usa | Cuándo Activarla | Referencia |
|---|---|---|---|
| **`ui-nice-skill`** | 1_ANALYZE, 4_AUDIT | Siempre que el encargo implique crear/modificar pantallas o flujos de UI (Editor WPF, Shell WinForms, Multiview, Stage View). Audita contra el sistema de diseño vigente, no inventa estética. | Skill propia del repo |
| **`wpf-ui-builder`** | 3_IMPLEMENT | Siempre que se implemente UI nueva en el Editor WPF (`src/managed/features/editor/`). Workflow de 8 etapas que transforma requisitos en componentes WPF listos para producción. | Syncfusion WPF UI Builder |
| **`wpf-dotnet-expert`** | 2_PLAN (consulta) | Verificar patrones MVVM, XAML y ecosistema .NET para aplicaciones de escritorio Windows. | Agente especializado en WPF/XAML y MVVM |
| **`win32-native-design`** | 3_IMPLEMENT | Siempre que se implemente UI nueva en el núcleo C++ (pantalla de emergencia, diálogos nativos, borderless). Verificar convenciones Win32 y DPI awareness. | Skill específica del dominio |
| **`desktop-design`** | 2_PLAN (consulta) | Verificar convenciones de escritorio Windows (touch targets ≥32px en modo táctil, breakpoints, DPI awareness). **Solo consulta, no dicta estética.** | Skill de convenciones de plataforma |
| **`humanizer`** | 3_IMPLEMENT (post-UI) | Antes de cerrar `3_IMPLEMENT`, para pulir TODO texto visible al usuario (diálogos de error [SPEC §11.2], etiquetas, tooltips). | Skill de pulido de texto |
| **`vlm`** | 3.5_VISUAL_GATE | Capturas de la app (Live, Editor, Multiview, Stage View) en local; descartar slop/BASURA. **No rediseñar.** | Skill de análisis visual |
| **`taste-skill`** | 4_AUDIT | Opcional. Si la auditoría visual detecta riesgo de "AI slop". | Skill de evaluación estética |
| **`dotnet-accessibility`** (oficial) | 4_AUDIT | Verificar semántica, contraste, `AutomationProperties` y navegación por teclado en la capa C#. | Skill oficial de accesibilidad .NET |
| **`dotnet-testing`** (oficial) | 4_AUDIT | Tests unitarios y de integración en la capa C#. Complementa `quality_gate.sh`. | Skill oficial de testing .NET |

### Skills Específicas del Dominio

| Skill | Estado donde se usa | Cuándo Activarla | Referencia |
|---|---|---|---|
| **`openxml-pptx`** | 3_IMPLEMENT, 4_AUDIT | Siempre que se toque el importador/exportador PPTX. Valida OPC, herencia de 4 niveles, EMUs, centésimas de punto, mitigación XXE/billion laughs [SPEC §9.2]. | Clippit: librería .NET para crear, modificar y convertir PPTX con Open XML SDK. Ofrece CLI scriptable para flujos split/build/verify. |
| **`zefania-xml`** | 3_IMPLEMENT, 4_AUDIT | Siempre que se toque el importador de Biblias (Zefania, `.bib`, JSON). Valida indexación, búsqueda instantánea ≤200 ms [SPEC §9.1]. | Paquete `bible_parser_flutter` para parseo de OSIS, USFX y ZEFANIA XML con enfoque directo y respaldado por base de datos. |
| **`directshow-media`** | 3_IMPLEMENT, 4_AUDIT | Siempre que se toque la reproducción de video en el núcleo C++. Valida carga diferida estricta, fail-safe de video [SPEC §6.4, §6.5]. | Tutorial oficial de Microsoft: clase `DShowPlayer` para audio/video en DirectShow. Nota: MS recomienda Media Foundation para código nuevo. |
| **`ipc-bridge`** | 3_IMPLEMENT, 4_AUDIT | Siempre que se toque el puente C++ ↔ C#. Valida protocolo `ipc.v1`, cola sin bloqueo, latencia ≤16 ms [SPEC §3.4]. | Named Pipes son específicos para IPC en Windows y funcionan en C++ (Win32 API) y C# (`System.IO.Pipes`). Alternativa: C++/CLI como puente. |
| **`performance-budget`** | 4_AUDIT | Al cerrar cualquier pieza que toque render, media o arranque. Verifica contra la tabla 10.1 [SPEC §10.1]. | Skill específica del dominio |
| **`win7-compat`** | 2_PLAN, 4_AUDIT | Siempre que se toque el bootstrap o se añada una API de Windows. Verifica `_WIN32_WINNT=0x0601` y compatibilidad con CRT de VS2015 para Win7. | Skill específica del dominio |

**Regla de No Contradicción:** Si una skill sugiere algo que viola los Límites de Tres Niveles, **prevalece el `AGENT.md`**. Toda skill del contrato debe estar instalada y con carga previa de su `SKILL.md` antes de usarse; si falta, el agente lo registra en `PROGRESS.md` y continúa manualmente aplicando sus principios.

---

## Definición de Hecho (Por Pieza)

Código + Test que lo cubre + `quality_gate.sh` verde + Compilación x86 y x64 verde + Entrada en `PROGRESS.md` + `docs/` actualizado (si cambió comportamiento) + Run de Actions verde + Verificación en Win7 x86 sin .NET si la pieza toca el núcleo [SPEC §4.2, §12.3].

---

## Documentación con Alcance (Progressive Disclosure)

Para mantener este archivo delgado, el detalle vive en `docs/agent/`. El agente **debe consultar** el archivo correspondiente cuando el estado del flujo lo requiera.

| Necesidad | Archivo de Referencia | Cuándo Consultarlo |
|---|---|---|
| Verdad funcional | `spec/Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md` | Estado `1_ANALYZE` (siempre) |
| Especificación base obligatoria | `spec/especificacion-programa-completo.md` [SPEC] | Estado `1_ANALYZE` (siempre) |
| Requerimientos de integración | `spec/Requerimientos.md` [REQ] | Estado `1_ANALYZE` (siempre) |
| Detalle Holyrics | `spec/holyrics-spec.md` [HOLY] | Estado `1_ANALYZE` (si la pieza toca Live, API, Triggers, Biblias) |
| Detalle PowerPoint | `spec/powerpoint-spec.md` [PPT] | Estado `1_ANALYZE` (si la pieza toca Editor, PPTX, herencia de estilos) |
| Dependencias y toolset | `docs/agent/DEPENDENCIAS.md` | Estado `2_PLAN` o `3_IMPLEMENT` (si se toca NuGet, MSVC o Inno Setup) |
| Estilo de código C++ y C# | `docs/agent/CODE_STYLE.md` | Estado `3_IMPLEMENT` (siempre) |
| Arquitectura dual y IPC | `docs/agent/ARCHITECTURE.md` | Estado `2_PLAN` (siempre) |
| Estrategia de Testing | `docs/agent/TESTING.md` | Estado `4_AUDIT` (siempre) |
| Flujo de Git y Commits | `docs/agent/GIT_WORKFLOW.md` | Estado `7_PERSIST` (siempre) |
| Compatibilidad y degradación A/B/C | `docs/agent/COMPATIBILITY.md` | Estado `2_PLAN` o `4_AUDIT` (siempre que toque bootstrap o perfil C) |
| Rendimiento y presupuesto | `docs/agent/PERFORMANCE.md` | Estado `4_AUDIT` (siempre que toque render, media o arranque) |
| Bitácora hot | `PROGRESS.md` | Al inicio y fin de cada turno |
| Bitácora warm | `progress_warm.md` | Al cerrar turno (rotación) |
| Bitácora cold | `progress_archive.md` | Al rotar warm → cold |

**Prohibido inventar contenido** en `docs/agent/` y en los `progress*.md`: si falta información, escribe `TODO:` y sigue.

---

## Criterios de Aceptación del MVP (Recordatorio Vinculante)

Toda pieza debe poder rastrearse hasta uno o más de estos criterios [SPEC §12.3]:

- **F0:** Arranque en Win7 x86 sin .NET (perfil C real); log correcto de SO/arquitectura/perfil.
- **F1:** Transición texto→imagen ≤16 ms sin frame negro; cambio de línea ≤1 frame.
- **F2:** Proyecto x86 abre idéntico en x64; herencia Tema→Elemento verificada con tema en caliente.
- **F3:** Video con bucle/volumen/punto de inicio; fail-safe demostrado con archivo corrupto.
- **F4:** PPTX con herencia de 4 niveles y EMUs importado con informe de fidelidad; `.pptm` sin ejecutar macros; RV1960 con búsqueda ≤200 ms.
- **F5:** Seis endpoints API con token válido y `401` sin él; Trigger `lento` → tema `calma` con acción OBS documentada.
- **F6:** Métricas de tabla 10.1 dentro de objetivo en Win7 x86 4 GB y Win11 x64; ningún diálogo exige `regedit`; log de 60 min sin `ERROR`.
