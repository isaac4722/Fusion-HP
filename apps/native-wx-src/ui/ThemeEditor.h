// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ThemeEditor.h : Editor visual de temas con previsualizacion en vivo y
//  comprobador de contraste WCAG.
// ============================================================================
#ifndef LUMINA_THEMEEDITOR_H
#define LUMINA_THEMEEDITOR_H

#include "../core/Types.h"

#include <wx/checkbox.h>
#include <wx/clrpicker.h>
#include <wx/dialog.h>

class wxColourPickerCtrl;
class wxComboBox;
class wxFontPickerCtrl;
class wxSpinCtrl;
class wxTextCtrl;
class wxStaticText;
class PreviewCanvas;
class Database;

class ThemeEditor : public wxDialog
{
public:
    ThemeEditor(wxWindow *parent, Database &db, const Theme &theme, bool isNew);

private:
    void OnChange(wxCommandEvent &e);
    void OnChange(wxSpinEvent &e);
    void OnColour(wxColourPickerEvent &e);
    void OnOk(wxCommandEvent &e);
    void OnBgType(wxCommandEvent &e);
    void OnPickImage(wxCommandEvent &e);
    void OnIdle(wxIdleEvent &e);
    void RefreshPreview();

    Database &m_db;
    Theme m_theme;
    bool m_isNew;
    bool m_dirty = false;

    wxTextCtrl *m_name;
    wxComboBox *m_bgType;
    wxColourPickerCtrl *m_col1;
    wxColourPickerCtrl *m_col2;
    wxSpinCtrl *m_angle;
    wxTextCtrl *m_imagePath;
    wxComboBox *m_fit;
    wxFontPickerCtrl *m_titleFont;
    wxFontPickerCtrl *m_bodyFont;
    wxColourPickerCtrl *m_titleCol;
    wxColourPickerCtrl *m_bodyCol;
    wxSpinCtrl *m_titleSize;
    wxSpinCtrl *m_bodySize;
    wxSpinCtrl *m_outline;
    wxCheckBox *m_shadow;
    wxComboBox *m_align;
    wxCheckBox *m_upper;
    PreviewCanvas *m_preview;
    wxStaticText *m_wcag;
    wxButton *m_okBtn;
    Slide m_sample;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_THEMEEDITOR_H
