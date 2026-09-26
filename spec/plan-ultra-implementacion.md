# Plan de Ultra Implementación — Aplicación Híbrida Litúrgica y Multimedia

**Fuente normativa única:** `Aplicacion_Hibrida_TechnicalDoc_v1.1_2026-09-23.md`  
**Alcance:** implementación completa del MVP descrito en las Secciones 1–13.  
**Naturaleza del entregable:** prototipo ejecutable de escritorio para Windows, no aplicación web.

> **Restricción explícita del propietario:** no crear, restaurar, regenerar ni modificar `CMakeLists.txt` ni ningún archivo CMake. Este plan no contiene tareas para recuperar o reconstruir CMake. Si una fase necesita compilar y no existe un mecanismo de build permitido, esa fase debe quedar marcada como bloqueada hasta que el propietario proporcione uno; no se sustituye esta decisión por iniciativa propia.

---

## 1. Reglas de ejecución del plan

### 1.1 Fuente de verdad

1. Este plan se deriva exclusivamente del documento técnico v1.1 adjunto.
2. `README.md`, `progress.md`, `docs/roadmap.md` y otros documentos del repositorio no amplían ni reducen este alcance.
3. Cada tarea debe poder rastrearse a una sección del documento.
4. No se añaden arquitecturas, plataformas, productos cloud, códecs, plugins o dependencias no contemplados.
5. No se execute una tarea que recorte una capacidad obligatoria para “ahorrar” RAM, tamaño o complejidad.
6. Cada pieza se entrega con código, pruebas, gate de compilación, auditoría del diff y registro de avance.

### 1.2 Terminología obligatoria

- **Escenario:** colección ordenada de Elementos.
- **Elemento:** unidad mínima proyectable; exactamente cinco tipos en el MVP.
- **Proyecto:** contenedor de Escenarios, tema, pantallas y recursos.
- **Perfil A:** .NET 4.8 o 4.7.2+.
- **Perfil B:** .NET 3.5 SP1–4.6.x.
- **Perfil C:** ausencia de .NET utilizable; núcleo nativo autónomo.
- **Modo Live / Presentación:** modo de arranque por defecto.
- **Modo Creación:** editor WPF incrustado en el shell WinForms.
- **Stage View:** conjunto de hasta tres salidas independientes.

### 1.3 Reglas tecnológicas

1. Núcleo nativo C++/Win32 siempre presente.
2. Capa administrada C# sobre .NET Framework.
3. Prohibido Java/JRE.
4. Prohibido .NET Core/.NET 5+ como runtime obligatorio.
5. El build nativo en MSVC debe conservar `/MT` y compatibilidad Win7.
6. Prohibidos registros escritos por la aplicación, `regedit`, consola o elevación para la operación normal.
7. Prohibido modificar o activar características de Windows sin consentimiento.
8. La UI principal será WinForms; el Editor de Escenarios será WPF embebido mediante `ElementHost`.
9. La comunicación C++↔C# del producto será IPC por pipes con protocolo versionado.
10. No se recreará CMake bajo ninguna circunstancia.

---

# 2. Arquitectura objetivo

```text
Aplicación Híbrida
│
├─ Bootstrap nativo C++
│  ├─ Detección de Windows
│  ├─ Detección de arquitectura
│  ├─ Detección de .NET
│  ├─ Selección de perfil A/B/C
│  ├─ Selección x86/x64
│  ├─ Host de proyección
│  ├─ Pipeline de render
│  ├─ Media DirectShow
│  ├─ IPC ipc.v1
│  ├─ Log binario nativo
│  └─ UI mínima de emergencia
│
├─ Capa C#/.NET Framework
│  ├─ Shell WinForms
│  ├─ Editor WPF mediante ElementHost
│  ├─ Proyecto/Escenarios/Elementos ahp.v1
│  ├─ Biblioteca, temas y recursos
│  ├─ API HttpListener
│  ├─ Triggers
│  ├─ OBS/NDI
│  ├─ Planning Center
│  ├─ Google Drive en F6
│  ├─ JSLib sandbox
│  └─ Diagnóstico
│
└─ Persistencia
   ├─ Proyecto ahp.v1
   ├─ SQLite/FTS para biblioteca y Biblias
   ├─ Configuración JSON
   ├─ Logs
   └─ Recursos media/
```

## 2.1 Frontera nativa/administrada

La API P/Invoke existente no sustituye el IPC normativo. La implementación final debe ofrecer:

- pipes con nombre;
- mensajes binarios con longitud prefijada;
- versión `ipc.v1`;
- cola no bloqueante de comandos hacia render;
- límite de latencia explícito;
- compatibilidad evolutiva sin romper `ipc.v1`;
- operación completa del núcleo si C# no carga.

El hosting C# en proceso es obligatorio para el producto. Un simple `CreateProcess` de otro ejecutable no satisface el diseño. El bootstrap debe mantener la salida nativa aunque la carga administrada falle.

## 2.2 Matriz de compilación

| Objetivo | Arquitectura | Runtime | Resultado requerido |
|---|---:|---|---|
| Perfil A | x86 | .NET 4.8/4.7.2+ | Todas las capacidades del MVP |
| Perfil A | x64 | .NET 4.8/4.7.2+ | Todas las capacidades del MVP |
| Perfil B | x86 | .NET 3.5 SP1–4.6.x | Kernel Live y degradaciones controladas |
| Perfil B | x64 | .NET 3.5 SP1–4.6.x | Kernel Live y degradaciones controladas |
| Perfil C | x86 | Sin .NET | Proyección nativa de emergencia |
| Perfil C | x64 | Sin .NET | Proyección nativa de emergencia |

