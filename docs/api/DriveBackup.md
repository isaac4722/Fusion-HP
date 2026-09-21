# DriveBackup — Respaldo y sincronización en Google Drive

## 1. Class Overview

`DriveBackup` cubre el requisito de la especificación (§3.4) de **respaldo automático y sincronización en Google Drive** de canciones, biblias y configuraciones. Implementa el flujo OAuth 2.0 *loopback* para aplicaciones de escritorio nativas: al conectar, se abre el navegador del sistema con `redirect_uri = http://127.0.0.1:<puerto>` y un `QTcpServer` local recibe el código de autorización — sin copiar/pegar códigos y sin secretos embebidos en el binario (el usuario pega las credenciales de su propia app de Google Cloud).

Alcance mínimo **`drive.file`**: la aplicación solo crea y lee sus propios archivos, alojados en la carpeta oculta `appDataFolder` del Drive del usuario, con **rotación de 4 copias** (idéntica a la política local). Las subidas usan `uploadType=multipart` con metadatos JSON + bytes del `.db`.

## 2. Project Structure and Dependencies

- **Incluido por**: `main.cpp` (creación y hook de auto-subida), `gui/SettingsPanel.cpp` (UI de conexión y operaciones).
- **Referenciado por** `AppContext` como `ctx.drive`.
- **Requiere**: Qt5 Core y Network (`QTcpServer`, `QNetworkAccessManager`), `QDesktopServices` para abrir el navegador.
- **Depende de**: `Database` (puntero no propietario) para persistir las credenciales en los ajustes del vault.

## 3. Public API

| Método | Tipo | Descripción |
|---|---|---|
| `setDatabase(Database *)` | `void` | Inyección del vault (ajustes). |
| `hasAccount()` | `bool` | ¿Hay refresh token + credenciales guardados? |
| `connectAccount(clientId, clientSecret)` | `void` | OAuth loopback: abre el navegador y espera el código (3 min de espera). |
| `backupNow(dbFilePath)` | `void` | Sube el `.db` a `appDataFolder` y rota (4 más recientes). |
| `listBackups()` | `void` | Lista las copias (más nueva primero). |
| `downloadBackup(fileName, destPath)` | `void` | Descarga una copia (nombre vacío = la más reciente). |
| `disconnectAccount()` | `void` | Olvida credenciales y token. |

**Constantes**: `kTimeoutMs = 20000` (llamadas API), `kAuthTimeoutMs = 180000` (autorización en navegador), `kKeepBackups = 4`.

## 4. Signals

| Señal | Payload | Emisión |
|---|---|---|
| `authResult` | `(ok, message)` | Fin del flujo OAuth (éxito, cancelación, caducidad). |
| `uploadResult` | `(ok, message)` | Fin de una subida (tras rotar). |
| `backupsListed` | `(ok, message, names)` | Respuesta de `listBackups()`. |
| `downloadResult` | `(ok, message, path)` | Descarga terminada (ruta local o vacío). |

## 5. Settings Keys (vault)

| Clave | Contenido |
|---|---|
| `drive_client_id` / `drive_client_secret` | Credenciales de la app de escritorio del usuario. |
| `drive_refresh_token` | Token de refresco (vacío = sin cuenta). |
| `drive_auto_upload` | `1` sube también la copia automática semanal. |

## 6. Notas de diseño

- **Renovación automática**: `ensureToken(cb)` refresca el access token cuando falta o expira (margen de 60 s); los 401 se traducen en mensajes claros («¿revocó el permiso?»).
- **Hook semanal**: `Database::autoBackupCreated(path)` → `main.cpp` sube la copia si `drive_auto_upload=1`.
- ** Seguridad**: errores TLS se registran sin ignorarse en ciego; el listener loopback responde una página amigable y se cierra tras recibir el código.
- **Restauración explícita**: la descarga deja el `.db` en `backups/drive_latest.db`; aplicar la restauración es un paso deliberado del usuario con el botón local existente (flujo reversible).

## 7. Usage Example

```cpp
// main.cpp (extracto): subida automática tras el backup semanal
drive.setDatabase(&db);
QObject::connect(&db, &Database::autoBackupCreated, &drive,
                 [&db, &drive](const QString &path) {
    if (drive.hasAccount() &&
        db.setting("drive_auto_upload", "0") == "1")
        drive.backupNow(path);
});
```
