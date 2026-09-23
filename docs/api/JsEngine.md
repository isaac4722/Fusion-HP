# JsEngine / JsLibHost — Extensibilidad JavaScript (JSLib)

## 1. Class Overview

LuminaPresentation Suite es una aplicación híbrida de presentación litúrgica
y multimedia. Dentro de su arquitectura, `JsEngine` y su clase auxiliar
`JsLibHost` dan soporte al requisito de **extensibilidad JavaScript / JSLib**
de la especificación (§3.3): permitir que el usuario automatice e integre el
programa con otros sistemas (mezcladores, OBS, servicios web) mediante
módulos `.js` cargados en caliente, sin recompilar ni instalar nada.

`JsEngine` es el orquestador: hospeda el **motor IActiveScript/JScript del
propio Windows** (`jscript.dll`, ES3, presente de Win7 SP1 a Win11 — cero
despliegue, sin NuGet, sin Lua y sin binario nativo adicional), carga los
módulos del directorio `<datos>\modules\` y expone a la UI los puntos de
entrada `FireEvent()`, `CallGlobal()`, `LoadModules()` y `Reload()`.
`JsLibHost` es el objeto `jslib` visible desde JScript: una clase
**ComVisible con ClassInterface AutoDispatch** (resolución tardía por
`GetIDsOfNames`, sin sobrecargas) con métodos para sockets persistentes,
HTTP, temporizadores, acciones sobre la app y suscripción a eventos.

> **Evolución documentada**: el diseño original de este archivo describía el
> hospedaje con `QJSEngine` (Qt). La arquitectura real del programa es
> C++17 Win32 + capa gestionada C# (ver `docs/architecture-hybrid.md`), por
> lo que el motor se implementa con **IActiveScript COM interop** desde la
> UI WPF (`managed/Lumina.WPF/Scripting/`), conservando exactamente el mismo
> contrato de API y las mismas lecciones de ciclo de vida.

## 2. Project Structure and Dependencies

- **Capa pura (testeable en el arnés net8/Linux)**:
  `managed/Lumina.Core/Scripting/JsModules.cs` —
  `JsModuleScanner` (escaneo tolerante de `<datos>\modules\*.js`, orden
  alfabético estable, error POR ARCHIVO sin tumbar el resto), `JsLogRing`
  (ring thread-safe de 60 líneas), `JsEventRegistry` (suscripciones como
  datos puros con tokens opacos), `JsTimerIds` (ids > 0 con liberación
  validada) y `JsPrelude` (preámbulo ES3 con `jsonParse`).
- **Runtime (UI WPF net48)**: `managed/Lumina.WPF/Scripting/` —
  `ActiveScriptInterop.cs` (interfaces COM fiel al vtable de `ActivScp.h`:
  `IActiveScript` 13 métodos, `IActiveScriptSite` 8, `IActiveScriptParse` 3,
  `IActiveScriptError` + `EXCEPINFO`), `JsLibHost.cs` (el objeto `jslib`) y
  `JsEngine.cs` (el sitio `ActiveScriptSite` + ciclo de vida).
- **Incluido por**: `MainWindow.Integrations.cs` (arranque/recarga/cierre,
  retransmisión de eventos), `App.xaml.cs` (paso 5 del `--selfcheck` —
  gate REAL del motor en el runner Windows de CI) y la página
  *Integraciones › Módulos JS*.
- **Requiere**: .NET Framework 4.8 (`ClientWebSocket` para `ws`; `tcp`,
  `httpGet`, COM y timers funcionan igual en net48). La variante baseline
  net35 **no** trae el motor (misma decisión que OBS — ver roadmap).

## 3. Public API — JsEngine

| Método | Tipo | Descripción |
|---|---|---|
| `JsEngine.Create(Dispatcher, IJsLibSink)` | estático | Crea el motor sin cargar módulos. |
| `LoadModules(dir)` | `bool` | Escanea y evalúa `dir\*.js` en un motor NUEVO; `false` si algún módulo falló (los válidos siguen cargados). |
| `Reload()` | `bool` | Recarga (motor nuevo; conexiones cerradas por diseño). |
| `FireEvent(name, payloadJson)` | `void` | Eventos del presentador → callbacks `onEvent`. Thread-safe (despacha al hilo del motor si hace falta). |
| `CallGlobal(name, args…)` | `object` | Invoca una función global (acción de activador `script`; `null` si no existe). |
| `LoadedModules()` / `Errors()` | listas | Nombres cargados; errores `archivo:línea — mensaje`. |
| `Log` | `JsLogRing` | Registro visible (persiste entre recargas). |
| `Dispose()` | — | Suelta conexiones/callbacks ANTES de cerrar el motor (orden seguro). |

Orden canónico de arranque del motor COM:
`SetScriptSite → InitNew → AddNamedItem("jslib") → Started →
ParseScriptText(preludio + módulos) → Connected → GetScriptDispatch`.

## 4. Public API — JsLibHost (el objeto `jslib` de JS)

| Método | Descripción |
|---|---|
| `version()` | Versión del motor (`"6.1.0"`). |
| `log(msg)` | Registro visible en Integraciones › Módulos JS (ring de 60). |
| `notify(titulo, texto)` | Registro destacado + aviso de la barra de estado. |
| `httpGet(url, cb)` | GET con timeout de 15 s; `cb(status, body)`; status 0 = fallo. |
| `tcp(id, host, puerto, onLine)` | Socket TCP persistente identificado por `id` (líneas UTF-8 delimitadas por `\n`, `\r\n` tolerado). |
| `tcpSend(id, texto)` / `tcpClose(id)` / `tcpConnected(id)` | Envío (UTF-8 puro, sin delimitador añadido) / cierre / diagnóstico. |
| `ws(id, url, onMessage)` | WebSocket persistente (frames de texto; binarios ignorados con aviso). |
| `wsSend(id, texto)` / `wsClose(id)` / `wsConnected(id)` | Envío / cierre / diagnóstico. |
| `cmd(accion[, indice])` | `'next' · 'prev' · 'black' · 'clear' · 'show'` al motor (devuelve resultado legible). |
| `showText(texto[, segundos])` | Aviso en pantalla (zócalo lower third; 6 s por defecto). |
| `onEvent(nombre, cb)` | Suscripción a eventos del presentador (ver abajo). |
| `setTimeout(ms, cb)` / `clearTimeout(id)` | Temporizadores de un disparo (id > 0). |

**Eventos** (los mismos que los activadores, con payload JSON — parsear con
`jsonParse(data)` del preámbulo): `slide_changed` `{index, item, kind,
title, path}`, `item_changed`, `song_started`, `video_ended` `{item, path,
title}`, `midi_note` `{channel, note, velocity}`, `midi_cc`, `midi_program`.

## 5. Notas de diseño y ciclo de vida

- **Apartamento-hilo**: el motor COM se crea en el hilo de UI (STA) y TODO
  callback JS se ejecuta allí — los sockets/HTTP/timers corren en hilos
  propios y despachan por `Dispatcher.BeginInvoke` (patrón del `ObsClient`,
  sin async/await en la capa gestionada).
- **Orden de destrucción**: `Dispose`/recarga cierran sockets y timers y
  sueltan los callbacks (huérfanos por diseño — no deben invocar JS de un
  motor muerto) ANTES de `SetScriptState(Disconnected)` + `Close()`.
- **Recarga**: destruye host y motor; las conexiones persistentes se cierran
  de forma intencional (mismo contrato documentado del diseño original).
- **Invocación tardía**: las funciones JScript llegan a .NET como
  `System.__ComObject` (IDispatch) y se invocan por `InvokeMember` con
  `DISPID_VALUE`; las funciones globales por `GetScriptDispatch(null)` +
  `GetIDsOfNames`. Sin `dynamic` (contrato C# 7.3 de la capa).
- **Confianza**: los payloads JSON los construye el propio programa
  (`MiniJson.Serialize` de contextos internos) y los módulos son archivos
  locales del usuario — el `eval` de `jsonParse` no introduce entrada
  remota (mismo modelo de confianza que los activadores).
- **Errores amigables** (spec §2.6): los fallos de sintaxis llegan por
  `OnScriptError` con `archivo:línea — descripción` (los BSTR de `EXCEPINFO`
  se liberan); un módulo roto no impide que el resto cargue.
- **Gates**: tests net8 de la capa pura (escáner/ring/registro/timers) +
  paso 5 del `--selfcheck` en CI Windows con el motor COM REAL (módulo de
  autoprueba que ejercita `log`, `onEvent`+`jsonParse`, `cmd`, `showText`,
  `setTimeout` asíncrono bombeando el dispatcher y `clearTimeout`).

## 6. Usage Example

```js
// <datos>\modules\automatizacion.js — se carga al activar el motor
// (Integraciones › Módulos JS) o con «Recargar módulos».
jslib.log('automatización lista (' + jslib.version() + ')');

jslib.onEvent('slide_changed', function (data) {
    var d = jsonParse(data);           // payload JSON del preámbulo ES3
    jslib.tcp('mixer', '192.168.1.50', 5000, function (linea) {
        jslib.log('el mezclador dijo: ' + linea);
    });
    jslib.tcpSend('mixer', 'SLIDE=' + d.title + '\n');
});

jslib.httpGet('http://192.168.1.60/api/estado', function (status, body) {
    if (status == 200) jslib.notify('Servicio', 'estado consultado OK');
});

jslib.setTimeout(30000, function () {
    jslib.notify('Recordatorio', 'revisar niveles de audio');
});
```

Y desde los **activadores** (regla evento → condición → acción `script`):
`function=alIniciar · data={"bloque":"Alabanza"}` invoca la función global
`alIniciar('{"bloque":"Alabanza"}')` de los módulos cargados.