El lanzador debe detectar el sistema y arquitectura, seleccionar el binario adecuado y permitir forzar la variante mediante un conmutador explícito.

---

# 3. F0 — Núcleo nativo

## F0.01 Detección de sistema operativo

**Requisito:** Secciones 3.1, 4.1 y 4.3.

Implementar, en el núcleo C++ antes de cargar C#:

1. Detección de versión mediante `RtlGetVersion` o `VerifyVersionInfo`.
2. Prohibición de `GetVersion` por ser obsoleta.
3. Clasificación:
   - Windows 7 SP1;
   - Windows 8.1;
   - Windows 10;
   - Windows 11, build 22000 o posterior.
4. Detección de Windows 7 sin SP1.
5. Mensaje de no compatibilidad y sugerencia de instalar SP1.
6. Registro del SO detectado en el log de arranque.

**Criterio:** no se intenta cargar la capa administrada antes de completar esta detección.

## F0.02 Detección de arquitectura

**Requisito:** Secciones 1.5, 3.3 y 4.1.

1. Detectar arquitectura del proceso.
2. Detectar arquitectura nativa del SO.
3. Detectar x86 sobre x64 mediante WOW64.
4. Usar `IsWow64Process2` con alternativa compatible si corresponde al toolset.
5. Seleccionar el paquete x86 o x64.
6. Permitir forzar x86/x64 desde el lanzador.
7. Registrar la selección en el log.

## F0.03 Detección de runtime .NET

**Requisito:** Secciones 3.5, 4.1 y 4.2.

1. Detectar .NET 3.5 SP1 a 4.8 mediante plumbing CLR o una estrategia de solo lectura equivalente.
2. No escribir ni activar el runtime.
3. Clasificar el perfil A, B o C.
4. Detectar 4.7.2+ como perfil A.
5. Detectar 3.5 SP1–4.6.x como perfil B.
6. Detectar ausencia o unusabilidad como perfil C.
7. Si se requiere runtime moderno, ofrecer abrir un instalador offline oficial incluido opcionalmente.
8. No descargar ni instalar .NET automáticamente.
9. Registrar versión, perfil y decisión adoptada.

## F0.04 Perfiles A/B/C

**Requisito:** Sección 4.2.

### Perfil A

- Editor WPF completo.
- API HTTP completa.
- Triggers.
- PPTX completo.
- Planning Center.
- Google Drive cuando F6 esté habilitada.
- JSLib.
- Stage View y Multiview.

### Perfil B

- Motor Live completo.
- Escenarios y Elementos.
- Zefania XML/JSON.
- PDF básico.
- API básica.
- Editor sin efectos acelerados.
- PPTX solo texto/imágenes y aviso de omisiones.
- Sin Planning Center, Drive completa ni funciones avanzadas.

### Perfil C

- Texto, imágenes y video desde núcleo C++.
- Atajos básicos.
- Sesión/proyecto plano empaquetado.
- UI mínima de emergencia.
- Indicación clara de funciones administradas no disponibles.

### Regla común

- Toda función no disponible se deshabilita con mensaje.
- No se permiten fallos silenciosos.
- El perfil activo se muestra en `Ayuda → Estado del sistema`.

## F0.05 Pipe IPC `ipc.v1`

**Requisito:** Sección 3.4.

1. Diseñar mensajes binarios con prefijo de longitud.
2. Versionar el protocolo como `ipc.v1`.
3. Crear servidor y cliente de pipes con nombre.
4. Transportar comandos, estado, carga de proyecto y resultados.
5. Incorporar cola no bloqueante para C#→render.
6. Definir límite de latencia de la cola.
7. Validar mensajes inválidos sin tumbar el núcleo.
8. Conservar proyección y atajos si C# se desconecta.
9. Añadir pruebas de fragmentación, payload grande, desconexión y versión desconocida.

## F0.06 Render base

**Requisito:** Secciones 3.1, 6.3 y 6.4.

1. Implementar ruta Direct2D cuando esté disponible.
2. Implementar fallback GDI+.
3. Implementar double buffering estricto.
4. Aplicar `WS_EX_NOREDIRECTIONBITMAP` donde exista.
5. Mantener una salida persistente y no mostrar el escritorio.
6. Mantener orden de composición:
   - fondo;
   - contenido;
   - Lower Third;
   - indicadores.
7. Medir tiempo de render por frame.
8. Registrar fallos de creación de HWND, DC o bitmap.

## F0.07 Salida borderless

**Requisito:** Secciones 6.1 y 6.5.

1. Crear ventana borderless a pantalla completa.
2. Ocultar cursor y controles.
3. Seleccionar monitor explícitamente.
4. Mantener la ventana de control separada.
5. Permitir minimizar la ventana de control sin cerrar la salida.
6. Añadir bandeja de sistema para la ventana de control.
7. Mantener la salida activa al cambiar de escenario, línea, tema o monitor compatible.

## F0.08 UI mínima de emergencia

**Requisito:** Secciones 3.4, 4.2 y 11.2.

1. Crear executable/ventana nativa mínima.
2. Permitir abrir un proyecto empaquetado.
3. Permitir navegar texto, imagen y video.
4. Permitir Negro, fondo fijo y logo.
5. Mostrar una leyenda clara de modo emergencia.
6. Conservar la proyección si C# no carga o falla completamente.

## F0.09 Logging nativo inicial

**Requisito:** Sección 11.

1. Crear log nativo en formato compatible con la capa C#.
2. Registrar SO, arquitectura, runtime, perfil y decisión de arranque.
3. Usar timestamp UTC y local.
4. Incluir severidad, módulo, mensaje, contexto y call stack de excepciones no controladas.
5. Ubicar según modo instalado/portable.
6. No registrar letras, versículos ni credenciales.

