// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  SettingsDialog.h : Ajustes — salida (pantalla), servidor remoto (puerto y
//  token), datos (carpeta y respaldo). Persiste en la tabla settings + wxConfig.
// ============================================================================
#ifndef LUMINA_SETTINGSDIALOG_H
#define LUMINA_SETTINGSDIALOG_H

#include <wx/dialog.h>

class wxCheckBox;
class wxComboBox;
class wxSpinCtrl;
class wxTextCtrl;
class Database;

class SettingsDialog : public wxDialog
{
public:
    SettingsDialog(wxWindow *parent, Database &db);

    bool ServerEnabled() const { return m_serverOn; }
    int  ServerPort() const { return m_port; }
    wxString ServerToken() const { return m_token; }
    int  OutputScreen() const { return m_screen; }

private:
    void OnOk(wxCommandEvent &e);
    void OnOpenFolder(wxCommandEvent &e);
    void OnBackupNow(wxCommandEvent &e);

    Database &m_db;
    bool m_serverOn = false;
    int m_port = 8088;
    int m_screen = -1;
    wxString m_token;

    wxCheckBox *m_serverCheck;
    wxSpinCtrl *m_portSpin;
    wxTextCtrl *m_tokenCtrl;
    wxComboBox *m_screenCombo;
    wxCheckBox *m_counterCheck;
    wxSpinCtrl *m_linesSpin;
    wxCheckBox *m_hymnCheck;

    wxDECLARE_EVENT_TABLE();
};

#endif // LUMINA_SETTINGSDIALOG_H
