# GIT_WORKFLOW.md — Flujo de git y commits

## Ramas

- `main`: línea canónica protegida; cambios por PR verificado.
- Línea reestructurada: `reestructuracion-v1.0.0` → PR → merge → tag
  `v1.0.0-beta.1` → GitHub Release con zips x86/x64 + SHA256SUMS.

## Commits convencionales (español)

Formato: `<tipo>(v<x.y.z>): resumen en español, cuerpo con el detalle por
fixture/pieza y los gates que cierran`. Tipos: feat/fix/chore/docs/refactor/test.

- Una pieza por commit (regla del ciclo 9.5 pasos) — el cuerpo lista la
  trazabilidad (`F0.05`, `F4.15`, …) y las evidencias (comando + resultado).
- Prohibido commitear: tokens, contraseñas de OBS, credenciales de Planning
  Center, binarios, secretos [AGENT.md].
- `PROGRESS.md` se ACTUALIZA AÑADIENDO (jamás reescribir entradas previas).

## Push y CI

1. Gate local `bash scripts/quality_gate.sh` verde antes de cada push.
2. Push a la rama de trabajo → CI híbrido (auditoría F6.09 + núcleo x86/x64 +
   port Linux + gestionada + interop + package).
3. CI verde → merge/PR; tag `v*` dispara el job de release.
4. El token de push NUNCA va al repo: se usa solo en la URL remota efímera del
   operador (variable de sesión), no en configs ni logs.