## F0.10 Gate F0

Aplicar exactamente el criterio 12.3 F0:

- ejecutar en Win7 x86 sin .NET;
- arrancar perfil C;
- proyectar texto;
- proyectar imagen;
- comprobar log con SO, arquitectura y perfil;
- repetir para x64 cuando corresponda;
- verificar que la ausencia de C# no interrumpe la salida.

---

# 4. F1 — Motor Live mínimo

## F1.01 Elemento Texto Formateado

**Requisito:** Secciones 5.2 y 6.2.

1. Renderizar texto multi-línea.
2. Aplicar fuente, tamaño, color, alineación y sombra.
3. Conservar el fondo sin modificar al cambiar de línea.
4. Renderizar la línea activa con estilo configurable por tema.

## F1.02 Elemento Imagen

**Requisito:** Secciones 5.2, 6.4 y 7.3.

1. Renderizar JPG, PNG, GIF, BMP y TIF.
2. Implementar ajuste llenar/ajustar.
3. Implementar opacidad.
4. Aplicar recorte.
5. Mantener rutas relativas y recursos empaquetados.
6. Mostrar tema de fondo ante imagen inválida.
7. Registrar ruta y error sin exponer datos del contenido.

## F1.03 Sincronización por línea

**Requisito:** Secciones 5.2, 5.3 y 6.2.

1. Añadir `syncMark` a cada línea.
2. Mantener índice de línea activa.
3. Avanzar y retroceder por línea.
4. Admitir teclado, control físico, móvil y API.
5. Permitir autoavance por `syncMark`.
6. Mantener el fondo y el video intactos al cambiar de línea.
7. Enviar línea activa a Stage View.
8. Conservar IDs estables al navegar.

## F1.04 Cero parpadeo

**Requisito:** Sección 6.3.

1. Mantener la misma ventana entre transiciones.
2. Pre-renderizar el siguiente Elemento en buffer oculto.
3. Intercambiar buffers al activar.
4. Conservar último frame válido.
5. No crear frame negro entre tipos de Elemento.
6. Medir transición real.
7. Añadir prueba de captura a 60 fps.

## F1.05 Atajos

**Requisito:** Secciones 6.5 y 4.4.

Implementar y persistir en JSON:

- flechas;
- Espacio;
- Enter;
- teclas de función;
- favoritos;
- Esc para pantalla de reposo.

## F1.06 Arranque en Presentación

**Requisito:** Sección 6.5.

1. Iniciar en Modo Presentación.
2. Abrir el último proyecto.
3. Dejar Creación como acceso explícito por menú o atajo.
4. No bloquear la salida al entrar/salir de edición.

## F1.07 Búsqueda en caliente

**Requisito:** Sección 6.5.

1. Cuadro de búsqueda visible durante proyección.
2. Búsqueda global de canciones y pasajes.
3. Selección sin interrumpir salida.
4. Atajo de acceso rápido.
5. no cargar la biblioteca completa en memoria para cada búsqueda.

## F1.08 Pantalla de reposo

**Requisito:** Secciones 6.1 y 6.5.

1. Negro.
2. Logo.
3. Fondo fijo.
4. Esc activa reposo, no destruye la salida.
5. Nunca mostrar escritorio.

## F1.09 Gate F1

- Texto→imagen en ≤16 ms sin frame negro.
- Cambio de línea en ≤1 frame.
- Pruebas con captura a 60 fps.
- Verificación de fondo y video estables.
- Pruebas de atajos y reposo.

---

# 5. F2 — Modelo de datos, biblioteca y Modo Creación

## F2.01 Proyecto `ahp.v1`

**Requisito:** Secciones 5.1, 5.3 y 12.4.

Implementar contenedor JSON versionado:

```json
{
  "format": "ahp.v1",
  "project": {
    "name": "Culto Domingo 10am",
    "themeRef": "theme-calma",
    "scenarios": []
  },
  "media": []
}
```

Requisitos:

1. `format` obligatorio.
2. Proyecto con nombre, tema, Escenarios y recursos.
3. Escenarios ordenados.
4. IDs estables.
5. Manifiesto `media/`.
6. Recursos externos opcionales.
7. Empaquetado ZIP cuando corresponda.
8. Campos desconocidos ignorados.
9. Validación de compatibilidad.
10. Mismo contenido x86/x64 sin conversión.

## F2.02 Modelo de Elementos

**Requisito:** Sección 5.2.

El MVP debe exponer exactamente:

1. Texto Formateado.
2. Versículo Bíblico.
3. Imagen.
4. Video.
5. Lower Third.

No se consideran tipos adicionales del modelo persistente. Los formatos de importación se convierten a estos cinco Elementos.

## F2.03 Textos y sincronización

Persistir por línea:

- texto;
- formato;
- `syncMark`;
- estilo;
- estado activo.

## F2.04 Versículos

Persistir:

- libro;
- capítulo;
- versículos;
- traducción;
- palabras destacadas;
- formato de cita.

## F2.05 Imágenes

Persistir:

- ruta relativa;
- ajuste;
- opacidad;
- recorte;
- etiquetas semánticas.

## F2.06 Videos

Persistir:

- ruta;
- volumen inicial;
- `startAt`;
- loop;
- posición;
- etiquetas.

## F2.07 Lower Third

Persistir:

- texto;
- estilo;
- duración;
- posición;
- Elemento superpuesto;
- activación manual o Trigger.

## F2.08 Herencia de estilos

**Requisito:** Sección 5.4.

Implementar cascada exacta:

```text
Tema → Plantilla de Escenario → Escenario → Elemento
```

