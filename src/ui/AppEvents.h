// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  AppEvents.h : Eventos personalizados que los paneles emiten hacia el
//  MainFrame (desacoplamiento: los paneles no conocen el frame).
//  Payload convencional:
//   - SetInt(id)   : id de la entidad (cancion, tema, fila del culto)
//   - SetString(s) : referencia/ruta/texto ("book|chap|vfrom|vto", ruta...)
// ============================================================================
#ifndef LUMINA_APPEVENTS_H
#define LUMINA_APPEVENTS_H

#include <wx/event.h>

wxDECLARE_EVENT(EVT_UI_GOLIVE_SONG, wxCommandEvent);     // id = songId
wxDECLARE_EVENT(EVT_UI_ADD_SONG, wxCommandEvent);        // id = songId
wxDECLARE_EVENT(EVT_UI_GOLIVE_REF, wxCommandEvent);      // str = "book|chap|vfrom|vto"
wxDECLARE_EVENT(EVT_UI_ADD_REF, wxCommandEvent);         // str = referencia cruda
wxDECLARE_EVENT(EVT_UI_GOLIVE_MEDIA, wxCommandEvent);    // str = ruta
wxDECLARE_EVENT(EVT_UI_ADD_MEDIA, wxCommandEvent);       // str = ruta
wxDECLARE_EVENT(EVT_UI_APPLY_THEME, wxCommandEvent);     // id = themeId
wxDECLARE_EVENT(EVT_UI_DATA_CHANGED, wxCommandEvent);    // str = "songs"|"themes"|"media"
wxDECLARE_EVENT(EVT_UI_SERVICE_GOLIVE, wxCommandEvent);  // id = fila
wxDECLARE_EVENT(EVT_UI_SERVICE_CHANGED, wxCommandEvent); // (sin payload)

#endif // LUMINA_APPEVENTS_H
