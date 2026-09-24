---
name: ipc-bridge
description: Protocolo IPC ipc.v1 entre el núcleo C++ y la capa C# — pipes con nombre, mensajes binarios con longitud prefijada, cola no bloqueante y latencia ≤16 ms. Usar siempre que se toque el puente C++/C#.
---

# ipc-bridge — Protocolo ipc.v1 [SPEC §3.4, F0.05]

## Contrato

- Pipes con nombre (Win32 `\\.\pipe\<name>`; la capa C# usa `System.IO.Pipes`).
- Mensajes BINARIOS con longitud prefijada. Frame de 12 bytes:
  `[u32 magic][u16 version=1][u16 type][u32 length]` + payload UTF-8.
- Tipos: HELLO, WELCOME, COMMAND, RESULT, STATE, EVENT, PING, PONG, ERROR.
- Versión `ipc.v1`; mensajes de versión desconocida se rechazan sin tumbar el núcleo.

## Cola de comandos (C# → render)

- Cola NO bloqueante con límite de latencia explícito (`maxAgeMs`).
- Comandos vencidos se descartan y se cuentan (F6.01).
- La proyección y los atajos SOBREVIVEN si C# se desconecta (F0.05.8).

## Capas de prueba (ambas obligatorias)

1. FrameDecoder/EncodeFrame + CommandQueue — puras, multiplataforma
   (fragmentación, magic malo, payload grande, versión desconocida).
2. Transporte real por pipes en el runner Windows (loopback del arnés C#).

## Reglas

- El núcleo funciona íntegro aunque la capa C# no cargue (perfil C).
- Toda función dependiente de C# verifica disponibilidad con guard clause —
  prohibido el fallo silencioso.
- El hosting C# en proceso es obligatorio para el producto; un `CreateProcess`
  de otro ejecutable NO satisface el diseño.