Reglas:

1. Propiedad nula se resuelve desde el nivel inferior.
2. `null` significa heredar.
3. El tema se puede modificar durante proyección.
4. El cambio se aplica con re-render incremental.
5. La UI muestra el origen efectivo de cada propiedad.
6. El exportador PPTX puede traducir la misma cascada.

## F2.09 Shell WinForms + WPF

**Requisito:** Sección 7.1.

1. Mantener ventana principal WinForms.
2. Incrustar Editor WPF mediante `ElementHost`.
3. Conservar contenido editable completo en perfil B.
4. Desactivar efectos acelerados en perfil B.
5. No usar dos aplicaciones independientes como arquitectura final.

## F2.10 Lienzo vectorial de capas

**Requisito:** Secciones 7.1 y 7.2.

1. Canvas con capas reordenables.
2. Escala.
3. Rotación.
4. Opacidad.
5. Cuadrícula.
6. Guías.
7. Snap configurable.
8. Sin límite artificial de capas.
9. Orden determinista.
10. Previsualización con el mismo motor que la salida.

## F2.11 Gestión de Escenarios

**Requisito:** Sección 7.2.

Operaciones obligatorias:

- crear;
- duplicar;
- reordenar por arrastre;
- agrupar;
- guardar como plantilla.

Jerarquía visible:

```text
Proyecto → Escenarios → Elementos
```

## F2.12 Biblioteca de canciones

- Búsqueda global.
- Anotaciones.
- Historial de uso.
- Frecuencia.
- Popularidad.
- Etiquetas.

## F2.13 Biblioteca de Biblias

- Búsqueda instantánea por cita.
- Búsqueda por palabra.
- Resaltado.
- Tradicciones importadas.
- Índice libro/capítulo/versículo.

## F2.14 Biblioteca de recursos

- Imágenes.
- Videos.
- Etiquetas.
- Arrastrar y soltar desde Explorador.
- Rutas relativas.
- Re-vinculación portable.

## F2.15 Guardado y auto-guardado

- Guardado `ahp.v1`.
- Auto-guardado configurable.
- Guardado atómico.
- Recuperación tras fallo.
- API y Triggers activos durante edición.

## F2.16 Gate F2

1. Crear proyecto en x86.
2. Abrir el mismo contenido en x64.
3. Comparar render bit a bit.
4. Cambiar tema durante salida activa.
5. Verificar herencia en los cuatro niveles.
6. Verificar IDs estables.
7. Verificar `syncMark` persistido.
8. Verificar auto-guardado.

---

# 6. F3 — Multimedia y salidas

## F3.01 Video DirectShow

**Requisito:** Secciones 3.1, 5.2, 6.4 y 6.5.

1. Implementar Filter Graph DirectShow en C++.
2. Reproducir video desde el núcleo.
3. Aplicar volumen inicial.
4. Aplicar `startAt`.
5. Aplicar loop.
6. Mantener posición.
7. Liberar filtros y buffers determinísticamente.
8. Decodificar por bloques cuando el tamaño lo exija.
9. No cargar todos los videos del proyecto.

## F3.02 Fail-safe de video

Ante error:

1. No cerrar la salida.
2. No mostrar negro sin aviso.
3. Sustituir el video por el fondo del tema.
4. Mostrar aviso en monitor del operador.
5. Registrar formato, ruta, error y componente.
6. Mostrar acción sugerida.

## F3.03 Versículo Bíblico

1. Renderizar libro/capítulo/versículos.
2. Traducción.
3. Formato de cita.
4. Palabras destacadas.
5. Colores por tema.
6. Fail-safe si falta versículo.

## F3.04 Lower Third

1. Renderizar en el pipeline nativo.
2. Superposición semitransparente.
3. Posición.
4. Duración.
5. Animación sin frame negro.
6. Activación manual.
7. Activación por Trigger.
8. Persistencia como Elemento.

## F3.05 Stage View

**Requisito:** Secciones 8.5 y 10.3.

Implementar tres salidas:

1. Pantalla Pública.
2. Pantalla de Retorno:
   - letra;
   - línea activa;
   - cronómetro.
3. Pantalla HTML/Instrucciones:
   - notas internas;
   - siguiente contenido;
   - estado operativo.

Usar un render común y buffers compartidos.

## F3.06 Multiview

1. Vista previa con miniaturas en vivo.
2. Preconfigurar las tres pantallas.
3. Activar las tres salidas.
4. Mostrar asignación de monitores.
5. Detectar desconexiones.
6. Permitir volver a activarlas sin reiniciar la proyección.

## F3.07 Carga diferida

1. Solo Elemento activo y siguiente residentes.
2. Liberar Elemento anterior.
3. Precargar posterior.
4. No tocar video al cambiar texto.
5. No precargar Biblias completas.
6. Medir memoria por subsistema.

## F3.08 Gate F3

- Video con loop/volumen/`startAt`.
- Archivo corrupto demuestra fail-safe.
- Lower Third desde Trigger.
- Tres salidas independientes.
- Multiview con miniaturas.
- Una sola búsqueda para las tres vistas.
- Memoria dentro del presupuesto de video.

---

# 7. F4 — Interoperabilidad

## F4.01 Modelo bíblico unificado

**Requisito:** Sección 9.1.

Campos comunes:

- libro canónico;
- alias;
- capítulo;
- versículo;
- traducción;
- texto.

Todos los importadores convergen a este modelo.

## F4.02 Zefania XML

1. Parseo incremental.
2. Soportar archivos de más de 100 MB descomprimidos.
3. Indexar durante importación.
4. Indexar por libro/capítulo/versículo.
5. Leer `<name>`.
6. Leer `<copyright>`.
7. Validar esquema.
8. Informe de errores línea a línea.
9. Probar RV1960 real.

