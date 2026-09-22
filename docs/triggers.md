# Activadores (Triggers) — v5.0.0 «SINERGIA»

> Requisito del spec (§3.3): «Motor de Activadores: reglas automáticas
> evaluadas continuamente; eventos internos (proyectar canción, fin de video)
> o externos (comando MIDI, llamada de la API) → acciones (cambiar escena en
> OBS, reproducir audio, enviar MIDI, mostrar texto)». Espejo del sistema de
> Holyrics.

## Modelo

```
EVENTO ──┬── CONDICIONES (match) ──┐
         │                          ├──> ACCIÓN(ES)
contexto ┴──────────────────────────┘
```

Una **regla** = `{id, name, enabled, event, match:{…}, actions:[{type, params:{…}}]}`,
persistida en **`data\triggers\triggers.json`** (tolerante a corrupción:
arranque con reglas vacías, nunca bloquea).

## Eventos publicados

| Evento | Contexto | Quién lo publica |
|---|---|---|
| `item_changed` | `item, index, kind, title, path` | eventos del motor |
| `slide_changed` | `index, item, kind, title` | eventos del motor |
| `song_started` | `item, title` | primer slide de un ítem canción |
| `video_ended` | `item, path, title` | VideoPlayerForm al terminar |
| `midi_note` | `channel, note, velocity` | winmm MIDI IN |
| `midi_cc` | `channel, controller, value` | winmm MIDI IN |
| `midi_program` | `channel, program` | winmm MIDI IN |
| `api_webhook` | `event, payload` | webhook OBS (ApiServer) |

## Condiciones (match)

Pares `clave=valor` sobre el contexto. Prefijos del patrón:

* `title=Ofrenda` → igual (insensible a mayúsculas; numérico si ambos lo son)
* `title=contains:alabanza` → contiene
* `value=gte:5` / `value=lte:5` → comparación numérica
* `note=60` → igualdad numérica
* `x=*` → comodín (siempre)

Sin condiciones → la regla dispara **siempre** que ocurra el evento.

## Acciones

| Tipo | Parámetros | Efecto |
|---|---|---|
| `obs_scene` | `scene` | cambia la escena programada de OBS |
| `obs_source_text` | `source, text` | escribe texto en una fuente OBS |
| `play_audio` | `path, volume` | reproduce audio (WMP oculto) |
| `show_text` | `text, subtitle, seconds` | zócalo lower third |
| `set_theme` | `theme` | aplica un tema de `data\themes` |
| `api_cmd` | `action(next/prev/black/clear/show), on, index` | comando al motor |
| `midi_out` | `status, data1, data2` | mensaje corto MIDI OUT |

## Ejemplos (guiados por el spec de Holyrics)

1. **Ofrenda automática**: evento `item_changed`, condición `title=Ofrenda`,
   acción `obs_scene` con `scene=Culto` → al proyectar la diapositiva de la
   ofrenda, OBS cambia solo a la escena preparada.
2. **Tema por etiqueta**: evento `song_started`, condición
   `title=contains:Adoración`, acción `set_theme` con `theme=Soleado`.
3. **Pedal MIDI**: evento `midi_cc`, condición `controller=7`, acción
   `api_cmd` con `action=next` → el guitarrista avanza la letra.
4. **Cierre de video**: evento `video_ended`, acción `obs_scene` con
   `scene=Cámaras`.

La página **«Activadores»** permite crear/editar/activar/eliminar reglas con
un diálogo directo (evento + condición + acción con parámetros); el JSON
queda guardado y se recarga en cada arranque.
