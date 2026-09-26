> **ENMIENDA v4.0.0 (26/09/2026):** La **Sección 8 completa (API HTTP, control remoto móvil, Triggers y OBS/NDI) queda REVOCADA** por decisión del usuario: el producto es 100 % local y no abre ningún puerto ni cliente de red. Los criterios F5 de las secciones 12.3 relativos a API/OBS se sustituyen por: «el proceso no abre sockets de escucha ni clientes de red; verificado por prueba automatizada». Ver `docs/agent/DECISIONES_v4.md`.

# Especificación de Prototipo (MVP) — Aplicación Híbrida de Presentación Litúrgica y Multimedia para Windows 7 x86 – Windows 11 x64

**Documento Técnico v1.1 · 23/09/2026 · Equipo de Arquitectura del Proyecto**  
**Control de versiones:** v1.0 primera emisión (13 secciones) · v1.1 los 200 MB pasan a ser ejemplo ilustrativo, sin límite duro (ver Secciones 1.5, 10 y 13)

---

# 1. Introducción y Alcance del Prototipo

## 1.1 Propósito del documento

Este documento define la **especificación de prototipo (MVP)** de la *Aplicación Híbrida de Presentación Litúrgica y Multimedia*, un programa de escritorio nativo para Windows diseñado para operar en el rango completo **Windows 7 x86 (32 bits) hasta Windows 11 x64**. El documento integra de forma normativa cuatro fuentes: la especificación maestra del programa (`especificacion-programa-completo.md`, requisito mínimo de cumplimiento obligatorio), el documento maestro de diseño (`Requerimientos.md`, integración obligatoria), y los análisis de detalle de Holyrics (`holyrics-spec.md`) y de Microsoft PowerPoint (`powerpoint-spec.md`), que aportan los requisitos específicos de cada ecosistema. Su función es servir como referencia única, verificable y trazable para la construcción del prototipo: cualquier decisión técnica posterior debe poder rastrearse hasta una sección de este documento o de sus fuentes.

## 1.2 Naturaleza del entregable: MVP y prototipo de idea

El presente documento describe un **prototipo de idea (MVP conceptual)**, no un programa compilado ni código fuente ejecutable. El objetivo es fijar con precisión de especificación técnica **cómo debe construirse, comportarse y validarse** la aplicación, dejando el MVP como conjunto mínimo de capacidades que demuestre la viabilidad del diseño. La aplicación final se concibe como un producto instalable para PC —**no una aplicación web**— con instalación local, funcionamiento 100 % offline en sus funciones núcleo y capacidad opcional de integraciones en red local (API HTTP, OBS Studio, NDI, control remoto móvil).

El MVP debe cumplir **como mínimo** la totalidad de los requisitos de `especificacion-programa-completo.md`, con la **integración completa** de los principios de `Requerimientos.md` y la incorporación de los detalles funcionales de Holyrics y PowerPoint descritos en sus documentos de especificación respectivos.

## 1.3 Audiencia

El documento está dirigido a tres perfiles: (a) el **arquitecto de software** responsable de la estructura dual C++/C#, (b) los **desarrolladores** de cada módulo (motor de presentación, editor, interoperabilidad, servidor API), y (c) el **responsable de calidad**, que valida el prototipo contra los criterios de aceptación de la Sección 12. Se asume conocimiento de WinForms/WPF, del modelo de objetos de PowerPoint/OpenXML y de los protocolos HTTP/WebSocket.

## 1.4 Prerrequisitos y convenciones

Antes de aplicar esta especificación, el lector debe disponer de los cuatro documentos fuente y conocer los términos del Glosario (Sección 13). Las convenciones aplicables son:

1. **Requisitos normativos**: los verbos *debe* / *deberá* indican requisito obligatorio; *podrá* indica capacidad opcional.
2. **Trazabilidad**: cada sección indica su documento fuente entre corchetes, p. ej. `[SPEC]`, `[REQ]`, `[HOLY]`, `[PPT]`.
3. **Terminología única**: un concepto, una palabra. "Escenario" y "Elemento" se usan exclusivamente con el significado definido en la Sección 5.

## 1.5 Supuestos fundacionales del prototipo

Los siguientes supuestos, derivados de la instrucción del propietario del proyecto, prevalecen sobre cualquier interpretación alternativa de las fuentes:

1. **Escalera .NET (3.5 → 4.8)**: el prototipo es **dual C++/C#**. El núcleo nativo en **C++** (Win32) garantiza arranque, render base y compatibilidad profunda con Win7 x86; la capa de presentación e interoperabilidad en **C#** (.NET Framework) detecta en tiempo de ejecución la versión disponible —**3.5 SP1 como suelo mínimo funcional y 4.8 como objetivo óptimo**— y degrada funciones sin romper si solo existe la versión inferior. Nota: la referencia "3.8" del enunciado no corresponde a ninguna versión publicada de .NET; el rango real viable es 3.5→4.8.
2. **Doble arquitectura**: binarios independientes **x86 y x64**, con detección automática del sistema y selección del binario adecuado (Sección 4).
3. **Formato `.bib`**: se interpreta como módulos de Biblia de e-Sword para el importador (Sección 9.1), junto con Zefania XML y JSON.
4. **Ligereza sin límite funcional duro**: la cifra de **200 MB citada por el propietario es un ejemplo ilustrativo, no una cuota rígida**. El requisito real es cualitativo: la aplicación debe ser **ultra rápida, optimizada y ligera en relación con su funcionalidad completa**; su tamaño instalado puede crecer si ello mantiene íntegras la funcionalidad, la velocidad de arranque y la latencia de transición en equipos de 4 GB de RAM. La ligereza nunca puede usarse como excusa para recortar funcionalidad especificada (Sección 10).

## 1.6 Alcance excluido del MVP

Quedan fuera del prototipo, sin perjuicio de futuras fases: coautoría colaborativa en la nube, complementos tipo Office.js, exportación de video MP4 y soporte para sistemas distintos de Windows. Estos exclusiones se justifican en la hoja de ruta (Sección 12).

El resto del documento desarrolla los fundamentos comparativos (Sección 2), la arquitectura dual (Sección 3) y los tres componentes funcionales (Secciones 6–8), cerrando con interoperabilidad, rendimiento, calidad y hoja de ruta del MVP.

---

# 2. Fundamentos: Síntesis de los Cuatro Documentos Fuente

Esta sección consolida el diagnóstico que justifica el diseño del prototipo. Concluida la definición de alcance (Sección 1), aquí se establece **qué problemas debe resolver** la aplicación y **bajo qué principios**, como base directa de la arquitectura de la Sección 3.

## 2.1 Diagnóstico comparativo de las fuentes

Los documentos fuente describen dos paradigmas maduros pero complementarios. **Holyrics** [HOLY] representa al *presentador en vivo*: agilidad litúrgica, sincronización de letra línea por línea, servidor API local con token, Triggers, integración OBS/NDI, Stage View de tres salidas, control remoto móvil y módulos JavaScript (JSLib). Su debilidad estructural es la dependencia del **JRE 8 exacto** de Java, con errores documentados (Biblia configurada sin texto, fallos de video al cambiar temas, vislumbre del escritorio entre transiciones) y soluciones comunitarias que exigen editar el Registro de Windows. **PowerPoint** [PPT] representa al *creador de contenidos*: diseño jerárquico de cuatro niveles (Tema → Maestro → Diseño → Diapositiva), arquitectura OPC/OpenXML rigurosa, multimedia y accesibilidad avanzadas; a cambio, exige Windows 10/11 en sus versiones modernas, consume 4–8 GB de RAM, arranca lento en hardware modesto y carece de herramientas litúrgicas y de infraestructura de control en vivo. `Requerimientos.md` [REQ] añade la tercera perspectiva: la del usuario que necesita ambos mundos en una sola herramienta estable, ligera y sin dependencias frágiles.

| Aspecto | Holyrics [HOLY] | PowerPoint [PPT] | Prototipo híbrido (este documento) |
| :--- | :--- | :--- | :--- |
| Paradigma | Presentador en vivo | Creador de contenidos | **Doble modo: Live + Creación** |
| Plataforma | Win7–11, pero vía Java 8 | Solo Win10/11 moderno | **Win7 x86 → Win11 x64 nativo** |
| Dependencias | JRE 8 (frágil) | Ninguna externa, pero pesada | **Cero Java; C++/C# autocontenido** |
| Sincronización de letra | Línea por línea (nativa) | No existe | **Línea por línea nativa** |
| Diseño visual | Temas básicos | Herencia 4 niveles completa | **Herencia 4 niveles + temas en caliente** |
| Consumo | Bajo, pero inestable | Alto (4–8 GB) | **Optimizado para 4 GB** |
| Automatización | API + Triggers + OBS/NDI | VBA/COM en escritorio | **API HTTP + Triggers + OBS/NDI nativos** |
| Errores típicos | Parpadeo, Biblia vacía, video | Lentitud, complejidad, regedit | **Log estructurado + mensajes claros** |

