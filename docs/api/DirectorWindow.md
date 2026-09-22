# DirectorWindow — Pantalla 3 «Director / Instrucciones»

> Estado v5.1.0 «FUNDAMENTO»: **implementado y distribuido** como
> `DirectorForm` (WinForms, net35-safe). Se abre desde En Vivo con el botón
> «⚑ Director», la tecla **F7** o `Settings.DirectorScreen`.

## Qué es

La **tercera salida de pantalla** del Multiview del spec (§3.3): *«Pantalla
HTML / Instrucciones: mensajes y notas internas para el director del
servicio»*. Junto a la salida pública (proyector nativo) y el monitor de
escenario (StageViewForm para músicos), completa el modelo de hasta tres
salidas independientes desde una misma interfaz.

## Contenido

- **Cronómetro grande** del servicio: Espacio = iniciar/pausar · R =
  reiniciar (doble clic también alterna).
- **Reloj de pared** HH:MM:SS permanente.
- **Texto COMPLETO del ítem actual** (todas las líneas, para leer adelantado,
  no solo la slide visible) con rótulo de bloque.
- **Próximo ítem** del culto con adelanto de líneas.
- **Panel de notas** editable abajo (avisos internos del director; no se
  proyecta ni persiste — es volátil por diseño).

## Controles

| Tecla / gesto | Acción |
|---|---|
| `Escape` | Cerrar |
| `F11` | Alternar pantalla completa |
| `Espacio` | Iniciar/pausar cronómetro (fuera del panel de notas) |
| `R` | Reiniciar cronómetro |
| Doble clic | Alternar cronómetro |

## Dónde está el código

- `managed/Lumina.UI/DirectorForm.cs` — el formulario.
- `managed/Lumina.UI/MainForm.cs` → `OpenDirectorView()` /
  `UpdateDirectorView()` (refresco por evento del motor) /
  `DirectorItemLines()` (recopilación de líneas por ítem).
- `Settings.DirectorScreen` — pantalla destino (Ajustes, persistido).

## Pendiente (roadmap)

- Notas **persistentes por ítem** (campo `note` en `ScenarioItem` + editor).
- Espejo web del director (`/director.html`) vía el RemoteServer.