## F4.03 `.bib` e-Sword

1. Interpretar como módulos de Biblia e-Sword.
2. Extraer texto y referencias.
3. Normalizar al modelo común.
4. Informar que notas al pie y Strong se descartan en MVP.
5. Insertar todas las filas en la base.
6. Probar un módulo real.
7. Eliminar el estado actual de “validado pero no insertado”.

## F4.04 JSON bíblico

Aceptar el array:

```json
[
  {
    "reference": "Juan 3:16",
    "text": "..."
  }
]
```

1. Parseo incremental.
2. Normalización de alias.
3. Inserción por lotes.
4. Índice de búsqueda.
5. Resultado de importación detallado.

## F4.05 Importador PPTX/OPC

Recorrido obligatorio:

1. `[Content_Types].xml`.
2. `_rels/`.
3. `docProps/`.
4. `ppt/presentation.xml`.
5. Orden de slides.
6. Relaciones de cada slide.
7. `slideLayouts/`.
8. `slideMasters/`.
9. `theme/`.
10. `media/`.

## F4.06 Herencia PPTX

Resolver cada propiedad visual:

```text
Diapositiva → Diseño → Maestro → Tema
```

Placeholders:

- título;
- subtítulo;
- contenido;
- texto;
- fecha;
- número;
- pie.

## F4.07 Unidades PPTX

1. EMU: `914400 = 1 pulgada`.
2. Convertir coordenadas y dimensiones a unidades del lienzo.
3. Convertir `sz="3200"` a 32 pt.
4. Probar coordenadas negativas, cero y extremas.

## F4.08 Seguridad XML/PPTX

1. Entidades externas deshabilitadas.
2. Resolución XML nula.
3. DTD bloqueado.
4. Límite de expansión de entidades.
5. Protección billion laughs.
6. Límite de ZIP y entradas.
7. Parsing sin cargar archivos completos innecesariamente.
8. Pruebas XXE, entidad recursiva, ZIP corrupto y oversized.

## F4.09 PPTM y namespaces

1. Aceptar `.pptm` sin ejecutar macros.
2. Detectar `vbaProject.bin`.
3. Mostrar aviso explícito.
4. Ignorar namespaces desconocidos.
5. No abortar por extensiones `p14`, `pc2` u otras.

## F4.10 Mapeo PPTX a `ahp.v1`

- Diapositiva→Escenario.
- Caja de texto→Texto Formateado.
- Imagen→Imagen.
- Video incrustado→Video.
- Fondo de diseño→fondo del Escenario.
- Tabla/gráfico no soportado→Elemento omitido con informe.
- Animación/transición no soportada→omitida con informe.

## F4.11 Exportación PPTX

1. OPC completo conforme ISO/IEC-29500.
2. Master, layout y theme.
3. Recursos en `media/`.
4. Coordenadas a EMU.
5. Fuentes a centésimas.
6. Sincronización por línea como una diapositiva por línea.
7. Diálogo de exportación que explique esta regla.
8. Informe de fidelidad.

## F4.12 Exportación PDF

1. PDF etiquetado.
2. Texto seleccionable.
3. Árbol de estructura.
4. Metadatos de accesibilidad.
5. Tema activo.
6. Exportar Escenario o proyecto.
7. Informe de fidelidad.

## F4.13 Exportación de imágenes

1. JPG.
2. PNG.
3. GIF.
4. BMP.
5. TIF.
6. Resolución configurable.
7. Mínimo 1920×1080.
8. Exportar Escenario individual.

## F4.14 Compatibilidad Holyrics

1. Importar biblioteca y listas JSON/XML.
2. Mapear letras a Texto Formateado.
3. Mapear categorías a etiquetas.
4. Importar fondos.
5. Re-vincular rutas relativas.
6. Deduplicar por título y letra normalizada.
7. Confirmación antes de sobrescribir.
8. No modificar biblioteca local sin aceptación.

## F4.15 Informe de fidelidad

Obligatorio en toda importación/exportación:

- unidades convertidas;
- herencia resuelta;
- elementos omitidos;
- efectos no soportados;
- macros detectadas;
- rutas re-vinculadas;
- advertencias;
- resultado final.

## F4.16 Gate F4

1. PPTX real con herencia de cuatro niveles y EMUs.
2. Importación con informe.
3. PPTM sin ejecutar macros.
4. Módulo `.bib` real insertado.
5. RV1960 Zefania importada.
6. Búsqueda RV1960 indexada ≤200 ms.
7. JSON `{reference,text}` importado.
8. PPTX exportado y reabierto.
9. PDF etiquetado validado.
10. Imagen 1920×1080 exportada.
11. Datos Holyrics importados sin sobrescritura no autorizada.

---

# 8. F5 — API, Triggers e integraciones

## F5.01 Servicio API

**Requisito:** Sección 8.1.

1. `HttpListener`.
2. Prefijo `http://+:<puerto>/`.
3. Red local.
4. Apagado por defecto.
5. Activación desde configuración.
6. Puerto personalizado.
7. Token generado automáticamente al activar.
8. Cabecera obligatoria `Authorization: Bearer <token>`.
9. `401` para token ausente o inválido.
10. Registro de cada petición y rechazo.
11. Límite configurable de clientes concurrentes.
12. `503` cuando el servicio no pueda atender.
13. Backoff y reinicio del hilo afectado.

## F5.02 Endpoints obligatorios