## 2.2 Brechas críticas que el prototipo debe cerrar

Del análisis anterior se derivan cinco brechas funcionales que ningún competidor cubre simultáneamente y que definen el valor del MVP:

1. **Unificar ejecución en vivo y creación avanzada** sin sacrificar estabilidad en ninguno de los dos modos [REQ].
2. **Eliminar toda dependencia de Java** y, en general, de runtimes externos no incluidos en el paquete [SPEC].
3. **Interoperar de verdad** con los ecosistemas vecinos: importar Biblias (Zefania XML, `.bib`, JSON) [HOLY], importar archivos `.pptx` respetando OPC y herencia de estilos [PPT], y exportar Escenarios a **PPTX y PDF** [SPEC].
4. **Mantener el control en vivo profesional**: API HTTP con token, Triggers, OBS WebSocket, NDI, Stage View triple, Planning Center y control remoto móvil [HOLY][SPEC].
5. **Operar en el parque de hardware completo** de Win7 a Win11, x86 y x64, sin exigir actualizaciones del sistema ni manipulación del Registro [REQ][SPEC].

## 2.3 Principios de diseño obligatorios

Todos los módulos del prototipo quedan sujetos a estos principios, tomados de las fuentes y elevados a norma:

1. **Eliminación de relleno**: ninguna función sin propósito claro y aplicabilidad práctica en producción real [REQ][HOLY].
2. **Fiabilidad por encima de complejidad**: ante conflicto entre una función nueva y la estabilidad del modo en vivo, gana la estabilidad; las funciones de riesgo se relegan a fases posteriores [REQ].
3. **Cero hacks del sistema**: prohibido requerir `regedit`, líneas de comando o permisos elevados para el funcionamiento normal [REQ][SPEC].
4. **Errores humanos, no técnicos**: registro estructurado (timestamp, módulo, stack) para el técnico y diálogos comprensibles para el operador [SPEC].
5. **Especialización litúrgica como identidad**: el valor diferencial frente a PowerPoint es el flujo de adoración; frente a Holyrics, la solidez y potencia de creación [HOLY][REQ].

Con las brechas y principios fijados, la Sección 3 traduce estos fundamentos en la **arquitectura técnica dual C++/C#** que los hace ejecutables en el rango completo de Windows.

---

# 3. Arquitectura Técnica Dual: Núcleo C++ Nativo + Capa C#/.NET

Esta sección traduce los principios de la Sección 2 en una arquitectura concreta de dos capas —núcleo nativo C++ y capa administrada C#— capaz de operar desde Windows 7 x86 sin actualizaciones hasta Windows 11 x64. La Sección 4 detallará el mecanismo de detección y degradación que hace viable esta estrategia.

## 3.1 Justificación de la dualidad tecnológica

La especificación base [SPEC] exige .NET Framework 4.8 como suelo; el enunciado del propietario añade un requisito más exigente: **el programa debe funcionar incluso si el equipo solo dispone de versiones antiguas de .NET (3.5 SP1) o de un entorno Win7 sin mantenimiento**. La respuesta arquitectónica es una división de responsabilidades por criticidad:

1. **Núcleo C++ (nativo, Win32/CRT)**: todo lo que *no puede fallar* —arranque del proceso, gestor de ventanas de salida, render base por Direct2D/GDI+, reproducción de video por DirectShow, acceso a disco y red de bajo nivel, y el *bootstrap* que detecta el entorno. Compilado con MSVC con toolset compatible con Win7 (`_WIN32_WINNT=0x0601`), sin dependencias del runtime C redistribuible moderno (enlazado estático `/MT`).
2. **Capa C# (.NET Framework, WinForms + WPF)**: todo lo que *gana productividad en desarrollo* —ventanas de control, Editor de Escenarios WPF, modelos de datos, servidor HTTP (`HttpListener`), importadores/exportadores OpenXML, lógica de Triggers y scripting. Compilada con `LangVersion` conservadora y referencias compatibles desde 3.5, con ensamblados satélite opcionales activados solo si el runtime detectado los soporta (Sección 4.2).

Esta división cumple el principio "fiabilidad por encima de complejidad" [REQ]: un fallo o ausencia de .NET moderno degrada funciones, pero nunca impide proyectar.

## 3.2 Diagrama de módulos del sistema

```
+----------------------------------------------------------------------------------+
|                    APLICACIÓN HÍBRIDA DE PRESENTACIÓN (host Win32)               |
+----------------------------------------------------------------------------------+
|  NÚCLEO NATIVO C++ (siempre presente, /MT, x86 y x64)                            |
|   [Bootstrap & Detección] [Render Out (Direct2D/GDI+)] [Media (DirectShow)]      |
|   [Gestor de pantallas] [IPC Puente] [Log binario]                               |
+-------------------------------------+--------------------------------------------+
                                      |  IPC (pipes con nombre / C++/CLI bridge)
+-------------------------------------+--------------------------------------------+
|  CAPA ADMINISTRADA C# (.NET detectado: 3.5 SP1 → 4.8)                            |
|  [Shell WinForms: modos Live/Creación]  [Editor WPF (ElementHost)]               |
|  [Modelo Escenarios/Elementos + JSON]   [Importadores: Zefania/.bib/JSON/PPTX]   |
|  [Exportadores: PPTX/PDF/imágenes]      [Servidor API HttpListener + Triggers]   |
|  [JSLib/scripting] [Planning Center] [Google Drive] [Control remoto móvil]       |
+----------------------------------------------------------------------------------+
```

## 3.3 Estrategia de doble compilación

El prototipo debe distribuirse en dos binarios autocontenidos, ambos obligatorios [SPEC]: **x86** para hardware modesto y compatibilidad total con Win7 32 bits (límite práctico ~2 GB de heap por proceso, suficiente para el MVP), y **x64** para entornos Win10/11 con más de 4 GB de RAM y bibliotecas multimedia mayores. El instalador (o el lanzador portable) selecciona el binario según el sistema operativo detectado; el usuario podrá forzar la otra variante desde un conmutador explícito. Ambas variantes comparten el mismo modelo de datos y el mismo formato de proyecto (Sección 5), de modo que un Escenario creado en x86 debe abrirse sin conversión en x64.

## 3.4 Puente de interoperabilidad C++ ↔ C#

La comunicación entre capas se realiza por **pipes con nombre con protocolo binario simple** (mensajes de longitud prefijada), con un *bridge* C++/CLI opcional para los casos de paso de punteros de render. Reglas obligatorias: (a) el núcleo C++ funciona íntegro aunque la capa C# no cargue —en ese caso se muestra la interfaz mínima de emergencia nativa—; (b) todo comando de la capa C# hacia el render pasa por una cola sin bloqueo con límite de latencia (Sección 10); (c) el protocolo de IPC se versiona (`ipc.v1`) para permitir evolución sin romper compatibilidad.

## 3.5 Restricciones tecnológicas normativas

Queda **prohibido**: cualquier dependencia de Java/JRE [SPEC], el uso de .NET Core/.NET 5+ como runtime obligatorio (podrán existir variantes experimentales separadas, nunca sustitutas) [SPEC], APIs de .NET superiores a la versión detectada sin *guard clause*, y llamadas directas al Registro de Windows desde cualquier capa [REQ]. Queda **recomendado**: DirectX/Direct2D sobre GDI+ puro para el render de salida, y `HttpClient`/`HttpListener` nativos de .NET para la red en la capa administrada [SPEC].

Definidas las capas, la Sección 4 especifica el **sistema de detección de entorno** (SO, arquitectura, versión .NET) y la degradación gradual que el diseño asume.

---

# 4. Compatibilidad Windows 7 x86 – Windows 11: Detección y Degradación Gradual

Sobre la arquitectura dual de la Sección 3, esta sección especifica **cómo el prototipo descubre su entorno de ejecución** (sistema operativo, arquitectura y runtime .NET disponible) y **cómo degrada funciones** sin romper el funcionamiento núcleo. Este mecanismo es el que hace real el requisito "si detecta lo más actual lo usa; si no, igual funciona" del propietario.

