// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  ThemeEditor.cpp : implementacion (ver .h)
// ============================================================================
#include "ThemeEditor.h"
#include "PreviewPanel.h"
#include "../core/Database.h"
#include "../core/Renderer.h"

#include <wx/button.h>
#include <wx/checkbox.h>
#include <wx/clrpicker.h>
#include <wx/msgdlg.h>
#include <wx/combobox.h>
#include <wx/filedlg.h>
#include <wx/fontpicker.h>
#include <wx/spinctrl.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/textctrl.h>

namespace {
enum {
    ID_BGTYPE = wxID_HIGHEST + 800,
    ID_FIT,
    ID_ALIGN,
    ID_PICKIMG,
    ID_COL1,
    ID_COL2,
    ID_TCOL,
    ID_BCOL,
    ID_ANGLE,
    ID_TSIZE,
    ID_BSIZE,
    ID_OUTLINE,
    ID_SHADOW,
    ID_UPPER,
};
} // namespace

BEGIN_EVENT_TABLE(ThemeEditor, wxDialog)
    EVT_COMBOBOX(ID_BGTYPE, ThemeEditor::OnBgType)
    EVT_COMBOBOX(ID_FIT, ThemeEditor::OnChange)
    EVT_COMBOBOX(ID_ALIGN, ThemeEditor::OnChange)
    EVT_BUTTON(ID_PICKIMG, ThemeEditor::OnPickImage)
    EVT_COLOURPICKER_CHANGED(ID_COL1, ThemeEditor::OnColour)
    EVT_COLOURPICKER_CHANGED(ID_COL2, ThemeEditor::OnColour)
    EVT_COLOURPICKER_CHANGED(ID_TCOL, ThemeEditor::OnColour)
    EVT_COLOURPICKER_CHANGED(ID_BCOL, ThemeEditor::OnColour)
    EVT_SPINCTRL(ID_ANGLE, ThemeEditor::OnChange)
    EVT_SPINCTRL(ID_TSIZE, ThemeEditor::OnChange)
    EVT_SPINCTRL(ID_BSIZE, ThemeEditor::OnChange)
    EVT_SPINCTRL(ID_OUTLINE, ThemeEditor::OnChange)
    EVT_CHECKBOX(ID_SHADOW, ThemeEditor::OnChange)
    EVT_CHECKBOX(ID_UPPER, ThemeEditor::OnChange)
    EVT_BUTTON(wxID_OK, ThemeEditor::OnOk)
    EVT_IDLE(ThemeEditor::OnIdle)
END_EVENT_TABLE()

namespace {

Slide SampleSlide()
{
    Slide s;
    s.kind = Slide::Text;
    s.title = L"Grande es el Señor";
    s.refLabel = "[Coro]";
    s.lines = { SlideLine("Tu nombre es digno de alabar"),
                SlideLine("Tu poder y bondad proclamar"),
                SlideLine("Toda la tierra te adora, Dios"),
                SlideLine(L"Grande es el Señor") };
    return s;
}
}