| Método | Ruta | Comportamiento |
|---|---|---|
| GET | `/api/v1/state` | Estado completo JSON |
| POST | `/api/v1/next` | Avanzar línea/Elemento según contexto |
| POST | `/api/v1/prev` | Retroceder línea/Elemento |
| POST | `/api/v1/goto` | Ir por ID o índice |
| GET | `/api/v1/text` | Texto plano o JSON con `?format=` |
| POST | `/api/v1/bible` | Proyectar cita estructurada |
| POST | `/api/v1/message` | Mostrar aviso/Lower Third |

## F5.03 Control remoto móvil

1. App móvil o cliente web servido localmente.
2. Funcionamiento sin Internet.
3. Sin nube.
4. Sin cuentas.
5. Next/prev.
6. Goto.
7. Biblia.
8. Mensajes.
9. Volumen.
10. Emparejamiento por IP+token.
11. QR visible en configuración.
12. Token no incluido accidentalmente en logs.

## F5.04 Motor de Triggers

**Requisito:** Sección 8.3.

Modelo obligatorio:

```text
evento → condiciones → acciones
```

Eventos:

- Escenario.
- Elemento.
- Línea.
- Etiqueta semántica.
- Inicio de video.
- Fin de video.
- MIDI.
- Llamada API.
- Horario programado.

Acciones:

- Escena OBS.
- Fuente navegador OBS.
- Audio/video.
- Iluminación MIDI/DMX mediante JSLib.
- Script JavaScript.
- Tema.
- Fondo.
- Mensaje a pantallas.

Reglas:

1. Orden determinista.
2. Orden visible.
3. UI sin código.
4. Import/export JSON.
5. Errores visibles y registrados.
6. Reintento según política definida.
7. Prueba obligatoria `lento → calma → OBS`.

## F5.05 OBS WebSocket

1. IP.
2. Puerto.
3. Contraseña.
4. Handshake.
5. Cambio de escena.
6. Fuentes de navegador.
7. Volúmenes.
8. Reconexión automática.
9. Backoff.
10. Registro de desconexión.
11. Diagnóstico visible.

## F5.06 NDI

1. Stage View como fuente NDI.
2. Selección de servidor.
3. Comutación automática de servidor.
4. Diagnóstico de desconexión.
5. Estado visible.
6. No depender de Internet.

## F5.07 URLs OBS

- `/api/v1/text?format=plain`.
- `/api/v1/text?format=json`.
- Consumo sin scripting adicional.
- Token conforme a la API.

## F5.08 Planning Center

1. Descarga de programas.
2. Descarga de listas.
3. Estructura del planificador.
4. Conversión a Escenarios `ahp.v1`.
5. Manejo de errores en lenguaje humano.
6. Funcionamiento core offline después de importar.
7. Sin exigir sincronización cloud para usar el servicio.

## F5.09 JSLib

1. Motores JavaScript de usuario.
2. Variables.
3. Temporizadores.
4. Triggers propios.
5. Sockets TCP persistentes.
6. WebSocket.
7. HTTP.
8. Integración con sockets de cámara PTZ y bases locales.
9. Sandbox sin disco fuera del proyecto.
10. Límite de CPU.
11. Límite de memoria.
12. Límite de conexiones.
13. Depurador integrado.
14. Errores con archivo/línea.

## F5.10 Diagnóstico integrado

**Requisito:** Sección 11.3.

`Ayuda → Estado del sistema` debe mostrar:

- SO.
- Arquitectura.
- Perfil A/B/C.
- Monitores y destino.
- API.
- Triggers.
- Drive.
- Planning Center.
- OBS.
- NDI.
- JSLib.
- RAM.
- Render.
- Importaciones.
- Cola de render.
- Estado de logs.

Botón `Verificar entorno`:

- render;
- multimedia;
- red local;
- permisos de carpetas;
- API;
- pipes;
- runtime;
- recursos.

Resultado verde/ámbar/rojo por componente.

## F5.11 Gate F5

1. Probar los seis endpoints con token Bearer.
2. Probar `401` sin token.
3. Probar concurrencia.
4. Probar Trigger de etiqueta con acción OBS.
5. Probar QR móvil.
6. Probar Planning Center.
7. Probar reconexión OBS.
8. Probar NDI.
9. Probar sandbox JSLib.
10. Probar diagnóstico integrado.

---

# 9. F6 — Consolidación, rendimiento y distribución

## F6.01 Medición continua

**Requisito:** Sección 10.

Instrumentar:

- RAM por subsistema.
- Working set.
- Memoria pico.
- Tiempo de render por frame.
- Tiempo de importación.
- Tamaño de cola.
- Comandos descartados.
- Latencia de transición.
- Latencia IPC.
- Conexiones API.
- Scripts JSLib.
- Errores.

Volcar contadores cada 60 segundos durante Live.

## F6.02 Objetivos medibles

| Métrica | Objetivo |
|---|---:|
| RAM reposo Live | ≤300 MB |
| RAM pico video 1080p | ≤700 MB |
| Arranque frío | ≤3 s |
| Transición entre Elementos | ≤16 ms |
| Cambio de línea | ≤1 frame |
| PPTX 100 slides | ≤10 s |
| Búsqueda bíblica | ≤200 ms |

Condiciones:

- Win7 x86.
- 4 GB RAM.
- HDD mecánico.
- Salida activa 30 minutos.

## F6.03 Disciplina de memoria

1. Solo Elemento activo y siguiente.
2. Frames de video por bloques.
3. Texturas comprimidas.
4. Liberación determinista.
5. Sin depender del GC para recursos nativos.
6. Cola sin bloqueo.
7. Thread pool limitado.
8. Operaciones de disco/red con prioridad `BelowNormal`.
9. Inicio diferido de API, Triggers, Drive y editor.
10. Un render alimentando tres salidas.