## 4.1 Secuencia de arranque y detección (bootstrap)

El arranque sigue una secuencia fija de cinco pasos, implementada íntegramente en el núcleo C++ antes de intentar cargar la capa C#:

```
PASO 1  Detectar SO (RtlGetVersion / VerifyVersionInfo; NUNCA GetVersion, obsoleto)
        → Windows 7 SP1 | 8.1 | 10 | 11 (build >= 22000)
PASO 2  Detectar arquitectura del proceso y del SO (IsWow64Process2)
        → x86 sobre x86 | x64 | x86 sobre x64 (WOW64)
PASO 3  Detectar runtime .NET instalado (clr plumbing:
        netfx 3.5→4.8 vía ICLRRuntimeInfo / detección de claves SHIM, solo lectura)
PASO 4  Elegir estrategia: [A] capa C# completa | [B] capa C# reducida | [C] modo nativo
PASO 5  Cargar capa C# en proceso (host CLR) y establecer IPC; registrar resultado en log
```

La lectura del entorno es **de solo lectura**: el programa no instala, no activa características de Windows ni modifica el Registro [REQ][SPEC]. Si .NET 4.8 no está presente, el programa **no lo descarga en segundo plano sin consentimiento**: ofrece, con un diálogo claro, abrir el instalador offline oficial incluido opcionalmente en el paquete (Sección 4.4).

## 4.2 Escalera de degradación funcional

Cada nivel de runtime habilita un perfil de funciones. La transición entre perfiles es automática y transparente; el log registra el perfil activo y la UI lo muestra en *Ayuda → Estado del sistema*.

| Perfil | Runtime detectado | Capacidades activas | Capacidades degradadas |
| :--- | :--- | :--- | :--- |
| **A — Óptimo** | .NET 4.8 (o 4.7.2+) | Todo el MVP: editor WPF completo, API HTTP, Triggers, importador PPTX completo, Planning Center, Google Drive, JSLib | Ninguna |
| **B — Reducido** | .NET 3.5 SP1–4.6.x | Motor Live completo, Escenarios/Elementos, importador Zefania XML/JSON, exportación PDF básica, API HTTP básica | Editor WPF limitado (sin efectos acelerados); PPTX: importa texto/imágenes, omite efectos; sin Planning Center/GDrive |
| **C — Nativo** | Sin .NET utilizable | Proyección de texto/imágenes/video desde el núcleo C++, atajos de teclado, archivo de sesión plano | Toda la capa administrada; la UI mínima de emergencia nativa permite abrir un Escenario empaquetado |

Regla normativa: **ninguna función del perfil A puede fallar silenciosamente en el perfil B o C**. Toda función dependiente de nivel superior debe verificar el perfil en arranque y deshabilitarse con mensaje explicativo (Sección 11).

## 4.3 Matriz de compatibilidad objetivo

| Combinación | Estado de soporte |
| :--- | :--- |
| Win7 SP1 x86 (4 GB RAM o menos) | Soportado: binario x86, perfil A/B/C según .NET |
| Win7 SP1 x64 | Soportado: binario x86 o x64 a elección |
| Win8.1 / Win10 x86/x64 | Soportado: binario según arquitectura, perfil A |
| Win11 x64 | Soportado y objetivo óptimo: binario x64, perfil A |
| Win7 sin SP1 | No soportado; el bootstrap lo informa y sugiere SP1 |

## 4.4 Distribución: instalador y modo portable

El paquete de distribución comprende: (a) **instalador dual** que instala solo el binario correspondiente a la arquitectura detectada y verifica la presencia de .NET, ofreciendo el instalador offline oficial de Microsoft como componente opcional; y (b) **modo portable** desde USB, alineado con la práctica de Holyrics [HOLY], que ejecuta la aplicación sin escribir fuera de su carpeta. En ambos modos, la configuración del usuario se almacena en archivos JSON dentro de `%APPDATA%\AppHibrida` (instalado) o de la carpeta del programa (portable), nunca en el Registro [REQ].

El resultado de este mecanismo es un sistema que arranca siempre; la Sección 5 define el modelo de datos común que ambas capas y ambas arquitecturas comparten.

---

# 5. Modelo de Datos: Escenarios, Elementos y Formato de Proyecto

Este es el corazón conceptual del prototipo: un modelo de datos propio que **fusiona la "lista de servicio" de Holyrics [HOLY] con la "diapositiva" de PowerPoint [PPT]** en un único par de conceptos —**Escenario** y **Elemento**— definidos por `Requerimientos.md` [REQ] y elevados aquí a norma. Toda la aplicación (motor Live, editor WPF, importadores, exportadores y API) opera exclusivamente sobre este modelo, que es compartido por ambas arquitecturas (x86/x64) y ambos perfiles de runtime (A/B/C de la Sección 4).

## 5.1 Conceptos fundamentales

1. **Elemento**: unidad mínima de contenido proyectable. Existen exactamente cinco tipos en el MVP (Sección 5.2). Un Elemento posee contenido, propiedades visuales locales (opcionalmente heredadas del tema) y metadatos (etiquetas semánticas, notas de operador).
2. **Escenario**: colección ordenada de Elementos que representa un momento del servicio —una alabanza, un pasaje bíblico, un video de anuncio— o una presentación completa. Es el equivalente híbrido de "canción en playlist" [HOLY] y "diapositiva" [PPT].
3. **Proyecto**: documento contenedor con la lista de Escenarios del servicio, el tema activo, la configuración de pantallas y los recursos multimedia referenciados.
4. **Etiqueta semántica**: palabra clave asociable a Elementos, fondos y temas, base del sistema de Triggers (ej.: canción etiquetada `"lento"` activa el tema `"calma"` con fondo `"ocaso"`) [HOLY].

## 5.2 Tipos de Elemento del MVP

| # | Tipo | Contenido | Propiedades clave | Fuente |
| :--- | :--- | :--- | :--- | :--- |
| 1 | **Texto Formateado** | Rich text multi-línea con **marcas de sincronización por línea** | Fuente, tamaño, color, alineación, sombreado; línea activa | [REQ][SPEC] |
| 2 | **Versículo Bíblico** | Cita estructurada (libro, capítulo, versículo(s), traducción) con **resaltado de palabras** | Traducción, formato de cita, palabras destacadas | [REQ][SPEC] |
| 3 | **Imagen** | JPG, PNG, GIF, BMP, TIF | Ajuste (llenar/ajustar), opacidad, etiquetas semánticas | [SPEC][HOLY] |
| 4 | **Video** | Clip con control de reproducción | Volumen inicial, punto de inicio, bucle (`loop`), posición | [SPEC][HOLY] |
| 5 | **Lower Third** | Zócalo inferior semitransparente superpuesto | Texto, estilo, duración, posición sobre otro Elemento | [REQ][SPEC] |

## 5.3 Formato de proyecto nativo (`.ahp` — JSON versionado)

El formato nativo del proyecto es un contenedor **JSON versionado** (`ahp.v1`): legible, comprimible (ZIP con recursos en `media/`), diff-amigable y sincronizable por Google Drive [HOLY]. Ejemplo canónico (parámetros clave comentados):

```json
{
  "format": "ahp.v1",                      // versión de formato; obligatorio, valida compatibilidad
  "project": {
    "name": "Culto Domingo 10am",
    "themeRef": "theme-calma",             // referencia al tema (herencia, Sección 5.4)
    "scenarios": [
      {
        "id": "scn-001",
        "title": "Grande es Tu Fidelidad",
        "tags": ["lento", "adoracion"],    // etiquetas semánticas → Triggers (Sección 8.3)
        "elements": [
          {
            "type": "text",                 // texto formateado con sync por línea
            "lines": [
              { "text": "Grande es tu fidelidad", "syncMark": 0.0 },
              { "text": "Grande es tu bondad",    "syncMark": 12.5 }
            ],
            "styleOverride": null           // null = hereda del tema/escenario
          },
          { "type": "video", "src": "media/intro.mp4", "loop": true, "volume": 30, "startAt": 5.0 }
        ]
      }
    ]
  },
  "media": [ "media/intro.mp4", "media/fondo1.jpg" ]   // manifiesto de recursos externos
}
```

Reglas normativas del formato: (a) los recursos se referencian y se empaquetan opcionalmente, para permitir proyectos ligeros; (b) todo campo nuevo debe ser **ignorable** por versiones anteriores (tolerancia, igual que en OpenXML [PPT]); (c) los IDs son estables entre sesiones para que la API y los Triggers puedan referenciarlos.

