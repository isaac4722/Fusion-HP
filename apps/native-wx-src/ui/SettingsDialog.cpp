// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SettingsDialog.cpp : implementacion (ver .h)
// ============================================================================
#include "SettingsDialog.h"
#include "../core/AppPaths.h"
#include "../core/Database.h"

#include <wx/checkbox.h>
#include <wx/button.h>
#include <wx/combobox.h>
#include <wx/dir.h>
#include <wx/display.h>
#include <wx/filedlg.h>
#include <wx/filename.h>
#include <wx/msgdlg.h>
#include <wx/spinctrl.h>
#include <wx/sizer.h>
#include <wx/stattext.h>
#include <wx/textctrl.h>
#include <wx/utils.h>

namespace {
enum { ID_OPENFOLDER = wxID_HIGHEST + 900, ID_BACKUP };
}

BEGIN_EVENT_TABLE(SettingsDialog, wxDialog)
    EVT_BUTTON(wxID_OK, SettingsDialog::OnOk)
    EVT_BUTTON(ID_OPENFOLDER, SettingsDialog::OnOpenFolder)
    EVT_BUTTON(ID_BACKUP, SettingsDialog::OnBackupNow)
END_EVENT_TABLE()


SettingsDialog::SettingsDialog(wxWindow *parent, Database &db)
    : wxDialog(parent, wxID_ANY, "Ajustes", wxDefaultPosition, wxSize(560, 480),
               wxDEFAULT_DIALOG_STYLE | wxRESIZE_BORDER),
      m_db(db)
{
    m_serverOn = db.GetSettingInt("server_on", 1) != 0;
    m_port = db.GetSettingInt("server_port", 8088);
    m_token = db.GetSetting("server_token");
    m_screen = (int)AppPaths::CfgInt("output_screen", -1);

    auto *root = new wxBoxSizer(wxVERTICAL);

    // --- Salida ------------------------------------------------------------------
    auto *outBox = new wxStaticBoxSizer(wxVERTICAL, this, L"Salida (proyección)");
    auto *r1 = new wxBoxSizer(wxHORIZONTAL);
    r1->Add(new wxStaticText(this, wxID_ANY, "Pantalla de salida:"), 0,
            wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_screenCombo = new wxComboBox(this, wxID_ANY, wxString(), wxDefaultPosition,
                                   wxDefaultSize, 0, nullptr, wxCB_READONLY);
    m_screenCombo->Append("Ventana flotante (prueba)");
    for (unsigned i = 0; i < wxDisplay::GetCount(); ++i) {
        wxDisplay d(i);
        m_screenCombo->Append(wxString::Format(L"Pantalla %u — %dx%d", i + 1,
                                               d.GetGeometry().width, d.GetGeometry().height));
    }
    m_screenCombo->SetSelection(m_screen + 1 >= 0 &&
                                m_screen + 1 < (int)m_screenCombo->GetCount()
                                    ? m_screen + 1 : 0);
    r1->Add(m_screenCombo, 1, wxEXPAND);
    outBox->Add(r1, 0, wxEXPAND | wxBOTTOM, 6);

    m_counterCheck = new wxCheckBox(this, wxID_ANY,
        L"Mostrar contador de slide (n/total) en la proyección");
    m_counterCheck->SetValue(db.GetSettingInt("show_counter", 0) != 0);
    outBox->Add(m_counterCheck, 0, wxBOTTOM, 6);

    auto *r2 = new wxBoxSizer(wxHORIZONTAL);
    r2->Add(new wxStaticText(this, wxID_ANY, L"Líneas por slide:"), 0,
            wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_linesSpin = new wxSpinCtrl(this, wxID_ANY, wxString(), wxDefaultPosition, wxDefaultSize,
                                 wxSP_ARROW_KEYS, 2, 8, db.GetSettingInt("lines_per_slide", 4));
    r2->Add(m_linesSpin, 0, wxRIGHT, 14);
    m_hymnCheck = new wxCheckBox(this, wxID_ANY, "Modo Hinario (coro intercalado)");
    m_hymnCheck->SetValue(db.GetSettingInt("hymn_mode", 0) != 0);
    r2->Add(m_hymnCheck, 0, wxALIGN_CENTER_VERTICAL);
    outBox->Add(r2);
    root->Add(outBox, 0, wxEXPAND | wxALL, 10);

    // --- Servidor remoto -----------------------------------------------------------
    auto *srvBox = new wxStaticBoxSizer(wxVERTICAL, this,
        L"Control remoto (móvil / OBS — http://<pc>:puerto)");
    m_serverCheck = new wxCheckBox(this, wxID_ANY, "Servidor activo");
    m_serverCheck->SetValue(m_serverOn);
    srvBox->Add(m_serverCheck, 0, wxBOTTOM, 6);
    auto *r3 = new wxBoxSizer(wxHORIZONTAL);
    r3->Add(new wxStaticText(this, wxID_ANY, "Puerto:"), 0, wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_portSpin = new wxSpinCtrl(this, wxID_ANY, wxString(), wxDefaultPosition, wxDefaultSize,
                                wxSP_ARROW_KEYS, 1024, 65535, m_port > 0 ? m_port : 8088);
    r3->Add(m_portSpin, 0, wxRIGHT, 14);
    r3->Add(new wxStaticText(this, wxID_ANY, "Token (opcional):"), 0,
            wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    m_tokenCtrl = new wxTextCtrl(this, wxID_ANY, m_token);
    m_tokenCtrl->SetHint(L"vacío = sin protección");
    r3->Add(m_tokenCtrl, 1, wxEXPAND);
    srvBox->Add(r3, 0, wxEXPAND);
    srvBox->Add(new wxStaticText(this, wxID_ANY,
        L"Móvil: http://ip-de-este-pc:puerto  ·  OBS: /api/live.txt y /api/state"),
        0, wxTOP, 6);
    root->Add(srvBox, 0, wxEXPAND | wxLEFT | wxRIGHT | wxBOTTOM, 10);

    // --- Datos ------------------------------------------------------------------------
    auto *dataBox = new wxStaticBoxSizer(wxVERTICAL, this, "Datos y respaldo");
    auto *r4 = new wxBoxSizer(wxHORIZONTAL);
    r4->Add(new wxStaticText(this, wxID_ANY, "Carpeta de datos:"), 0,
            wxALIGN_CENTER_VERTICAL | wxRIGHT, 6);
    r4->Add(new wxStaticText(this, wxID_ANY, AppPaths::DataDir()), 1,
            wxALIGN_CENTER_VERTICAL);
    auto *open = new wxButton(this, ID_OPENFOLDER, "Abrir");
    r4->Add(open, 0, wxLEFT, 6);
    dataBox->Add(r4, 0, wxEXPAND | wxBOTTOM, 6);
    dataBox->Add(new wxButton(this, ID_BACKUP, "Crear respaldo ahora"), 0);
    root->Add(dataBox, 0, wxEXPAND | wxLEFT | wxRIGHT | wxBOTTOM, 10);

    auto *btns = new wxStdDialogButtonSizer();
    auto *ok = new wxButton(this, wxID_OK, "Aplicar");
    ok->SetDefault();
    btns->AddButton(ok);
    btns->AddButton(new wxButton(this, wxID_CANCEL, "Cancelar"));
    btns->Realize();
    root->Add(btns, 0, wxALIGN_RIGHT | wxALL, 10);

    SetSizerAndFit(root, true);
}

void SettingsDialog::OnOk(wxCommandEvent &)
{
    m_serverOn = m_serverCheck->GetValue();
    m_port = m_portSpin->GetValue();
    m_token = m_tokenCtrl->GetValue().Trim(true).Trim(false);
    const int sel = m_screenCombo->GetSelection();
    m_screen = sel - 1;    // -1 = ventana flotante

    m_db.SetSettingInt("server_on", m_serverOn ? 1 : 0);
    m_db.SetSettingInt("server_port", m_port);
    m_db.SetSetting("server_token", m_token);
    m_db.SetSettingInt("show_counter", m_counterCheck->GetValue() ? 1 : 0);
    m_db.SetSettingInt("lines_per_slide", m_linesSpin->GetValue());
    m_db.SetSettingInt("hymn_mode", m_hymnCheck->GetValue() ? 1 : 0);
    AppPaths::SetCfgInt("output_screen", m_screen);

    EndModal(wxID_OK);
}

void SettingsDialog::OnOpenFolder(wxCommandEvent &)
{
    wxLaunchDefaultApplication(AppPaths::DataDir());
}

void SettingsDialog::OnBackupNow(wxCommandEvent &)
{
    if (!wxDirExists(AppPaths::BackupDir()))
        wxMkdir(AppPaths::BackupDir());
    const wxString stamp = wxDateTime::Now().Format("%Y%m%d_%H%M%S");
    const wxString dst = AppPaths::BackupDir() + wxFileName::GetPathSeparator() +
                         "respaldo_" + stamp + ".sqlite3";
    if (m_db.BackupToFile(dst))
        wxMessageBox("Respaldo creado:\n" + dst, "Respaldo", wxOK | wxICON_INFORMATION);
    else
        wxMessageBox("No se pudo crear el respaldo:\n" + m_db.LastError(),
                     "Respaldo", wxOK | wxICON_ERROR);
}
