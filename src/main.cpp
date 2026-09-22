// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  main.cpp : Punto de entrada wxWidgets. Aplicacion NATIVA de escritorio
//  (sin dependencias de instalacion: wxWidgets estatico + SQLite embebido).
//  Arranque: instancia unica -> BD -> semillas (tema, biblia RVR1909 incluida)
//  -> ventana principal.
// ============================================================================
#include "core/AppPaths.h"
#include "core/Database.h"
#include "core/Types.h"
#include "ui/MainFrame.h"

#include <wx/app.h>
#include <wx/dynlib.h>
#include <wx/file.h>
#include <wx/image.h>
#include <wx/log.h>
#include <wx/msgdlg.h>
#include <wx/snglinst.h>

#include <nlohmann/json.hpp>

class LuminaApp : public wxApp
{
public:
    bool OnInit() override
    {
        wxLog::SetActiveTarget(new wxLogStderr());

        // Handlers de imagen (PNG/JPG/GIF/BMP/TIFF/WebP)
        wxInitAllImageHandlers();

        SetAppName("LuminaPresentationSuite");
        SetAppDisplayName("LuminaPresentation Suite");
        SetVendorName("Isaac");

        // Instancia unica por equipo
        static wxSingleInstanceChecker checker;
        if (checker.Create("LuminaPresentationSuite-" + wxGetUserId()) &&
            checker.IsAnotherRunning()) {
            wxMessageBox(L"LuminaPresentation Suite ya está en ejecución.",
                         "LuminaPresentation", wxOK | wxICON_INFORMATION);
            return false;
        }

        // Base de datos (modo portable: carpeta Data junto al exe)
        wxString err;
        if (!wxDirExists(AppPaths::DataDir()))
            wxMkdir(AppPaths::DataDir());
        if (!m_db.Open(AppPaths::DbPath(), &err)) {
            wxMessageBox("No se pudo abrir la base de datos:\n" + err +
                             "\n\nRuta: " + AppPaths::DbPath(),
                         "Error de datos", wxOK | wxICON_ERROR);
            return false;
        }

        SeedFirstRun();

        m_frame = new MainFrame(m_db);
        m_frame->Show(true);
        return true;
    }

    void OnUnhandledException() override
    {
        try { throw; }
        catch (const std::exception &e) {
            wxLogError(L"Excepción no manejada: %s", e.what());
            wxMessageBox(wxString::Format("Error inesperado:\n%s", e.what()),
                         "LuminaPresentation", wxOK | wxICON_ERROR);
        }
        catch (...) {
            wxMessageBox("Error inesperado desconocido.", "LuminaPresentation",
                         wxOK | wxICON_ERROR);
        }
    }

    int OnExit() override
    {
        m_db.Close();
        return wxApp::OnExit();
    }

private:
    // Primera ejecucion: cancion de ejemplo + Biblia RVR1909 incluida
    void SeedFirstRun()
    {
        if (m_db.GetSettingInt("seeded", 0) != 0)
            return;

        Song demo;
        demo.title = L"Grande es el Señor";
        demo.artist = "Tradicional";
        demo.key = "Do";
        demo.bpm = 72;
        demo.lyrics =
            "[Verso 1]\n"
            "Grande es el Señor y digno de alabar\n"
            "En la ciudad de nuestro Dios\n\n"
            "[Coro]\n"
            "Santo, santo, santo\n"
            "Digno es el Cordero\n\n"
            "[Verso 2]\n"
            "Toda lengua confesará\n"
            L"Que Él es el Rey de gloria";
        const int id = m_db.AddSong(demo);
        m_db.SetSongTags(id, { "entrada", L"adoración" });

        // Biblia incluida (resources/data/bible_rvr1909.json junto al exe)
        const wxString bible = AppPaths::BundledBiblePath();
        if (!bible.empty()) {
            wxFile f(bible);
            if (f.IsOpened()) {
                const wxFileOffset flen = f.Length();
                wxCharBuffer buf(flen);
                f.Read(buf.data(), flen);
                // Parseo directo de los bytes UTF-8 (sin conversion por locale)
                nlohmann::json j = nlohmann::json::parse(buf.data(), buf.data() + flen,
                                                         nullptr, false);
                if (j.is_object() && j.contains("books") && j["books"].is_array()) {
                    wxString version(L"RVR1909");
                    if (j.contains("version") && j["version"].is_string())
                        version = FromUtf8(j["version"].get<std::string>());
                    std::vector<Database::BibleRow> rows;
                    for (const auto &b : j["books"]) {
                        if (!b.is_object() || !b.contains("chapters"))
                            continue;
                        const int bookNum = b.value("n", 0);
                        int chap = 0;
                        for (const auto &ch : b["chapters"]) {
                            ++chap;
                            if (!ch.is_array())
                                continue;
                            int verse = 0;
                            for (const auto &v : ch) {
                                ++verse;
                                if (!v.is_string())
                                    continue;
                                Database::BibleRow r;
                                r.book = bookNum;
                                r.chapter = chap;
                                r.verse = verse;
                                r.text = FromUtf8(v.get<std::string>());
                                rows.push_back(r);
                            }
                        }
                    }
                    if (!rows.empty())
                        m_db.ImportBible(version, wxString(j.value("name", "Reina-Valera 1909")),
                                         rows);
                }
            }
        }
        m_db.SetSettingInt("seeded", 1);
    }

    Database m_db;
    MainFrame *m_frame = nullptr;
};

wxIMPLEMENT_APP(LuminaApp);
