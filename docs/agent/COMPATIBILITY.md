# COMPATIBILITY.md — Compatibilidad y degradación A/B/C

**Fuente:** documento técnico v1.1 §4 (bootstrap, escalera .NET, matriz).

## Secuencia de arranque (fija, en el núcleo antes de cargar C#)

1. SO por `RtlGetVersion`/`VerifyVersionInfo` → Win7 SP1 | 8.1 | 10 | 11 (build ≥ 22000).
2. Arquitectura por `IsWow64Process2` → x86/x64/WOW64.
3. Runtime .NET (solo lectura, claves NDP) → 4.8+/4.7.2+ | 3.5 SP1–4.6.x | ausente.
4. Perfil: **A** (todo el MVP) · **B** (Live completo + degradaciones controladas) ·
   **C** (núcleo nativo autónomo + UI de emergencia).
5. Carga de la capa gestionada en proceso + IPC; el resultado se registra en el log.

## Matriz

| Combinación | Estado |
|---|---|
| Win7 SP1 x86 (≤4 GB) | soportado — binario x86, perfil A/B/C según .NET |
| Win7 SP1 x64 | soportado — variante a elección |
| Win8.1/Win10 x86/x64 | soportado — perfil A |
| Win11 x64 | objetivo óptimo — binario x64, perfil A |
| Win7 sin SP1 | NO soportado — el bootstrap informa y sugiere SP1 |

## Reglas de degradación

- Ninguna función del perfil A puede fallar silenciosamente en B/C: guard
  clauses en arranque + deshabilitación con mensaje explicativo.
- El perfil activo se muestra en `Ayuda → Estado del sistema`.
- Sin .NET 4.8 el programa NO descarga nada: ofrece el instalador offline
  oficial como componente opcional del paquete.
- Configuración: `%APPDATA%\AppHibrida` (instalado) o carpeta del programa
  (portable) — nunca el Registro.
- Compatibilidad cruzada: un proyecto creado en x86 abre idéntico en x64.

## Verificación (gate F0 / F6)

Win7 x86 sin .NET arranca y proyecta (perfil C real) · log con
SO/arquitectura/perfil · la ausencia de C# no interrumpe la salida.
Estado de esta campaña: ver `docs/verification/` (limitación de entorno
documentada — sin hardware Win7 disponible; CI cubre la compilación dual).