## 5.4 Herencia de estilos de cuatro niveles

El prototipo adopta el modelo jerárquico de PowerPoint [PPT][SPEC] adaptado a Escenarios: **Tema → Plantilla de Escenario → Escenario → Elemento**. Una propiedad (color, fuente, fondo) se resuelve desde el nivel más bajo y asciende si es `null`, exactamente como la cascada Tema→Maestro→Diseño→Diapositiva de PresentationML [PPT]. Esta equivalencia es la que permite que el exportador PPTX (Sección 9.3) traduzca el modelo sin pérdida conceptual, y que los temas modificables "en caliente" de Holyrics [HOLY] existan aquí de forma nativa.

El modelo de datos queda así definido; las Secciones 6, 7 y 8 describen los tres componentes que lo consumen: motor en vivo, editor y servidor de automatización.

---

# 6. Componente 1 — Motor de Presentación en Vivo (Live Mode)

El Motor de Presentación es el primer componente funcional [SPEC] y encarna la filosofía Holyrics [HOLY] con la solidez nativa de C++: proyectar **sin demora, sin parpadeo y sin distracciones** durante el servicio. Consume el modelo de datos de la Sección 5 y renderiza directamente desde el núcleo nativo (Sección 3), incluso en el perfil C sin .NET.

## 6.1 Salida limpia a pantalla

La ventana de salida debe ser **pura**: sin barras, bordes, cursor visible ni controles [SPEC]. Requisitos normativos:

1. Ventana *borderless* a pantalla completa sobre el monitor seleccionado, con soporte multi-monitor explícito (público, retorno, HTML — Sección 8.5).
2. **El operador nunca pierde el control**: la ventana de control permanece en el monitor principal y puede reducirse a bandeja sin cerrar la salida.
3. Pantalla de reposo configurable: **negro, logo o fondo fijo** entre elementos —nunca el escritorio [REQ].

## 6.2 Sincronización línea por línea

Función diferencial heredada de Holyrics [HOLY][SPEC]: el texto formateado (Elemento tipo 1) se proyecta y avanza **línea a línea** con la congregación. Normas:

1. Avance/retroceso instantáneo por teclado, control remoto físico, app móvil o API (Sección 8).
2. Cada línea admite `syncMark` de tiempo (Sección 5.3) para auto-avance y para el retorno de escenario (Stage View).
3. El resaltado de la línea activa (color/opacidad/fondo) es configurable por tema.
4. Cambiar de línea **nunca** re-renderiza el fondo: solo cambia la capa de texto (Sección 6.4).

## 6.3 Cero parpadeo (eliminación del "desktop glimpse")

El vislumbre de escritorio entre canción→versículo→video es el defecto más citado de Holyrics [HOLY][REQ]; el prototipo lo elimina por diseño:

1. **Ventanas persistentes**: la salida nunca se destruye entre elementos; se re-compacte la misma ventana.
2. **Composición previa**: el siguiente elemento se renderiza en un buffer oculto (pre-render) mientras el actual está en pantalla; la conmutación es un intercambio de buffers, no una recarga.
3. **Fallback conservador**: en hardware sin Direct2D acelerado, el render GDI+ usa *double buffering* estricto (WS_EX_NOREDIRECTIONBITMAP donde exista).
4. Criterio de aceptación: transición entre **cualquier** par de tipos de elemento ≤ **16 ms** percibidos, sin frame negro intermedio (verificado en Sección 12).

## 6.4 Pipeline de render y multimedia

El pipeline del núcleo C++ procesa capas en orden fijo: **fondo → contenido → superposición (lower third) → indicadores**. La reproducción multimedia usa DirectShow en el núcleo [SPEC]: video con volumen inicial, punto de arranque y bucle definidos por el Elemento (Sección 5.2); imágenes con ajuste y opacidad. Los archivos **no se pre-cargan todos**: aplica la regla de **carga diferida estricta** [SPEC][REQ] — solo el elemento activo y el siguiente se cargan en memoria; al avanzar, se libera el anterior y se precarga el posterior. En el modo Live, cambiar de línea de texto no toca la memoria de video.

## 6.5 Control del operador

1. **Atajos personalizables** [HOLY]: flechas/Espacio/Enter para navegación, teclas de función para elementos marcados como favoritos, `Esc` para pantalla de reposo. La asignación se guarda en la configuración JSON del usuario.
2. **Búsqueda en caliente**: cuadro de búsqueda de canciones/pasajes accesible durante la proyección sin interrumpir la salida (comportamiento Holyrics [HOLY]).
3. **Modo de arranque directo**: la aplicación inicia por defecto en Modo Presentación con el último proyecto abierto [REQ]; el Modo Creación queda a un atajo (Sección 7).
4. **Fail-safe de video**: si un video falla al cargar, la salida muestra el fondo del tema con mensaje en el monitor del operador —nunca congelada ni negra sin aviso (lección de los fallos de video de Holyrics [HOLY][REQ]).

El motor Live garantiza la ejecución impecable del servicio; la Sección 7 describe el entorno de creación donde se prepara todo lo que este motor proyecta.

---

# 7. Componente 2 — Editor de Escenarios (Modo Creación, WPF incrustado en WinForms)

El segundo componente [SPEC] materializa la filosofía PowerPoint [PPT] con la agilidad de Holyrics [HOLY]: un entorno de creación donde se construyen los Escenarios que el Motor Live (Sección 6) proyecta. Su acceso es explícito —menú o tecla de acceso rápido— desde el Modo Presentación, que sigue siendo el arranque por defecto [REQ].

## 7.1 Contenedor híbrido WinForms + WPF

La ventana principal sigue siendo WinForms (estabilidad y bajo consumo [SPEC]); el Editor de Escenarios se incrusta como control WPF vía `ElementHost` [SPEC]. Requisitos normativos:

1. El lienzo WPF es **vectorial y con capas**: cada Elemento (Sección 5.2) es una capa reordenable, con transformaciones de escala, rotación y opacidad, aceleradas por hardware cuando el perfil A lo permite (Sección 4.2).
2. En el perfil B (runtime antiguo), el editor degrada a una vista funcional sin efectos visuales avanzados, conservando edición completa de contenido y estilos (Sección 4.2).
3. La cuadrícula, guías de alineación y ajuste (*snap*) son configurables; no existen límites artificiales de capas, aunque el presupuesto de rendimiento de la Sección 10 es vinculante.

## 7.2 Gestión de Escenarios y biblioteca

El panel izquierdo organiza la estructura jerárquica **Proyecto → Escenarios → Elementos** (Sección 5.1) con operaciones completas: crear, duplicar, reordenar por arrastre, agrupar y guardar como plantilla de escenario. La biblioteca central gestiona:

1. **Canciones**: búsqueda global, anotaciones e historial de uso (frecuencia y popularidad), replicando las utilidades de Holyrics [HOLY].
2. **Biblias**: gestor integrado con **búsqueda instantánea por cita o palabra clave** y resaltado [SPEC]; las traducciones provienen del importador de la Sección 9.1.
3. **Recursos**: imágenes y videos con **etiquetas semánticas** [SPEC][HOLY], importables por **arrastrar y soltar desde el Explorador de Windows** [HOLY].

## 7.3 Edición de los cinco tipos de Elemento

Cada tipo de Elemento dispone de diálogo contextual y panel de propiedades [REQ]:

| Elemento | Capacidades de edición | Dependencia |
| :--- | :--- | :--- |
| Texto Formateado | Rich text (tipografía, color, tamaño, alineación, sombreado), **marcas de sincronización por línea** asignables manualmente o desde audio de referencia | [REQ][SPEC] |
| Versículo Bíblico | Selección de cita, traducción, rango de versículos, **resaltado de palabras clave**, formato de cita en pantalla | [REQ][SPEC] |
| Imagen | Ajuste (llenar/ajustar), opacidad, recorte, etiquetas | [SPEC] |
| Video | Volumen inicial, punto de inicio, bucle, posición | [SPEC][HOLY] |
| Lower Third | Texto, estilo, posición, duración y modo de activación (manual o por Trigger) | [REQ][SPEC] |

## 7.4 Temas en caliente y herencia

El editor expone la herencia de cuatro niveles (Sección 5.4: Tema → Plantilla de Escenario → Escenario → Elemento) con la regla de cascada de PresentationML [PPT]. Cambios normativos:

