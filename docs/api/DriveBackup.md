# Respaldo y sincronización en la nube

> Estado v5.1.0 «FUNDAMENTO»: **respaldo ZIP a carpeta** (implementado y
> distribuido). La subida directa a Google Drive por OAuth queda en
> [roadmap](../roadmap.md): exige credenciales del propietario (client_id de
> Google Cloud), que el proyecto no puede inventar ni embeber.

## Qué hace la app HOY (Ajustes → «Respaldo y sincronización»)

- **Carpeta de respaldo configurable** («Explorar…»). Recomendado: la carpeta
  local del cliente oficial de **Google Drive** o **OneDrive** instalado en el
  PC — así el respaldo viaja a la nube SIN claves, SIN OAuth y SIN que la app
  toque Internet.
- **Respaldar ahora**: comprime TODA la carpeta `data\` (canciones, biblias,
  temas, activadores, ajustes) en
  `lumina-backup-AAAA-MM-DD_HHmmss.zip` dentro de la carpeta elegida.
- **Automático al cerrar**: la misma operación se dispara al salir de la app
  si está activado.
- **Retención**: se conservan los 10 respaldos más recientes (los viejos se
  eliminan automáticamente).
- **Restaurar**: extraer el ZIP sobre la carpeta del programa (recupera
  `data\` completa).

El motor ZIP es **propio** (`managed/Lumina.Core/Backup/ZipBackup.cs`):
formato PKWARE APPNOTE.TXT con CRC32 y Deflate — abre en el Explorador de
Windows, 7-Zip, WinRAR y en las vistas previas de Drive/OneDrive, y es
net35-safe (cero dependencias del GAC).

> La integridad de cada respaldo la cubre el test `ZipBackup: ZIP válido y
> extraíble` del arnés (`managed/Tests/Tests.cs`), que lo reabre con un lector
> ZIP real y verifica contenido y UTF-8.

## Dónde está el código

- `managed/Lumina.Core/Backup/ZipBackup.cs` — motor ZIP.
- `managed/Lumina.UI/MainForm.cs` → `PerformBackup()` / `RunBackupNow()` /
  `BrowseBackupFolder()` y el disparo en `OnFormClosing`.

## Plan técnico de la subida directa (roadmap)

1. OAuth 2.0 de Google con loopback + `drive.file` scope (solo archivos creados por la app).
2. Subida resumible multipart a `upload/drive/v3/files`.
3. Sesiones activas en `data\integrations\drive.json` (portable).