## F6.04 Google Drive

La sincronización bidireccional completa se realiza en F6:

1. Copias de canciones.
2. Biblias.
3. Configuraciones.
4. Proyectos.
5. Modo offline primero.
6. Resolución de conflictos por versión `ahp.v1`.
7. Sin pérdida silenciosa.
8. Historial de conflictos.

## F6.05 Configuración instalada

1. Modo instalado escribe en `%APPDATA%\AppHibrida`.
2. Configuración, logs y datos siguen la ruta normada.
3. Modo portable escribe en la carpeta del programa.
4. Portable no escribe fuera de su carpeta en operación normal.
5. No se usa Registro.

## F6.06 Instalador dual

1. Detectar arquitectura.
2. Instalar solo la variante adecuada.
3. Verificar .NET.
4. Ofrecer instalador offline opcional.
5. No exigir elevación para uso normal.
6. Mantener modo portable equivalente.

## F6.07 Pruebas de plataforma

Ejecutar gates en:

- Win7 SP1 x86.
- Win7 SP1 x64.
- Win8.1 x86/x64.
- Win10 x86/x64.
- Win11 x64.

Validar:

- .NET 3.5 SP1 real.
- .NET 4.8.
- Perfil C.
- Salida nativa.
- Carga C#.
- x86/x64.
- Portable.
- Instalado.
- Tamaño y latencia.

## F6.08 Sesión de 60 minutos

1. Servicio simulado de 60 minutos.
2. Proyección continua.
3. Navegación por líneas.
4. Cambios de tema.
5. Imágenes.
6. Video.
7. Lower Third.
8. Stage View.
9. API y Triggers.
10. Errores controlados.
11. Log sin `ERROR` no justificado.
12. Memoria dentro de presupuesto.
13. Sin frame negro no justificado.
14. Sin cierre silencioso.

## F6.09 Auditoría de prohibiciones

Comprobar automáticamente y manualmente:

- cero Java/JRE;
- cero dependencia .NET moderno obligatorio;
- cero escritura al Registro;
- cero `regedit` requerido;
- cero consola requerida;
- cero elevación para operación normal;
- cero pantalla blanca como respuesta de error;
- cero stack trace crudo como único mensaje;
- cero desactivación del log.

## F6.10 Gate F6

El MVP solo se cierra cuando:

1. Todas las métricas de 10.1 están medidas y dentro de objetivo.
2. Perfiles B y C están verificados.
3. Instalador y portable funcionan.
4. Win7 x86 4 GB y Win11 x64 pasan.
5. Sesión de 60 minutos pasa sin `ERROR`.
6. Ningún diálogo exige `regedit`.
7. El versionado sigue `1.x.y-beta+z`.
8. CI está verde.
9. Los seis gates anteriores están cerrados.

---

# 10. Sistema de logs y diagnóstico

## 10.1 Formato de entrada

```text
timestamp_utc
timestamp_local
severity
module
message
scenario_id
element_id
line_index
file_path
exception
call_stack
```

Severidades:

- `ERROR`
- `WARN`
- `INFO`
- `DEBUG`

En Live, el nivel mínimo es `INFO`. `DEBUG` se activa desde Diagnóstico.

## 10.2 Ubicación

Instalado:

```text
%APPDATA%\AppHibrida\logs
```

Portable:

```text
.\logs
```

## 10.3 Rotación

- Un archivo por día.
- Retención de 14 días.
- Compresión de archivos anteriores.
- Rotación segura.
- Escritura tolerante a fallos de disco.

## 10.4 Privacidad

Nunca incluir:

- letras;
- versículos;
- credenciales;
- tokens;
- contraseñas;
- cuerpos de API sensibles.

Incluir:

- rutas;
- IDs;
- metadatos;
- estado técnico.

## 10.5 Diagnóstico empaquetado

1. Botón `Enviar diagnóstico`.
2. Empaquetar logs y estado.
3. Crear ZIP revisable.
4. Mostrar contenido aproximado antes de enviar.
5. Permitir excluir elementos.

---

# 11. Estrategia de errores

## 11.1 Motor Live

- Captura de excepciones.
- Sustitución del Elemento fallido por fondo de tema.
- Aviso al operador.
- Log técnico.
- Proyección permanece activa.

## 11.2 API/Triggers

- Reinicio del hilo.
- Backoff.
- `503` para trabajo no atendido.
- Log de excepción.
- Estado degradado visible.

## 11.3 Editor

- Fallo aislado.
- Diálogo comprensible.
- Acción sugerida.
- Guardar proyecto antes de reiniciar.
- Botón `Copiar detalles técnicos`.

## 11.4 Fallo grave de C#

- El núcleo conserva la salida.
- Se ofrece perfil C.
- El usuario puede finalizar el servicio sin cerrar la proyección.

---

# 12. Plan de pruebas

## 12.1 Pruebas unitarias

Cobertura mínima:

- Bootstrap SO/arquitectura.
- Clasificación A/B/C.
- IPC `ipc.v1`.
- `ahp.v1`.
- IDs.
- Herencia.
- `syncMark`.
- Render.
- Cachés/lazy loading.
- API y Bearer.
- Triggers.
- OBS.
- JSLib.
- Cada importador.
- Cada exportador.
- Informes de fidelidad.
- Logs y rotación.
- Recuperación de errores.

## 12.2 Pruebas de integración

- C#→IPC→render.
- API→motor.
- Móvil→API.
- Trigger→OBS.
- Trigger→tema.
- JSLib→sockets.
- Importador→SQLite/FTS.
- Escenario→PPTX→reimportación.
- Escenario→PDF.
- Escenario→imágenes.
- Zip portable.

