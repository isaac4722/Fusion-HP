# Integraciones — OBS Studio · MIDI · Mando remoto (v5.0.0 «SINERGIA»)

> Requisito del spec (§3.3/§3.4): servidor API local, motor de activadores,
> integración OBS (WebSocket), entrada MIDI y control remoto móvil por LAN.
> Todo dentro del programa de escritorio; **cero instalaciones** para el usuario.

## OBS Studio (obs-websocket 5.x)

Página **«Integraciones»** → tarjeta OBS.

* Protocolo V5 completo: `Hello → Identify → Identified` con
  **autenticación** `base64(sha256(base64(sha256(pw+salt))+challenge))`
  (validada por el arnés contra un handshake de referencia).
* Acciones: **cambiar escena programada** (`SetCurrentProgramScene`) y
  **texto en vivo a una fuente** (`SetInputSettings` sobre una fuente de
  texto: letra/versículo actual en OBS, opción «Fuente de texto en vivo»).
* Transporte: `ClientWebSocket` en un hilo dedicado con tope de recepción
  (120 s) — **requiere .NET Framework 4.8** (en la variante net35 la tarjeta
  avisa y el resto del programa funciona igual).
* Se usa también desde **activadores** (`obs_scene`, `obs_source_text`).

## Entrada / salida MIDI

* **IN** (`Integrations/MidiInput.cs`): winmm.dll (`midiInOpen` con callback
  enraizado con GCHandle, patrón del puente del motor). Notas/CC/programa
  llegan a los activadores por el hilo de UI.
* **OUT**: acción `midi_out` de activadores (`midiOutShortMsg`).
* Dispositivos: botón «Buscar dispositivos» (nombre real vía `midiInGetDevCaps`).

## Mando remoto móvil (red local)

* La propia app sirve la página **`http://TU-IP:8070/remote`** (Spanish, táctil,
  autocontenida — sin archivos externos ni CDN): lista de diapositivas,
  transporte ◀/▶, pantalla negra, limpiar, avisos en pantalla y estado en vivo.
* **TcpListener propio** (HTTP/1.1 mínimo): a diferencia de `HttpListener`
  enlazado a `0.0.0.0` **NO requiere urlacl de administrador**. El firewall
  de Windows pregunta una única vez «Permitir acceso» — respuesta estándar.
* **Token recomendado**: se advierte en la interfaz si se activa sin él
  (cualquier equipo de la red podría controlar la proyección).
* Rutas: `GET /remote` (página), `GET /api/state`, `GET /api/catalog`,
  `GET /api/live.txt`, `POST /api/cmd` (`next|prev|show|black|clear|notice`).

## Salidas adicionales (misma página de transporte)

* **Monitor de escenario** (botón «Escenario»): 2ª pantalla con letra actual
  grande, 2 líneas siguientes, rótulo de bloque, próximo ítem y reloj HH:MM:SS.
* **Zócalo Lower Third** (botón «Aviso»): banda inferior semitransparente con
  barra de acento, fundido de entrada/salida y auto-ocultado configurable.
* **Video en la salida**: ítems «► Video…» en el culto; se reproducen a
  pantalla completa sobre la pantalla del proyector con **Windows Media
  Player** del SO (ActiveX por **enlace tardío**: `AxHost` derivado +
  reflexión → compila sin referencias COM; funciona donde WMP está
  instalado — todas las ediciones normales de Win7→Win11; en ediciones
  **N/KN** se informa del «Media Feature Pack»). Al terminar: auto-avance
  (configurable) y evento `video_ended` para activadores.

## API local (loopback, ya existente)

`ApiServer` (HttpListener, solo 127.0.0.1): `/api/state`, `/api/cmd`,
`/api/live.txt`, webhook OBS `POST /obs` — para OBS/Companion **en el mismo
equipo**. El mando remoto (arriba) es el canal LAN.
