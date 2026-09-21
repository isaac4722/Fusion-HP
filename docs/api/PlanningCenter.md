# PlanningCenter — Sincronización con Planning Center Online

## 1. Class Overview

`PlanningCenter` implementa el requisito de la especificación (§3.3) de **descarga e importación de programas y listas de canciones desde Planning Center Online**. Es un cliente asincrónico de la API v2 de PCO (`https://api.planningcenteronline.com/services/v2`) autenticado con un **Personal Access Token** (Application ID + Secret, HTTP Basic), que expone un flujo de tres pasos orientado a señales para que la GUI reaccione sin bloquear el hilo principal.

El emparejamiento con la biblioteca local (comparación de títulos normalizada, insensible a mayúsculas y acentos) no vive en esta clase: la realiza `MainWindow::onPcoImportItems`, que convierte los ítems del plan en `ServiceItem` de la cola del culto.

## 2. Project Structure and Dependencies

- **Incluido por**: `main.cpp` (creación), `gui/CommsPanel.h/.cpp` (UI de conexión e importación), `gui/MainWindow.h/.cpp` (importación a la cola).
- **Referenciado por** `AppContext` como `ctx.pco`.
- **Requiere**: Qt5 Core y Network. Sin dependencias de terceros.

## 3. Public API

| Método | Tipo | Descripción |
|---|---|---|
| `setToken(appId, appSecret)` | `void` | Fija el PAT (recorta espacios). |
| `hasToken()` | `bool` | ¿Credenciales presentes? |
| `fetchServiceTypes()` | `void` | Paso 1: lista de ministerios (hasta 100). |
| `fetchPlans(serviceTypeId)` | `void` | Paso 2: los 15 planes futuros más próximos. |
| `fetchPlanItems(planId)` | `void` | Paso 3: ítems del plan con `include=song` (hasta 200). |

**Constantes**: `kTimeoutMs = 15000` (todas las peticiones llevan `setTransferTimeout`), `kMaxPlans = 15`, `kMaxItems = 200`.

## 4. Signals

| Señal | Payload | Emisión |
|---|---|---|
| `serviceTypesReady` | `(ok, error, QVector<PcoServiceType>)` | Respuesta de `fetchServiceTypes()`. |
| `plansReady` | `(ok, error, QVector<PcoPlan>)` | Respuesta de `fetchPlans()`. |
| `itemsReady` | `(ok, error, planId, QVector<PcoItem>)` | Respuesta de `fetchPlanItems()`. |

`ok=false` entrega siempre un mensaje en lenguaje no técnico (spec §2.6): token rechazado, sin conexión, tiempo agotado, TLS, plan inexistente.

## 5. Data Types

- `PcoServiceType`: `id`, `name`.
- `PcoPlan`: `id`, `serviceTypeId`, `title`, `dates` (legible), `sortDate` (ISO).
- `PcoItem`: `title`, `description`, `songTitle` (resuelta desde el `include=song`), `isSong`, `sequence`.

## 6. Notas de diseño

- **Validación explícita de JSON** (`isNull()`/`isObject()`) antes de emitir — una respuesta corrupta llega como error amigable, no como lista vacía silenciosa (regla ERR-2 de qt-cpp-review).
- **Errores TLS registrados** sin ignorarse en ciego (regla ERR-9).
- **Persistencia del token**: la guarda `CommsPanel` en los ajustes del vault (`pco_app_id` / `pco_app_secret`).

## 7. Usage Example

```cpp
// CommsPanel (extracto): conectar, cargar planes e importar el seleccionado
pco->setToken(m_pcoId->text(), m_pcoSecret->text());
pco->fetchServiceTypes();
// … al llegar plansReady, el combo de planes se llena; el usuario elige:
pco->fetchPlanItems(m_pcoPlan->currentData().toString());
// … itemsReady -> emit pcoImportItems(items) -> MainWindow::onPcoImportItems
```