1. Los temas **son modificables durante la proyección** [HOLY]: cambiar una fuente o color en el tema se refleja en la salida en vivo sin reiniciar el servicio, mediante re-render incremental del pipeline (Sección 6.4).
2. La previsualización en el editor **usa el mismo motor de render que la salida** (no una imitación WPF distinta), garantizando WYSIWYG real —anti-patrón explícito de las discrepancias de PowerPoint [PPT].

## 7.5 Interacción con el Modo Presentación

1. Todo Escenario puede enviarse a la salida con un clic o atajo sin salir del editor (doble modo simultáneo en monitores separados [REQ]).
2. Al guardar, el proyecto se persiste en formato `ahp.v1` (Sección 5.3) con auto-guardado configurable.
3. El editor **no bloquea** funciones del Modo Presentación: la búsqueda en caliente, la API y los Triggers siguen operando mientras se edita (relevante para operadores técnico-litúrgicos en paralelo [HOLY]).

Definidos el motor y el editor, la Sección 8 especifica el tercer componente: la capa de red, automatización e integraciones que convierte al prototipo en plataforma de orquestación.

---

# 8. Componente 3 — Servidor API Local, Triggers e Integraciones

El tercer componente [SPEC] convierte al prototipo de "reproductor de diapositivas" en **plataforma central de orquestación**, replicando y superando el sistema API + Triggers de Holyrics [HOLY] con tecnología nativa .NET [SPEC]. Todos los servicios de red operan en la **capa C#** (Sección 3) y quedan desactivados por defecto, activándose solo desde la configuración.

## 8.1 Servidor API HTTP local

Implementación normativa [SPEC][REQ][HOLY]:

1. Base: `System.Net.HttpListener`, enlazado **exclusivamente a la red local** (`http://+:<puerto>/`); puerto TCP personalizable y **token de acceso obligatorio** (cabecera `Authorization: Bearer <token>`), generado automáticamente en la activación.
2. Comunicación **bidireccional** [HOLY]: la API acepta órdenes y expone el estado actual (elemento activo, línea activa, modo, pantalla de reposo).
3. Endpoints mínimos del MVP:

| Método | Ruta | Función | Fuente |
| :--- | :--- | :--- | :--- |
| GET | `/api/v1/state` | Estado completo en JSON (elemento, línea, tema, flags) | [HOLY] |
| POST | `/api/v1/next` / `/api/v1/prev` | Avanzar/retroceder línea o elemento | [REQ][HOLY] |
| POST | `/api/v1/goto` | Ir a Escenario/Elemento/línea por ID o índice | [REQ] |
| GET | `/api/v1/text` | Texto actual en **texto plano o JSON** (`?format=plain|json`) para OBS | [SPEC][REQ] |
| POST | `/api/v1/bible` | Proyectar cita (libro, cap, versículos, traducción) | [HOLY] |
| POST | `/api/v1/message` | Mostrar aviso/lower third personalizado | [HOLY] |

4. Límite de clientes concurrentes configurable y registro de cada petición en el log estructurado (Sección 11); peticiones sin token válido se rechazan con `401` y quedan registradas.

## 8.2 Control remoto móvil

La app móvil (o el cliente web ligero servido por el propio programa [REQ]) conecta por la red local a la API: cambiar diapositivas, proyectar pasajes bíblicos, enviar avisos y controlar volumen [HOLY][SPEC]. El emparejamiento se realiza por IP+token mostrado como **código QR** en la ventana de configuración. Sin conexión a Internet, sin nube, sin cuentas [REQ].

## 8.3 Motor de Triggers

El motor evalúa continuamente reglas del tipo **evento → condiciones → acciones** [SPEC][HOLY]:

1. **Eventos desencadenantes**: proyección de Escenario/Elemento/línea, detección de **etiqueta semántica** (ej. `lento` activa tema `calma`), inicio/fin de reproducción de video, comandos MIDI recibidos, llamadas API, horario programado.
2. **Acciones disponibles**: cambio de escena en OBS, reproducción de audio/video, activación de iluminación (vía MIDI/DMX por JSLib), ejecución de script JavaScript, cambio de tema/fondo, envío de mensaje a pantallas.
3. Las reglas se definen en UI (sin código) y se exportan/importan como JSON; el orden de evaluación es determinista y visible al operador.

## 8.4 Integración con OBS Studio y NDI

1. **Cliente WebSocket obs-websocket** integrado (IP, puerto, contraseña) para cambiar escenas, activar fuentes de navegador y ajustar volúmenes, con reconexión automática y registro de fallos [HOLY][SPEC].
2. **Salida NDI** del Stage View como fuente de video por red, con conmutación automática de servidor y diagnóstico de desconexiones —mitigando la inestabilidad NDI reportada en Holyrics [HOLY].
3. **URLs locales** para fuentes de navegador de OBS: `/api/v1/text` en texto plano o JSON, consumo directo sin scripting [SPEC][REQ].

## 8.5 Gestión multi-pantalla (Stage View / Multiview)

Hasta **tres salidas simultáneas independientes** [SPEC][HOLY]: (1) *Pantalla Pública* para la congregación; (2) *Pantalla de Retorno* con letra, línea activa y cronómetro para músicos; (3) *Pantalla HTML/Instrucciones* con notas internas del director. La ventana **Multiview** preconfigura las tres telas con miniaturas en vivo antes de activarlas.

## 8.6 Planning Center, Google Drive y extensibilidad JSLib

1. **Planning Center Online**: descarga de programas y listas de canciones desde los servicios programados, respetando la estructura del planificador [HOLY][SPEC]; el resultado se convierte a Escenarios `ahp.v1`.
2. **Google Drive**: respaldo automático y sincronización de canciones, biblias, configuraciones y proyectos entre computadoras [HOLY][SPEC]; modo offline primero, resolución de conflictos por versión de formato (`ahp.v1`).
3. **Módulos JavaScript + JSLib**: ejecución de scripts de usuario con variables, temporizadores y triggers propios; JSLib aporta sockets **TCP persistentes** y WebSocket simplificado para cámaras PTZ, bases de datos locales o APIs externas [HOLY]. El motor JS es sandbox: sin acceso a disco fuera de la carpeta de proyecto, con límites de CPU y depurador integrado.

Con el trío de componentes completo, la Sección 9 aborda la pieza de interoperabilidad más exigente: importar y exportar los formatos de los ecosistemas vecinos (Biblias, JSON, PPTX, PDF).

---

# 9. Interoperabilidad: Importación (Zefania XML, .bib, JSON, PPTX) y Exportación (PPTX, PDF, Imágenes)

Esta sección resuelve el requisito central de compatibilidad del propietario: **cargar e importar los formatos de Holyrics y PowerPoint** (bib, json, pptx y demás) y exportar a los estándares de intercambio. Todos los importadores/exportadores viven en la capa C# (Sección 3), comparten el modelo `ahp.v1` (Sección 5) como destino o origen, y funcionan offline.

## 9.1 Importador de Biblias (Zefania XML, .bib, JSON)

1. **Zefania XML** [HOLY][SPEC]: formato canónico (`bgfdb.de/zefania/`), con soporte para las ediciones del repositorio *Beblia/Holy-Bible-XML-Format* —incluida **Reina Valera 1960** [SPEC]. Requisitos: parseo incremental (las Biblias completas superan 100 MB descomprimidos), indexación por libro/capítulo/versículo para **búsqueda instantánea** [SPEC], detección de metadatos (`<name>`, `<copyright>`) y validación de esquema con informe de errores línea a línea.
2. **`.bib` (módulos e-Sword)**: interpretación acordada en la Sección 1.5. El importador extrae texto plano con referencias, informa las limitaciones de formato (notas al pie y Strong se descartan en el MVP) y normaliza al modelo bíblico interno común.
3. **JSON**: se aceptan volcados de versículos con estructura `{ "reference": "...", "text": "..." }` (arreglo de objetos), útil para migraciones desde apps personalizadas.
4. Normalización final: todas las fuentes convergen al **modelo bíblico interno unificado** (libro canónico + alias de nombres, capítulo, versículo, traducción), consumido por el Elemento tipo 2 (Sección 5.2) y por el endpoint `/api/v1/bible` (Sección 8.1).

## 9.2 Importador PPTX (OpenXML / OPC)

El importador aplica directamente las implicaciones del análisis de PowerPoint [PPT] y los estándares de la especificación [SPEC]:

