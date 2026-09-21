// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  AppContext.h : Contexto compartido entre la ventana principal y los
//  paneles (sin propiedad; todo se crea y destruye en MainWindow).
// ============================================================================
#ifndef LUMINA_APPCONTEXT_H
#define LUMINA_APPCONTEXT_H

#include "core/Models.h"

class Database;
class MediaEngine;
class WebServer;
class Triggers;
class JsEngine;
class PlanningCenter;
class DriveBackup;

struct AppContext
{
    Database    *db = nullptr;
    MediaEngine *media = nullptr;
    WebServer   *web = nullptr;
    Triggers    *triggers = nullptr;
    Theme       *currentTheme = nullptr;    // tema activo (propiedad de MainWindow)
    JsEngine       *js = nullptr;           // v1.5.0: módulos JS / JSLib
    PlanningCenter *pco = nullptr;          // v1.5.0: Planning Center Online
    DriveBackup    *drive = nullptr;        // v1.5.0: respaldo Google Drive
};

#endif // LUMINA_APPCONTEXT_H
