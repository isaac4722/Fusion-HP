# Fusion HP v4.2.0 — GUI reparada, salida integrada estilo PowerPoint y Modo Operador

**Fusion HP** es un presentador litúrgico híbrido (núcleo nativo C++ + capa C#/.NET Framework) que opera desde **Windows 7 SP1 x86** hasta **Windows 11 x64**, sin Java, sin .NET Core como runtime obligatorio y sin escribir en el Registro de Windows.

## 1. La GUI ya no tiene elementos rotos

Inventario completo de la interfaz auditado y corregido (cada punto con su regresión automatizada o verificación de construcción):

- **Pestañas de la biblioteca alcanzables** — la tira natural de 5 pestañas (Cantos · Biblia · Escenarios · Medios · Temas) medía ≈476 px y el panel 302 px: «Medios» y «Temas» quedaban **fuera del área de clic**. Los chips son ahora adaptativos (se recortan con elipsis + tooltip) y la biblioteca pasa a 340 px.
- **Columna derecha de Presentación completa** — se reorganizó en cuadrícula 2×N (Negro|Logo, Ocultar|Mostrar, Versículo|Mensaje, Acordes|Clasificador, Historial|Operador) con autoscroll: «LÍNEAS DEL ELEMENTO» y «Avance» ya no desaparecen por debajo del borde.
- **Inicio sin cortes** — autoscroll y los «PROYECTOS RECIENTES» se posicionan respecto a la última fila de mosaicos.
- **Biblioteca llena en el primer arranque** — antes Cantos/Biblia/Medios/Temas aparecían VACÍOS si no había proyecto cargado, con las 4 biblias ya instaladas.
- **GUARDAR (Ctrl+S o botón 💾)** — existía el guardado en el código pero **ningún botón lo invocaba**: el trabajo de escenarios se perdía al cerrar.
- **Preview sin líneas solapadas** — el interlineado aplicaba la escala a 1080p DOS veces y el texto quedaba solapado ~60 % en preview, miniaturas y Clasificador; la fórmula es ahora la del núcleo, y la exportación a imagen/PDF usa la misma (antes divergía).
- **Botones deshabilitados visibles** — los iconos en estado disabled eran blancos sobre blanco (invisibles): tinta tenue al 35 % y botones con fondo/borde perceptibles.
- **Editor** — WPF: Ctrl+C/V/Delete vuelven a editar TEXTO (no copiaban/borraban el elemento mientras se escribía) y los arrastres entran al historial (Ctrl+Z los deshace). Lite: «+ Texto/Quitar» se ven al instante (el lienzo no se repintaba) y sin fuga de fuentes.
- **Extras** — pista «Enter» falsa retirada, Acordes ya no recorta «Anglo (C D E)», leyendas del Clasificador visibles, selector de imagen de LOGO en Configuración (el reposo «Logo» era inalcanzable), tooltip único compartido (sin fugas), chips de modo medidos por contenido, preview 16:9.

## 2. La ventana de salida YA NO es un programa aparte (PowerPoint)

Antes: una ventana negra a pantalla completa **permanente** desde el arranque, imposible de cerrar, ajena a la presentación.

Ahora, como PowerPoint:

- La salida **nace oculta** y solo aparece al **«Iniciar presentación»** (botón dedicado o cualquier envío a pantalla — cantos, versículos, medios, escenarios).
- **«Terminar presentación»** o la tecla **Esc** la vuelven a ocultar. La ventana nunca se destruye: mostrar/ocultar es visibilidad pura (cero parpadeo).
- La barra de estado muestra «salida ● activa / ○ oculta» y el estudio nativo (perfil C) alterna con su botón «Proyector».
- La selección de monitor viaja con el **nombre de dispositivo** (`\\.\DISPLAY2`): los índices de WinForms y del núcleo no garantizan el mismo orden y podía proyectar en el monitor equivocado.

## 3. Modo Operador (F8) — consola en vivo dedicada (patrón Holy/PTT)

Ventana separada para quien maneja el culto:

- **EN PANTALLA** grande (el mismo contrato de render que la salida) con título e indicador «línea i/n».
- **SIGUIENTE**: primer render del próximo elemento — nunca más sorpresas.
- **Transporte gigante** SIGUIENTE/Anterior, Negro/Logo/Ocultar a un toque y Mostrar/Terminar presentación.
- **Lista del programa** con búsqueda instantánea y marca del elemento activo; un clic salta.
- Teclado propio (Espacio/←→/↑↓, B/L/C; Esc cierra la consola), reloj grande y estado de la salida.

## 4. Verificaciones

- Compilación completa: FusionShared (net35) + FusionStudio (net48) + FusionStudio.Lite (net35) + FusionTests (net48) — 0 errores.
- Gate de calidad: 0 violaciones. CI: núcleo C++ x86+x64 + tests nativos + tests administrados + verificación Win7 (imports Win8+) + 19 piezas críticas por zip.
- Pruebas nuevas: 5 pestañas caben en 338/220 px, fórmula de interlineado = núcleo, tinta InkFaded al 35 %, transiciones del Modo Operador.

**Instalación**: `FusionHP-4.2.0-setup.exe` (dual x86/x64) o portables (`FusionHP-Portable-x86.zip` universal / `FusionHP-Portable-x64.zip`), con checksums SHA256. Los datos y la configuración se conservan.