1. **Apertura del contenedor OPC** (ZIP estándar ECMA-376) y recorrido en orden normativo: `[Content_Types].xml` → `_rels/` (grafo de relaciones, preservar) → `docProps/` (metadatos) → `ppt/presentation.xml` (orden de diapositivas) → `slides/` → `slideLayouts/` → `slideMasters/` → `theme/` → `media/`.
2. **Resolución de herencia de cuatro niveles** [PPT]: cada propiedad visual (color, fuente, tamaño) se resuelve recorriendo Diapositiva → Diseño → Maestro → Tema; los *placeholders* del diseño definen la semántica (título, contenido).
3. **Conversión de unidades**: coordenadas y dimensiones en **EMUs** (914 400 EMU = 1 pulgada) a las unidades del lienzo propio; tamaños de fuente en **centésimas de punto** (`sz="3200"` → 32 pt) [PPT][SPEC].
4. **Seguridad obligatoria**: parsers XML con **entidades externas deshabilitadas** y límites de expansión, mitigando XXE y "billion laughs" [PPT]; los archivos `.pptm` con macros se importan **sin ejecutar** las macros, con aviso explícito.
5. **Tolerancia a namespaces de extensión** (`p14`, `pc2`, etc.): los elementos desconocidos se **ignoran**, nunca abortan la importación [PPT], dado el versionado constante de MS-PPTX (v1.0–v25.0) [PPT].
6. Mapeo al modelo propio: diapositiva → **Escenario**; caja de texto → Elemento Texto; imagen/video incrustado → Elemento correspondiente con extracción a `media/`; el fondo del diseño se traduce a fondo del Escenario. Las animaciones y transiciones **no se importan** en el MVP (Sección 12) y se informan al usuario.

```
PPTX (ZIP) ──Open OPC──► Content_Types ──► _rels ──► presentation.xml
                                                          │ orden de slides
     ▼  para cada slide: slideN.xml ──(herencia 4 niveles)──► resolver estilo
     ▼  EMUs → lienzo propio; sz/100 → pt
     ▼  media/ extraída a media/ del proyecto
   Resultado: Escenarios ahp.v1 + informe de importación (ítems omitidos)
```

## 9.3 Exportación (PPTX, PDF, imágenes)

1. **PPTX** [SPEC]: generación conforme a **ISO/IEC-29500 (PresentationML)** con estructura OPC completa (Sección 9.2 en dirección inversa): `presentation.xml`, `slides/`, layouts/masters mínimos, `theme/` derivado del tema `ahp.v1`, `media/` empaquetada; coordenadas convertidas de vuelta a **EMUs** y fuentes a centésimas de punto. La sincronización por línea se representa como **una diapositiva por línea** (regla MVP, explicitada en el diálogo de exportación).
2. **PDF** [SPEC]: PDF etiquetado (accesible) con estructura de texto seleccionable, usando el tema activo; exportación por Escenario o del proyecto completo [PPT].
3. **Imágenes** [PPT]: exportación de Escenarios individuales a JPG/PNG/GIF/BMP/TIF a resolución configurable (mínimo 1920×1080).
4. Regla común: toda exportación produce un **informe de fidelidad** que lista decisiones de conversión (unidades, herencia, elementos omitidos), siguiendo la transparencia exigida por la tolerancia de formatos [PPT].

## 9.4 Compatibilidad con datos de Holyrics

1. **Canciones**: importación del respaldo/exportación JSON/XML de Holyrics (biblioteca y listas), mapeando letras → Elementos Texto con líneas, y categorías/etiquetas → etiquetas semánticas [HOLY].
2. **Bibliotecas de temas y fondos**: importación de fondos referenciados con re-vinculación de rutas relativas.
3. Regla de origen: los datos importados **nunca sobrescriben** la biblioteca local sin confirmación explícita; la deduplicación se realiza por título+letra normalizada.

La interoperabilidad cerrada, la Sección 10 define el presupuesto de rendimiento que hace viable todo lo anterior en hardware modesto.

---

# 10. Optimización de Rendimiento y Presupuesto de Recursos

El requisito del propietario es explícito: el programa debe ser **ultra rápido, optimizado y ligero, sin por ello ser limitado**. La mención de ~200 MB en el enunciado original fue un **ejemplo ilustrativo, no un límite duro**: si la aplicación crece por encima de esa cifra manteniéndose funcional, rápida y completa, sigue cumpliendo el requisito. Esta sección convierte esa exigencia en **objetivos medibles y presupuesto orientativo por módulo**, vinculantes para todas las decisiones de diseño previas (Secciones 3–9) y verificables por los criterios de la Sección 12.

## 10.1 Objetivos medibles del MVP

| Métrica | Objetivo | Condición de medición | Fuente |
| :--- | :--- | :--- | :--- |
| RAM en reposo (Modo Live, proyecto típico) | ≤ **300 MB** | Win7 x86, 4 GB RAM, salida activa 30 min | [SPEC][REQ] |
| RAM pico con video 1080p activo | ≤ **700 MB** | Ídem, con precarga del siguiente elemento | [SPEC] |
| Arranque en frío hasta control operativo | ≤ **3 s** | HDD mecánico, Win7 x86 | [REQ] |
| Latencia de transición entre elementos | ≤ **16 ms** percibidos, sin frame negro | Cualquier par de tipos de elemento | [SPEC][REQ] |
| Latencia de cambio de línea | ≤ **1 frame** (~16 ms) | Texto sobre fondo estático | [HOLY] |
| Importación PPTX (100 diapositivas, texto+imágenes) | ≤ **10 s** | HDD, perfil A | [PPT] |
| Búsqueda bíblica por palabra | ≤ **200 ms** | Bibliia RV1960 indexada | [SPEC][HOLY] |
| Tamaño instalado completo | **Referencia ilustrativa ~200 MB** (sin límite duro) | Binarios x86+x64 + codecs propios; la prioridad es funcionalidad y velocidad | Propietario (ejemplo) |

## 10.2 Presupuesto de tamaño orientativo

El objetivo de tamaño es **cualitativo y relativo a la funcionalidad**: mantenerse liviano frente a suites equivalentes, sin cuota máxima rígida. Como referencia de orden de magnitud —partiendo del ejemplo de 200 MB— el paquete se reparte así: binarios C++ (x86+x64, `/MT`) ≈ 15 MB; capa C# y librerías (OpenXML, JSON, WebSocket) ≈ 30 MB; motor de multimedia y codecs propios (DirectShow filters, sin paquete de codecs de terceros) ≈ 25 MB; motor JavaScript + JSLib ≈ 10 MB; recursos de UI (iconos, plantillas de tema, fuentes embebidas básicas) ≈ 15 MB; instalador offline opcional de .NET 4.8 **excluido** del cómputo (componente separado opcional). Las Biblias y los proyectos **no** forman parte del paquete base: se importan por la Sección 9.1 y se almacenan en la carpeta de datos. Si una fase posterior añade funciones y el paquete crece por encima de la referencia, se acepta siempre que no se degraden las métricas de la tabla 10.1 ni se recorte funcionalidad.

## 10.3 Estrategias de optimización vinculantes

1. **Carga diferida estricta** [SPEC][REQ]: memoria residente solo del elemento activo y del siguiente (Sección 6.4); las Biblias se acceden por índice en disco, no por carga completa.
2. **Un render, tres consumos**: el pipeline del núcleo (Sección 6.4) alimenta salida pública, retorno y Multiview desde los mismos buffers, evitando renders duplicados (Sección 8.5).
3. **Disciplina del heap x86**: en 32 bits, el límite práctico de ~2 GB de espacio de direcciones obliga a: decodificación de video por bloques (sin frames completos en RAM), texturas comprimidas en GPU y liberación determinística (sin depender del GC para recursos nativos).
4. **Concurrencia acotada**: una cola de render sin bloqueo (Sección 3.4) y *thread pool* limitado; ninguna operación de red o disco (importaciones, Drive, API) puede competir con el hilo de render durante la proyección — se ejecutan con prioridad `BelowNormal`.
5. **Inicio diferido de servicios**: la red (API, Triggers, Drive) y el editor WPF se inicializan **bajo demanda**; el arranque del Modo Live no carga la capa de creación [REQ].
6. **Medición continua**: contadores internos (RAM por subsistema, ms de render por frame, tiempos de importación) visibles en una ventana de diagnóstico y volcados al log estructurado (Sección 11), para que cada build sea medible contra la tabla 10.1.

## 10.4 Límite de la ligereza

La ligereza **no autoriza a recortar funcionalidad especificada** [Propietario]: cuando una función (p. ej. el importador PPTX completo) exija más memoria de la disponible en x86 (Sección 4.2), la respuesta correcta es procesamiento por *streaming* y degradación controlada —nunca la eliminación de la función ni la exigencia de más hardware. La tensión entre tamaño y funcionalidad se resuelve **a favor de la funcionalidad y el rendimiento**: el tamaño instalado es el resultado optimizado del diseño, no una restricción de partida.