ThemeEditor::ThemeEditor(wxWindow *parent, Database &db, const Theme &theme, bool isNew)
    : wxDialog(parent, wxID_ANY, isNew ? "Nuevo tema" : "Editar tema",
               wxDefaultPosition, wxSize(940, 620),
               wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER),
      m_db(db), m_theme(theme), m_isNew(isNew)
{
    m_sample = SampleSlide();
    auto *root = new wxBoxSizer(wxVERTICAL);

    // Nombre
    auto *nameRow = new wxBoxSizer(wxHORIZONTAL);
    nameRow->Add(new wxStaticText(this, wxID_ANY, "Nombre:"), 0,
                 wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_name = new wxTextCtrl(this, wxID_ANY, m_theme.name);
    nameRow->Add(m_name, 1, wxEXPAND);
    root->Add(nameRow, 0, wxEXPAND | wxALL, 10);

    auto *split = new wxBoxSizer(wxHORIZONTAL);

    // --- Columna izquierda: controles ------------------------------------------
    auto *left = new wxBoxSizer(wxVERTICAL);

    // Fondo
    auto *bgBox = new wxStaticBoxSizer(wxVERTICAL, this, "Fondo");
    auto *r1 = new wxBoxSizer(wxHORIZONTAL);
    r1->Add(new wxStaticText(this, wxID_ANY, "Tipo:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_bgType = new wxComboBox(this, ID_BGTYPE, wxString(), wxDefaultPosition, wxDefaultSize,
                              0, nullptr, wxCB_READONLY);
    m_bgType->Append(L"Color sólido");
    m_bgType->Append("Gradiente");
    m_bgType->Append("Imagen");
    m_bgType->SetSelection(m_theme.background.type);
    r1->Add(m_bgType, 1, wxEXPAND);
    bgBox->Add(r1, 0, wxEXPAND | wxBOTTOM, 6);

    auto *r2 = new wxBoxSizer(wxHORIZONTAL);
    r2->Add(new wxStaticText(this, wxID_ANY, "Color 1:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_col1 = new wxColourPickerCtrl(this, ID_COL1, m_theme.background.color1);
    r2->Add(m_col1, 0, wxRIGHT, 10);
    r2->Add(new wxStaticText(this, wxID_ANY, "Color 2:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_col2 = new wxColourPickerCtrl(this, ID_COL2, m_theme.background.color2);
    r2->Add(m_col2, 0, wxRIGHT, 10);
    r2->Add(new wxStaticText(this, wxID_ANY, L"Ángulo:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_angle = new wxSpinCtrl(this, ID_ANGLE, wxString(), wxDefaultPosition, wxDefaultSize,
                             wxSP_ARROW_KEYS, 0, 360, m_theme.background.gradientAngle);
    r2->Add(m_angle);
    bgBox->Add(r2, 0, wxBOTTOM, 6);

    auto *r3 = new wxBoxSizer(wxHORIZONTAL);
    m_imagePath = new wxTextCtrl(this, wxID_ANY, m_theme.background.imagePath);
    auto *pick = new wxButton(this, ID_PICKIMG, L"…");
    pick->SetToolTip("Elegir imagen de fondo");
    r3->Add(m_imagePath, 1, wxEXPAND | wxRIGHT, 6);
    r3->Add(pick, 0);
    bgBox->Add(r3, 0, wxEXPAND | wxBOTTOM, 6);

    auto *r4 = new wxBoxSizer(wxHORIZONTAL);
    r4->Add(new wxStaticText(this, wxID_ANY, "Ajuste:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_fit = new wxComboBox(this, ID_FIT, wxString(), wxDefaultPosition, wxDefaultSize,
                           0, nullptr, wxCB_READONLY);
    m_fit->Append("Llenar (recorta)");
    m_fit->Append("Ajustar (completo)");
    m_fit->SetSelection(m_theme.background.imageFit);
    r4->Add(m_fit);
    bgBox->Add(r4);
    left->Add(bgBox, 0, wxEXPAND | wxBOTTOM, 10);

    // Titulo
    auto *tBox = new wxStaticBoxSizer(wxVERTICAL, this, L"Título");
    auto *tr1 = new wxBoxSizer(wxHORIZONTAL);
    tr1->Add(new wxStaticText(this, wxID_ANY, "Fuente:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_titleFont = new wxFontPickerCtrl(this, wxID_ANY,
        wxFont(wxFontInfo(m_theme.title.pointSize >= 12 ? m_theme.title.pointSize : 40).FaceName(m_theme.title.family).Bold()));
    tr1->Add(m_titleFont, 1, wxEXPAND | wxRIGHT, 8);
    tr1->Add(new wxStaticText(this, wxID_ANY, "Color:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_titleCol = new wxColourPickerCtrl(this, ID_TCOL, m_theme.title.color);
    tr1->Add(m_titleCol);
    tBox->Add(tr1, 0, wxEXPAND | wxBOTTOM, 4);
    auto *tr2 = new wxBoxSizer(wxHORIZONTAL);
    tr2->Add(new wxStaticText(this, wxID_ANY, L"Tamaño:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_titleSize = new wxSpinCtrl(this, ID_TSIZE, wxString(), wxDefaultPosition, wxDefaultSize,
                                 wxSP_ARROW_KEYS, 12, 120, m_theme.title.pointSize);
    tr2->Add(m_titleSize, 0, wxRIGHT, 8);
    tr2->Add(new wxStaticText(this, wxID_ANY, "(pt @1080p)"));
    tBox->Add(tr2);
    left->Add(tBox, 0, wxEXPAND | wxBOTTOM, 10);

    // Cuerpo
    auto *bBox = new wxStaticBoxSizer(wxVERTICAL, this, "Texto");
    auto *br1 = new wxBoxSizer(wxHORIZONTAL);
    br1->Add(new wxStaticText(this, wxID_ANY, "Fuente:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_bodyFont = new wxFontPickerCtrl(this, wxID_ANY,
        wxFont(wxFontInfo(m_theme.body.pointSize >= 12 ? m_theme.body.pointSize : 54).FaceName(m_theme.body.family).Bold()));
    br1->Add(m_bodyFont, 1, wxEXPAND | wxRIGHT, 8);
    br1->Add(new wxStaticText(this, wxID_ANY, "Color:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_bodyCol = new wxColourPickerCtrl(this, ID_BCOL, m_theme.body.color);
    br1->Add(m_bodyCol);
    bBox->Add(br1, 0, wxEXPAND | wxBOTTOM, 4);

    auto *br2 = new wxBoxSizer(wxHORIZONTAL);
    br2->Add(new wxStaticText(this, wxID_ANY, L"Tamaño:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_bodySize = new wxSpinCtrl(this, ID_BSIZE, wxString(), wxDefaultPosition, wxDefaultSize,
                                wxSP_ARROW_KEYS, 12, 140, m_theme.body.pointSize);
    br2->Add(m_bodySize, 0, wxRIGHT, 8);
    br2->Add(new wxStaticText(this, wxID_ANY, "Contorno:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_outline = new wxSpinCtrl(this, ID_OUTLINE, wxString(), wxDefaultPosition, wxDefaultSize,
                               wxSP_ARROW_KEYS, 0, 8, m_theme.body.outlineWidth);
    br2->Add(m_outline, 0, wxRIGHT, 8);
    m_shadow = new wxCheckBox(this, ID_SHADOW, "Sombra");
    m_shadow->SetValue(m_theme.body.shadow);
    br2->Add(m_shadow, 0, wxRIGHT, 8);
    br2->Add(new wxStaticText(this, wxID_ANY, "Alinear:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_align = new wxComboBox(this, ID_ALIGN, wxString(), wxDefaultPosition, wxDefaultSize,
                             0, nullptr, wxCB_READONLY);
    m_align->Append("Centro");
    m_align->Append("Izquierda");
    m_align->Append("Derecha");
    m_align->SetSelection(m_theme.body.align);
    br2->Add(m_align, 0, wxRIGHT, 8);
    m_upper = new wxCheckBox(this, ID_UPPER, L"MAYÚSCULAS");
    m_upper->SetValue(m_theme.body.upperCase);
    br2->Add(m_upper);
    bBox->Add(br2);
    left->Add(bBox, 0, wxEXPAND);

    // WCAG
    m_wcag = new wxStaticText(this, wxID_ANY, "");
    left->Add(m_wcag, 0, wxTOP, 10);

    split->Add(left, 0, wxEXPAND | wxRIGHT, 12);

    // --- Columna derecha: preview ---------------------------------------------
    auto *right = new wxBoxSizer(wxVERTICAL);
    right->Add(new wxStaticText(this, wxID_ANY, L"Previsualización (16:9):"), 0, wxBOTTOM, 4);
    m_preview = new PreviewCanvas(this, false);
    right->Add(m_preview, 1, wxEXPAND);
    split->Add(right, 1, wxEXPAND);

    root->Add(split, 1, wxEXPAND | wxLEFT | wxRIGHT, 10);

    auto *btns = new wxStdDialogButtonSizer();
    m_okBtn = new wxButton(this, wxID_OK, "Guardar");
    m_okBtn->SetDefault();
    btns->AddButton(m_okBtn);
    btns->AddButton(new wxButton(this, wxID_CANCEL, "Cancelar"));
    btns->Realize();
    root->Add(btns, 0, wxALIGN_RIGHT | wxALL, 10);

    SetSizer(root);
    RefreshPreview();
}

// ---------------------------------------------------------------------------
// Sincronizacion UI -> modelo
// ---------------------------------------------------------------------------
void ThemeEditor::OnBgType(wxCommandEvent &)
{
    m_theme.background.type = m_bgType->GetSelection();
    m_dirty = true;
    RefreshPreview();
}

void ThemeEditor::OnPickImage(wxCommandEvent &)
{
    wxFileDialog dlg(this, "Imagen de fondo", wxString(), wxString(),
                     L"Imágenes|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp",
                     wxFD_OPEN | wxFD_FILE_MUST_EXIST);
    if (dlg.ShowModal() == wxID_OK) {
        m_imagePath->ChangeValue(dlg.GetPath());
        m_theme.background.imagePath = dlg.GetPath();
        if (m_bgType->GetSelection() != 2) {
            m_bgType->SetSelection(2);
            m_theme.background.type = 2;
        }
        m_dirty = true;
        RefreshPreview();
    }
}

void ThemeEditor::OnChange(wxCommandEvent &) { RefreshPreview(); }
void ThemeEditor::OnChange(wxSpinEvent &)    { RefreshPreview(); }
void ThemeEditor::OnColour(wxColourPickerEvent &e) { OnChange(e); }

void ThemeEditor::OnIdle(wxIdleEvent &)
{
    // Poll barato de spinners (no emiten eventos combinables en GTK)
    static int lastAngle = -1, lastTs = -1, lastBs = -1, lastOut = -1;
    bool changed = false;
    if (m_angle->GetValue() != lastAngle) { lastAngle = m_angle->GetValue(); changed = true; }
    if (m_titleSize->GetValue() != lastTs) { lastTs = m_titleSize->GetValue(); changed = true; }
    if (m_bodySize->GetValue() != lastBs)  { lastBs = m_bodySize->GetValue(); changed = true; }
    if (m_outline->GetValue() != lastOut)  { lastOut = m_outline->GetValue(); changed = true; }
    if (changed)
        RefreshPreview();
}

void ThemeEditor::RefreshPreview()
{
    // Modelo desde la UI
    m_theme.name = m_name->GetValue().Trim(true).Trim(false);
    m_theme.background.color1 = m_col1->GetColour();
    m_theme.background.color2 = m_col2->GetColour();
    m_theme.background.gradientAngle = m_angle->GetValue();
    m_theme.background.imagePath = m_imagePath->GetValue().Trim().Trim(false);
    m_theme.background.imageFit = m_fit->GetSelection();

    const wxFont tf = m_titleFont->GetSelectedFont();
    if (tf.IsOk()) {
        m_theme.title.family = tf.GetFaceName();
        m_theme.title.bold = tf.GetWeight() == wxFONTWEIGHT_BOLD;
    }
    m_theme.title.pointSize = m_titleSize->GetValue();
    m_theme.title.color = m_titleCol->GetColour();
    m_theme.title.outlineWidth = 2;

    const wxFont bf = m_bodyFont->GetSelectedFont();
    if (bf.IsOk()) {
        m_theme.body.family = bf.GetFaceName();
        m_theme.body.bold = bf.GetWeight() == wxFONTWEIGHT_BOLD;
    }
    m_theme.body.pointSize = m_bodySize->GetValue();
    m_theme.body.color = m_bodyCol->GetColour();
    m_theme.body.outlineWidth = m_outline->GetValue();
    m_theme.body.shadow = m_shadow->GetValue();
    m_theme.body.align = m_align->GetSelection();
    m_theme.body.upperCase = m_upper->GetValue();

    // WCAG (texto vs fondo peor caso)
    const wxString lvl = Renderer::ContrastLevel(m_theme.body.color,
                                                 m_theme.background.color1);
    const double ratio = Renderer::ContrastRatio(m_theme.body.color,
                                                 m_theme.background.color1);
    if (lvl == "ok")
        m_wcag->SetLabel(wxString::Format(L"♿ Contraste: EXCELENTE (%.1f:1, AAA)", ratio));
    else if (lvl == "warn")
        m_wcag->SetLabel(wxString::Format(L"♿ Contraste: ACEPTABLE (%.1f:1, AA)", ratio));
    else
        m_wcag->SetLabel(wxString::Format(L"♿ Contraste: BAJO (%.1f:1) — sube el contorno o cambia colores", ratio));
    m_wcag->SetForegroundColour(lvl == "ok" ? wxColour(63, 191, 127)
                              : lvl == "warn" ? wxColour(230, 168, 42)
                                              : wxColour(224, 92, 92));

    m_preview->SetSlide(&m_sample, &m_theme, 0, 0);
}

void ThemeEditor::OnOk(wxCommandEvent &)
{
    RefreshPreview();
    if (m_theme.name.empty()) {
        wxMessageDialog msg(this, "El nombre del tema es obligatorio.", "Tema",
                            wxOK | wxICON_INFORMATION);
        msg.ShowModal();
        return;
    }
    m_theme.id = m_db.SaveTheme(m_theme);
    EndModal(wxID_OK);
}