## 12.3 Pruebas de seguridad

- XXE.
- Billion laughs.
- DTD.
- ZIP bombs.
- Paths maliciosos.
- Archivos corruptos.
- PPTM.
- Token ausente/inválido.
- Exceso de clientes.
- Payload API grande.
- JSLib fuera del sandbox.
- Intento de acceso a disco no autorizado.
- CPU/memoria JSLib.

## 12.4 Pruebas de rendimiento

- Arranque.
- RAM reposo.
- RAM video.
- Render frame.
- Cambio de línea.
- Transición.
- Búsqueda bíblica.
- Importación PPTX.
- Importación Biblia.
- Cola IPC.
- Tres salidas.
- 30 minutos.
- 60 minutos.

## 12.5 Pruebas de aceptación por fase

| Gate | Prueba obligatoria |
|---|---|
| F0 | Win7 x86 sin .NET, texto/imagen, log de entorno |
| F1 | 16 ms, un frame, captura 60 fps |
| F2 | x86/x64 idéntico, herencia, tema en caliente |
| F3 | video, fail-safe, Lower Third, tres salidas |
| F4 | PPTX/PPTM, `.bib`, RV1960, informes |
| F5 | seis endpoints, Bearer, tag→OBS |
| F6 | métricas, Win7/Win11, instalador, 60 min |

---

# 13. Orden de ejecución

## Onda 0 — Decisiones irreversibles

1. Confirmar contratos `ahp.v1`.
2. Confirmar framing `ipc.v1`.
3. Confirmar layout x86/x64.
4. Confirmar rutas instalada/portable.
5. Confirmar esquema de logs.
6. Confirmar build permitido sin recrear CMake.

## Onda 1 — F0 y foundations compartidos

Ejecución paralela:

- Bootstrap/detección.
- Perfil C.
- IPC.
- Render base.
- Logging nativo.
- SQLite/FTS.
- Contrato `ahp.v1`.

## Onda 2 — F1 y base F2

Ejecución paralela:

- Motor Live.
- Línea y `syncMark`.
- Imágenes.
- Proyecto y biblioteca.
- Editor WinForms/WPF.
- Herencia.

## Onda 3 — F2 completo y F3

Ejecución paralela:

- Canvas y biblioteca.
- Video DirectShow.
- Lower Third.
- Stage View.
- Multiview.
- Lazy loading.

## Onda 4 — F4

Ejecución por cadenas:

- Biblia Zefania.
- Biblia `.bib`.
- Biblia JSON.
- PPTX import.
- PPTX export.
- PDF.
- Imágenes.
- Holyrics.
- Informes de fidelidad.

## Onda 5 — F5

Ejecución paralela:

- API.
- Móvil/QR.
- Triggers.
- OBS.
- NDI.
- Planning Center.
- JSLib.
- Diagnóstico.

## Onda 6 — F6

- Instrumentación.
- Optimización.
- Drive.
- Instalador.
- Portable.
- Campañas Win7/Win11.
- Sesión 60 minutos.
- Cierre del MVP.

---

# 14. Dependencias críticas

1. No implementar sync de línea sin `syncMark` y estado por línea.
2. No implementar Stage View sin renderer común.
3. No implementar NDI sin Stage View definido.
4. No implementar Trigger por etiquetas sin IDs y contexto de etiquetas.
5. No exportar fidelidad sin un modelo común de importación/exportación.
6. No medir rendimiento sin instrumentación.
7. No cerrar F2 sin paridad x86/x64.
8. No cerrar F4 sin fixtures externos reales.
9. No cerrar F5 sin pruebas de red reales.
10. No cerrar F6 sin las métricas de la tabla 10.1.

---

# 15. Fuera del MVP

No implementar en este plan:

1. Exportación MP4.
2. Coautoría cloud.
3. Complementos Office.js.
4. Linux/macOS.
5. Animaciones PPTX.
6. MP4 como exportación.
7. Funciones que recorten el MVP para cumplir 200 MB.

La sincronización Google Drive completa es posterior según la Sección 12.1 y se ejecuta en F6. El MVP sí incluye importación de Planning Center.

---

# 16. Definición de terminado por tarea

Una tarea no está terminada si solo tiene código.

Cada tarea requiere:

1. Requisito del documento identificado.
2. Implementación real.
3. Test que falle sin la implementación.
4. Build permitido en verde.
5. Tests existentes en verde.
6. Sin errores ni warnings nuevos.
7. Auditoría del diff contra el requisito.
8. Log conforme a la Sección 11.
9. Documentación de comportamiento si cambia.
10. Commit convencional.
11. CI verde.
12. Registro acumulativo en `progress.md`.

---

# 17. Trazabilidad resumida

| Área | Sección MD | Fases |
|---|---:|---|
| Bootstrap, arquitectura, perfiles | 3–4 | F0/F6 |
| Proyecto y Elementos | 5 | F2 |
| Motor Live | 6 | F1/F3 |
| Editor | 7 | F2 |
| API e integraciones | 8 | F5 |
| Interoperabilidad | 9 | F4 |
| Rendimiento | 10 | F1/F2/F3/F6 |
| Errores y diagnóstico | 11 | F0/F5/F6 |
| Aceptación y entrega | 12 | Todas |
| Glosario y trazabilidad | 13 | Transversal |

---

# 18. Criterio final

El Plan de Ultra Implementación se considera aplicado únicamente cuando el código permite demostrar, sin depender de otros documentos, todos los requisitos obligatorios de las Secciones 1–12 y los gates F0–F6. Los tests verdes por sí solos no equivalen a cumplimiento: deben corresponder directamente a los criterios de aceptación del documento técnico v1.1.