Cerrado el presupuesto de recursos, la Sección 11 define cómo el sistema falla de forma controlada y transparente: diagnóstico, errores y registro.

---

# 11. Diagnóstico, Gestión de Errores y Registro Estructurado

Las fuentes coinciden en que el manejo de errores es un diferenciador competitivo: Holyrics falla sin explicación y sus soluciones comunitarias exigen `regedit` [HOLY]; PowerPoint produce errores de configuración que obligan a editar el Registro [PPT][REQ]. El prototipo invierte ese patrón como norma [SPEC]: **fallar de forma controlada, explicar en lenguaje humano y registrar con precisión técnica**.

## 11.1 Registro estructurado (log)

Requisitos normativos del log [SPEC]:

1. **Contenido por entrada**: timestamp UTC+local, severidad, módulo emisor (`core.render`, `cs.api`, `import.pptx`, …), mensaje, contexto clave (IDs de Escenario/Elemento, archivo) y *call stack* completo en excepciones no controladas.
2. **Ubicación y rotación**: `%APPDATA%\AppHibrida\logs` (instalado) o `.\logs` (portable), un archivo por día, rotación a 14 días, compresión de los anteriores. Nunca se escribe en el Registro de Windows [REQ].
3. **Niveles**: `ERROR`, `WARN`, `INFO`, `DEBUG`; en operación en vivo el nivel mínimo es `INFO` y los contadores de la Sección 10.3 se vuelcan cada 60 s. El nivel `DEBUG` se activa desde la ventana de diagnóstico, no por archivos de configuración manual.
4. **Capa nativa incluida**: el núcleo C++ registra en el mismo formato (log binario con prefijo de módulo convertido a texto por la capa C# cuando existe; en modo nativo, a archivo plano).
5. **Privacidad**: el log nunca contiene contenido de letras, versículos ni credenciales; solo rutas y metadatos. La opción "enviar diagnóstico" empaqueta logs + estado del sistema en un ZIP revisable por el usuario antes de enviarlo.

## 11.2 Gestión de excepciones y degradación controlada

1. **Excepciones no controladas**: capturadas en ambos dominios (SEH en C++; `AppDomain.UnhandledException` y `DispatcherUnhandledException` en C#) [SPEC]. Política por componente:
   - **Motor Live / render**: nunca se abandona la salida; el elemento que falla se sustituye por el fondo del tema con aviso en el monitor del operador (Sección 6.5) y el incidente se registra.
   - **Servidor API / Triggers**: el hilo afectado se reinicia con *backoff*; las peticiones en curso reciben `503` con mensaje claro.
   - **Editor**: fallo aislado por diálogo con opción de guardar el proyecto antes de reiniciar el módulo.
2. **Guard clauses obligatorias**: toda función del perfil A verificada como no disponible en el perfil B/C se deshabilita en arranque con mensaje explicativo (Sección 4.2) —prohibido el fallo silencioso—.
3. **Diálogos al usuario**: título concreto, qué ocurrió en lenguaje no técnico, qué puede hacer el usuario (acción sugerida) y botón "Copiar detalles técnicos" para soporte [REQ][SPEC]. Ejemplo: "No se pudo abrir el video `intro.mp4` (formato no soportado). Puedes convertirlo a MP4/H.264 o elegir otro archivo." — con el detalle técnico en el portapapeles, no en pantalla.
4. **Diálogo de fallo grave**: si la capa C# completa falla, el núcleo C++ ofrece el modo de emergencia nativo (Sección 4.2, perfil C) para finalizar el servicio sin cerrar la proyección.

## 11.3 Ventana de diagnóstico y verificación del entorno

Accesible desde *Ayuda → Estado del sistema*, muestra en una sola vista: SO, arquitectura, perfil de runtime activo (A/B/C, Sección 4.2), monitores detectados con su uso asignado, estado de API/Triggers/Drive/Planning Center, contadores de rendimiento en vivo (Sección 10.3) y el botón "Verificar entorno" que ejecuta un autotest (render, multimedia, red local, permisos de carpetas) con resultado verde/ámbar/rojo por componente. Esta ventana es la primera línea de soporte y evita las configuraciones opacas que afectan a Holyrics y PowerPoint [HOLY][PPT].

## 11.4 Prohibiciones explícitas

Queda prohibido [REQ][SPEC]: requerir edición del Registro, líneas de comando o permisos elevados para funciones normales; mostrar *stack traces* crudos como única respuesta al usuario; ocultar errores con pantallas en blanco o cierres silenciosos; y desactivar el registro para "ganar rendimiento" (el costo del log, por diseño, es < 1 % del presupuesto de la Sección 10).

Definido el comportamiento ante el fallo, resta delimitar el alcance del MVP en fases verificables: hoja de ruta, prioridades y criterios de aceptación (Sección 12).

---

# 12. Hoja de Ruta del MVP: Fases, Prioridades y Criterios de Aceptación

Las Secciones 3–11 definieron el *cómo*; esta define el **orden y el alcance verificable** de la construcción. El MVP es el conjunto mínimo de capacidades que demuestra la viabilidad completa del diseño (arranque dual, proyección estable, creación, interoperabilidad y automatización), sin incluir las funciones postergadas de la Sección 1.6.

## 12.1 Alcance del MVP — dentro y fuera

**Dentro del MVP** (obligatorio): arranque dual x86/x64 con detección (Sección 4); los cinco tipos de Elemento y el formato `ahp.v1` (Sección 5); Motor Live completo (Sección 6); Editor de Escenarios funcional (Sección 7); servidor API con los seis endpoints, Triggers básicos, OBS WebSocket, Stage View de 3 salidas y control remoto móvil (Sección 8); importadores Zefania XML / .bib / JSON / PPTX y exportadores PPTX / PDF / imágenes (Sección 9); presupuesto de recursos (Sección 10) y sistema de diagnóstico (Sección 11).

**Fuera del MVP** (fases posteriores, con justificación): exportación MP4 (alto costo de códec, sin valor litúrgico inmediato [REQ]); coautoría colaborativa en nube (depende de backend ajeno [PPT]); complementos tipo Office.js (no aplicables al ecosistema propio [PPT]); soporte Linux/macOS (fuera del ecosistema Windows definido [REQ]); importación de animaciones PPTX (complejidad PresentationML elevada, valor marginal en proyección [PPT]); sincronización Google Drive y Planning Center **completos** — el MVP incluye la importación de Planning Center, pero la sincronización bidireccional de Drive queda en Fase 6 (riesgo de red, no bloquea el servicio offline [REQ]).

## 12.2 Fases de construcción

| Fase | Entregable | Dependencia |
| :--- | :--- | :--- |
| **F0 — Núcleo nativo** | Bootstrap, detección de entorno (4.1), render base Direct2D/GDI+, salida borderless, IPC | Ninguna |
| **F1 — Motor Live mínimo** | Texto/imagen en pantalla, sincronización por línea, cero parpadeo, atajos, pantalla de reposo | F0 |
| **F2 — Modelo de datos y proyectos** | `ahp.v1`, biblioteca de canciones, temas con herencia 4 niveles, Modo Creación WPF básico | F1 |
| **F3 — Multimedia y elementos completos** | Video (DirectShow), Versículo Bíblico, Lower Third, retorno y Multiview | F2 |
| **F4 — Interoperabilidad** | Importadores Zefania/.bib/JSON/PPTX; exportadores PPTX/PDF/imágenes; importación Holyrics | F2 |
| **F5 — Automatización** | API HTTP con token (6 endpoints), Triggers, OBS WebSocket, control remoto móvil, diagnóstico (Sección 11) | F3 |
| **F6 — Consolidación del MVP** | Rendimiento medido contra 10.1, perfil B/C verificados, instalador + portable, cierre de criterios | F4+F5 |

## 12.3 Criterios de aceptación verificables

Cada fase se cierra solo si sus criterios pasan; los criterios de F6 son los del MVP completo:

1. **F0**: en Win7 x86 **sin .NET**, la app arranca y proyecta texto/imágenes (perfil C real); el log registra SO/arquitectura/perfil correctos.
2. **F1**: transición texto→imagen ≤ 16 ms sin frame negro (captura de video a 60 fps como evidencia); cambio de línea ≤ 1 frame.
3. **F2**: un proyecto creado en x86 abre idéntico en x64 (bit a bit del contenido renderizado); herencia Tema→Elemento verificada cambiando el tema con la salida activa (tema en caliente, Sección 7.4).
4. **F3**: video con bucle, volumen y punto de inicio conforme a 5.2; fail-safe de video demostrado con archivo corrupto (Sección 6.5).
5. **F4**: un `.pptx` de prueba con herencia de 4 niveles y EMUs se importa con informe de fidelidad; un `.pptm` se importa sin ejecutar macros; RV1960 en Zefania importa con búsqueda ≤ 200 ms (10.1).
6. **F5**: los seis endpoints responden con token válido y rechazan `401` sin él; un Trigger de etiqueta (`lento` → tema `calma`) ejecuta acción OBS documentada.
7. **F6**: todas las métricas de la tabla 10.1 medidas y dentro de objetivo en Win7 x86 4 GB y Win11 x64; ningún diálogo exige `regedit` (auditoría de 11.4); log completo de una sesión de servicio simulado de 60 min sin ERROR.

## 12.4 Gobernanza y validación del ciclo de desarrollo

Se adopta el ciclo de trabajo de la especificación [SPEC], adaptado al stack dual (sin Flutter, que era específico del documento AGENT.md original): (1) analizar encargo; (2) consultar documentación; (3) aplicar una pieza por commit; (4) auditar *diff* contra el encargo; (5) **gates de calidad**: compilación limpia x86 y x64, tests unitarios de la capa C# en verde y pruebas de humo del núcleo; (6) documentar en `progress.md` con entrada acumulativa por plantilla [SPEC]; (7) persistir con *commits* convencionales; (8) CI verde; (9) cerrar tarea. La versión del prototipo sigue el esquema `1.x.y-beta+z` del formato de proyecto `ahp.v1`.

Con el alcance cerrado, la Sección 13 consolida la terminología única y la trazabilidad completa hacia los documentos fuente.

---

# 13. Glosario, Trazabilidad y Referencias

Cierre del documento: unifica la terminología usada en las Secciones 1–12, garantiza la trazabilidad de cada requisito hacia su documento fuente y lista las referencias. Su propósito es que cualquier lector —o agente de desarrollo posterior— pueda interpretar el documento sin ambigüedad (convención 3 de la Sección 1.4).

## 13.1 Glosario de terminología única

| Término | Definición normativa | Sección |
| :--- | :--- | :--- |
| **Aplicación Híbrida** | El programa de escritorio especificado en este documento | 1.1 |
| **Escenario** | Colección ordenada de Elementos que representa un momento del servicio o una presentación completa | 5.1 |
| **Elemento** | Unidad mínima de contenido proyectable; existen exactamente cinco tipos | 5.1, 5.2 |
| **Proyecto** | Documento contenedor de Escenarios, tema activo, pantallas y recursos; formato `ahp.v1` (JSON versionado) | 5.1, 5.3 |
| **Tema** | Nivel 1 de la herencia de estilos (Tema → Plantilla → Escenario → Elemento) | 5.4 |
| **Modo Live / Modo Presentación** | Modo de arranque por defecto orientado a proyección en tiempo real | 6 |
| **Modo Creación** | Modo de edición con lienzo WPF incrustado en WinForms | 7 |
| **Sincronización línea por línea** | Avance granular de texto proyectado, diferencial frente a PowerPoint | 6.2 |
| **Desktop glimpse** | Vislumbre de escritorio en transiciones; defecto eliminado por diseño | 6.3 |
| **Carga diferida** | Residencia en memoria solo del elemento activo y del siguiente | 6.4, 10.3 |
| **Perfil A/B/C** | Niveles de capacidad según runtime .NET detectado (4.8 / 3.5–4.6 / sin .NET) | 4.2 |
| **Escalera .NET** | Política de detección y degradación de runtime (3.5 SP1 → 4.8) | 1.5, 4.2 |
| **Etiqueta semántica** | Palabra clave asociable a Elementos/fondos/temas, usada por Triggers | 5.1, 8.3 |
| **Trigger** | Regla automática evento → condiciones → acciones | 8.3 |
| **Stage View / Multiview** | Configuración de hasta tres salidas independientes (pública, retorno, HTML) | 8.5 |
| **Lower Third** | Zócalo inferior semitransparente superpuesto | 5.2, 7.3 |
| **OPC** | Open Packaging Convention; contenedor ZIP de archivos OpenXML (`.pptx`) | 9.2 |
| **EMU** | English Metric Unit; 914 400 EMU = 1 pulgada; unidad de PresentationML | 9.2 |
| **Herencia de 4 niveles** | Cascada de PresentationML: Tema → Maestro → Diseño → Diapositiva (y su equivalente propio) | 5.4, 9.2 |
| **Informe de fidelidad** | Reporte de decisiones de conversión de toda importación/exportación | 9.3 |
| **Núcleo nativo** | Capa C++ (Win32, `/MT`) responsable de lo que no puede fallar | 3.1 |
| **Capa administrada** | Capa C#/.NET responsable de UI, interoperabilidad y automatización | 3.1 |

## 13.2 Matriz de trazabilidad requisito → fuente

| Área de requisito | Fuente(s) | Secciones que la implementan |
| :--- | :--- | :--- |
| Tres componentes (Live, Editor, Servidor API) | [SPEC] | 6, 7, 8 |
| Dualidad de modos y filosofía sin relleno | [REQ] | 2.3, 6, 7 |
| Cero Java, .NET Framework, sin Registro | [SPEC][REQ] | 3.1, 3.5, 4.4, 11.4 |
| Doble compilación x86/x64 con autodetección | [SPEC] + propietario | 3.3, 4 |
| API HTTP con token, endpoints texto/JSON | [SPEC][REQ][HOLY] | 8.1 |
| Triggers y etiquetas semánticas | [SPEC][HOLY] | 5.1, 8.3 |
| OBS WebSocket, NDI, URLs locales | [SPEC][HOLY] | 8.4 |
| Stage View de 3 pantallas | [SPEC][HOLY] | 8.5 |
| Planning Center, Google Drive, JSLib | [SPEC][HOLY] | 8.6 |
| Biblias Zefania XML, RV1960, búsqueda instantánea | [SPEC][HOLY] | 9.1 |
| Importación PPTX: OPC, EMUs, herencia 4 niveles, XXE | [PPT][SPEC] | 9.2 |
| Exportación PPTX/PDF/imágenes | [SPEC][PPT] | 9.3 |
| Sincronización línea por línea | [SPEC][HOLY] | 5.2, 6.2 |
| Carga diferida y 4 GB RAM | [SPEC][REQ] | 6.4, 10 |
| Log estructurado y mensajes no técnicos | [SPEC][REQ] | 11 |
| Ligereza sin límite funcional duro (200 MB = ejemplo ilustrativo) | Propietario | 1.5, 10.1, 10.2, 10.4 |
| C++ + C# con degradación 3.5→4.8 | Propietario | 1.5, 3, 4.2 |

## 13.3 Referencias

1. `especificacion-programa-completo.md` — *Especificación Técnica y Funcional Definitiva: Programa Híbrido de Presentación Litúrgica y Multimedia* [SPEC]. Documento base de cumplimiento mínimo obligatorio.
2. `Requerimientos.md` — *Más Allá de PowerPoint y Holyrics: Diseño de un Prompt para una Aplicación de Presentación Ligera, Robusta y Compatible con Windows 7* [REQ]. Documento de integración obligatoria.
3. `holyrics-spec.md` — *Más Allá de las Letras: Cómo las Funciones Únicas de Holyrics Optimizan el Flujo de Trabajo de la Adoración* [HOLY]. Fuente de detalle del ecosistema Holyrics.
4. `powerpoint-spec.md` — *Más Allá de las Diapositivas: Un Mapeo Exhaustivo de la Arquitectura, Automatización y Colaboración de PowerPoint* [PPT]. Fuente de detalle del ecosistema PowerPoint/OpenXML.
5. Instrucción del propietario del proyecto (2026-09) — restricciones de plataforma (PC nativo, Win7 x86–Win11), arquitectura dual C++/C# con escalera .NET 3.5→4.8, interoperabilidad con formatos de Holyrics y PowerPoint, y ligereza como objetivo cualitativo (los 200 MB citados constituyen un ejemplo ilustrativo, no una cuota rígida).

Este documento, junto con las referencias listadas, constituye la especificación de prototipo completa y trazable de la Aplicación Híbrida de Presentación; toda evolución posterior debe registrarse según el ciclo de la Sección 12.4 sin reescribir la trazabilidad aquí establecida.
