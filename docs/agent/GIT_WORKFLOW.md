# GIT_WORKFLOW.md — flujo de git y commits

- Convencionales en español: `feat(nucleo): …`, `fix(estudio): …`,
  `docs(agent): …`, `ci: …`, `test(biblia): …`.
- Una pieza por commit; el gate de calidad se ejecuta antes de cada commit.
- Rama de trabajo → PR a `main` (el CI debe estar verde antes de merge).
- Tags: `v1.x.y-beta.z` alineado con `ahp.v1` [SPEC §12.4].
- PROGRESS.md: SOLO añadir entradas (una línea por pieza). Rotación
  hot → warm → cold según AGENT.md. Prohibido reescribir la bitácora.
- Prohibido commitear tokens/credenciales (GitHub, OBS, Planning Center).
