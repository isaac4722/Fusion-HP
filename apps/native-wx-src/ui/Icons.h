// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Icons.h : Fabrica de iconos vectoriales dibujados con wxGraphicsContext.
//  Sin archivos de imagen: cada icono se genera bajo demanda al tamano
//  pedido (nitidez garantizada en escalados de DPI) con una paleta coherente
//  (ambar = accion en vivo, gris azulado = edicion).
// ============================================================================
#ifndef LUMINA_ICONS_H
#define LUMINA_ICONS_H

#include <wx/bitmap.h>
#include <wx/string.h>

class Ico
{
public:
    // Nombres disponibles: live, next, prev, black, clear, logo, add, edit,
    // copy, del, up, down, search, gear, song, bible, media, theme, service,
    // screen, alert, backup, remote, folder, play, check, warn, err, info
    static wxBitmap Get(const wxString &name, int px);
};

#endif // LUMINA_ICONS_H
