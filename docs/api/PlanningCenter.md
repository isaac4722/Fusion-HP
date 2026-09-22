# Planning Center Online — Integración

> Estado v5.1.0 «FUNDAMENTO»: **importación de planes JSON** (implementado y
> distribuido). La descarga directa por OAuth contra la API de PCO queda en
> [roadmap](../roadmap.md): requiere credenciales del propietario de la cuenta
> de la organización (client_id/client_secret), que el proyecto no puede
> inventar ni embeber.

## Qué hace la app HOY

La página **Culto** trae «Importar plan JSON…», que carga un plan de servicio
completo como ítems del culto. Está pensado para:

- **Listas exportadas de Planning Center Online** (el plan del servicio
  exportado/adaptado a este formato por el equipo de medios).
- **Compartir cultos entre equipos** (un operador prepara el plan, otro lo importa).

## Formato del plan JSON

```json
{
  "name": "Culto Domingo — Adoración",
  "items": [
    { "type": "song",      "title": "Grande es el Señor", "artist": "…",
      "lyrics": "[Verso 1]\nGrande es el Señor...\n\n[Coro]\n…" },
    { "type": "scripture", "ref": "jn 3:16-18", "version": "RVR1909" },
    { "type": "text",      "title": "Bienvenida", "text": "Bienvenidos…" },
    { "type": "blank",     "title": "Pausa" }
  ]
}
```

| Campo | Notas |
|---|---|
| `name` | Opcional; si viene, se aplica al campo «Nombre» del culto. |
| `items[].type` | `song` · `scripture` · `text` · `blank` (desconocidos → blanco). |
| `song.lyrics` | Formato del editor: bloques `[Verso 1]` / `[Coro]`, acordes sobre la línea. |
| `scripture.ref` | Cualquier referencia del núcleo: `Jn 3:16`, `sal 23:1-6`, `1 co 13,4-7`. Se resuelve contra la BD abierta. |
| `scripture.version` | Opcional; si se omite usa la última versión utilizada. |
| `scripture.text` | Alternativa a `ref`: versos crudos separados por `\n`. |

Los ítems importados usan las mismas fábricas (`ScenarioBuilder.*`) que el
resto de la app: validación de canción por el núcleo y aplanado idéntico.

## Dónde está el código

- `managed/Lumina.UI/MainForm.cs` → `ImportPlanJson()`.
- `managed/Lumina.Core/ScenarioBuilder.cs` → fábricas de ítems.

## Plan técnico de la descarga directa (roadmap)

1. OAuth 2.0 + PKCE con loopback `http://127.0.0.1:<puerto>/callback`
   (sin navegador embebido; abre el del sistema).
2. `GET /services/v2/service_types/{id}/plans?filter=future&order=sort_date`.
3. Mapeo `plan_item` → este mismo formato JSON (el de arriba pasa a ser el
   contrato interno de intercambio).
4. Token en `data\integrations\pco.json` (portable, sin registro).
