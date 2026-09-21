# DirectorWindow — Pantalla 3 «Director / Instrucciones»

## 1. Class Overview

`DirectorWindow` materializa la **tercera salida de pantalla** del Stage View multiview de la especificación (§3.3): *«Pantalla HTML / Instrucciones: mensajes y notas internas para el director del servicio»*. Junto a la salida de audiencia y al Stage View para músicos, completa el modelo de hasta tres salidas independientes desde una misma interfaz.

Es un `QWidget` de pantalla completa sin marco, de contraste alto y tipografía dimensionada para lectura a ~1,5 m (tabla de tipografía para paneles/HMI: mínimo 20 px; disciplinas de la skill qt-ui-design). Muestra: reloj y temporizador, ítem actual con su texto, notas del ítem, el SIGUIENTE ítem de la cola con preview y un **registro persistente de los últimos 4 mensajes** del operador (a diferencia de las alertas del Stage View, no expiran). La contraparte web (`/director.html` + `/api/director.json` vía `WebServer`) ofrece la misma pantalla en cualquier navegador de la LAN.

## 2. Project Structure and Dependencies

- **Incluido por**: `gui/MainWindow.h/.cpp` (creación, selector de pantalla, ciclo de vida).
- **Requiere**: Qt5 Widgets. Sin dependencias de terceros.
- **Coordina con**: `MainWindow::updateDirector()` (estado), `WebServer::setDirectorState()/setDirectorCountdown()` (espejo web), `CommsPanel::messageSent` y alertas (mensajes).

## 3. Public API

| Método | Tipo | Descripción |
|---|---|---|
| `DirectorWindow(QWidget *parent)` | constructor | Ventana frameless oscura; reloj con refresco de 1 s. |
| `updateInfo(item, slideText, nextItem, nextSlideText, notes)` | `void` | Estado completo (lo publica MainWindow al cambiar de slide/ítem). |
| `clearInfo()` | `void` | Reinicia todos los campos. |
| `addMessage(text, title)` | `void` | Añade al registro persistente (últimos 4, con hora). |
| `startCountdown(minutes)` / `stopCountdown()` | `void` | Temporizador espejo del Stage View (UTC, DST-estable). |
| `applyAccent(color)` | `void` | Color de acento del título (coherencia con el tema activo). |

## 4. Layout (de arriba a abajo)

| Zona | Contenido | Estilo |
|---|---|---|
| Barra superior | Reloj (`hh:mm:ss`) + temporizador/`¡TIEMPO!` | 24 pt dorado / 22 pt rojo |
| Ítem actual | Título del ítem de la cola | 26 pt azul claro, negrita |
| Texto | Líneas de la slide actual | 17 pt blanco |
| Notas | Notas del ítem (si existen) | 14 pt gris cursiva |
| Panel SIGUIENTE | Título del siguiente ítem + preview de su slide | Caja `#1A2230` con radio 8 px |
| Mensajes | Historial de mensajes del operador (últimos 4, persistentes) | 14 pt dorado |

Máximo 3–4 tamaños tipográficos visibles por zona y color + texto como doble pista de estado (WCAG 2.2 — nunca color solo).

## 5. Notas de diseño

- **Mensajes persistentes**: el director no puede perder un aviso porque parpadee; quedan en el historial con marca horaria.
- **Cuenta atrás en UTC** (`currentDateTimeUtc()`), inmune a cambios DST (regla DEP-11 de qt-cpp-review).
- **App zombie**: `closeEvent` de MainWindow la oculta junto a las demás salidas top-level.
- **Estado web espejo**: `MainWindow::updateDirector()` publica los mismos campos a `WebServer`, de modo que `/director.html` y la ventana nativa permanecen sincronizados.

## 6. Usage Example

```cpp
// MainWindow (extracto): publicación del estado al cambiar de slide
void MainWindow::updateDirector()
{
    const int row = m_queueList ? m_queueList->currentRow() : -1;
    QString curItem, nextItem;
    if (row >= 0 && row < m_queueData.size())
        curItem = m_queueData.at(row).label;
    if (row + 1 < m_queueData.size())
        nextItem = m_queueData.at(row + 1).label;
    // … texto/notas de la slide actual …
    if (m_directorOn && m_director->isVisible())
        m_director->updateInfo(curItem, curText, nextItem, nextText, notes);
    // espejo web: /api/director.json
    m_ctx.web->setDirectorState(dir);
}
```
