# JsEngine / JsLibHost — Extensibilidad JavaScript (JSLib)

## 1. Class Overview

LuminaPresentation Suite es una aplicación híbrida de presentación litúrgica y multimedia. Dentro de su arquitectura, `JsEngine` y su clase auxiliar `JsLibHost` dan soporte al requisito de **extensibilidad JavaScript / JSLib** de la especificación (§3.3): permitir que el usuario automatice e integre el programa con otros sistemas (mezcladores, OBS, servicios web) mediante módulos `.js` cargados en caliente, sin recompilar ni instalar nada.

`JsEngine` es el orquestador: posee un `QJSEngine` (módulo Qt Qml) y un `JsLibHost`, carga los módulos del directorio `<datos>/modules/` y expone a C++ los puntos de entrada `fireEvent()` y `callFunction()`. `JsLibHost` es el objeto `jslib` visible desde JavaScript: un `QObject` con métodos `Q_INVOKABLE` para sockets persistentes, HTTP, temporizadores y suscripción a eventos. Los callbacks JS se guardan como `QJSValue` y se invocan desde las señales C++ — todo en el hilo principal (los sockets Qt requieren su event loop).

## 2. Project Structure and Dependencies

- **Incluido por**: `main.cpp` (creación y ciclo de vida), `core/Triggers.h` (retransmisión de eventos), `gui/CommsPanel.cpp` (UI de gestión).
- **Referenciado por** `AppContext` mediante el puntero `ctx.js`.
- **Requiere**: Qt5 Core, Network, WebSockets y **Qml** (QJSEngine). Componente de CMake: `Qt5::Qml`; en el paquete portable se despliega `Qt5Qml.dll`.
- **Depende de**: `Database` (solo indirectamente, para conocer el directorio de datos vía ajustes).

## 3. Public API — JsEngine

| Método | Tipo | Descripción |
|---|---|---|
| `JsEngine(QObject *parent)` | constructor | Crea motor + host (`recreate()`); no carga módulos. |
| `~JsEngine()` | destructor | Suelta callbacks ANTES de destruir el motor (orden seguro). |
| `host()` | `JsLibHost *` | Acceso al host (registro de logs para la GUI). |
| `loadedModules()` | `QStringList` | Nombres de archivo de los módulos cargados. |
| `errors()` | `QStringList` | Errores de sintaxis con `archivo:línea — mensaje`. |
| `loadModules(dataDir)` | `bool` | Fija el directorio y (re)carga; `false` si algún módulo falló (el válido sigue cargado). |
| `reload()` | `bool` | Recrea motor + host (cierra sockets persistentes) y recarga. |
| `fireEvent(name, data)` | `void` | Puente de eventos de triggers → callbacks `onEvent`. |
| `callFunction(name, data)` | `bool` | Acción de trigger «script»: llama a una función global con el payload. |

**Señal**: `modulesChanged()` — se emite al terminar una carga/recarga.

## 4. Public API — JsLibHost (el objeto `jslib` de JS)

| Método Q_INVOKABLE | Descripción |
|---|---|
| `log(msg)` | Registro visible en Comunicación → Módulos JS (ring de 60 líneas). |
| `notify(title, text)` | Registro destacado + señal `jsNotify`. |
| `httpGet(url, cb)` | GET con timeout de 15 s; `cb(status, body)`. |
| `tcpConnect(id, host, port)` / `tcpSend(id, text)` / `tcpClose(id)` | Socket TCP persistente identificado por `id`. |
| `onTcpMessage(id, cb)` | Callback por datos recibidos (`cb(texto)`). |
| `wsConnect(id, url)` / `wsSend(id, text)` / `wsClose(id)` | WebSocket persistente. |
| `onWsMessage(id, cb)` | Callback por mensajes de texto entrantes. |
| `onEvent(name, cb)` | Suscripción a eventos del presentador (`slide_next`, `media_play`, `alert`, …). |
| `setTimeout(ms, cb)` / `clearTimeout(id)` | Temporizadores de un disparo; devuelven/usan id numérico. |

## 5. Notas de diseño y ciclo de vida

- **Orden de destrucción**: los `QJSValue` de los callbacks referencian al motor; `~JsEngine` llama `disconnectAll()` (suelta callbacks y cierra sockets/timers) antes de eliminar el `QJSEngine`, evitando accesos a un motor muerto.
- **Recarga**: `reload()` destruye host y motor; los sockets persistentes se cierran de forma intencional (los callbacks quedan huérfanos de módulo y no deben seguir vivos).
- **Const-correctness**: los métodos de `QJSValue` son no-const en Qt 5 — los callbacks se copian a variables locales antes de invocarse.
- **Errores amigables** (spec §2.6): los fallos de sintaxis se reportan con archivo y línea; un módulo roto no impide que el resto cargue.

## 6. Usage Example

```js
// <datos>/modules/automatizacion.js — se carga al arrancar la aplicación
jslib.log('automatización lista');
jslib.onEvent('slide_next', function (data) {
    jslib.tcpConnect('mixer', '192.168.1.50', 5000);
    jslib.tcpSend('mixer', 'SLIDE=' + (data.item || ''));
});
jslib.setTimeout(30000, function () {
    jslib.notify('Recordatorio', 'revisar niveles de audio');
});
```
