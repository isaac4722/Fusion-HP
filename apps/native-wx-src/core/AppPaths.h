// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  AppPaths.h : Localizacion de datos (modo portable primero) y ajustes de
//  interfaz persistidos con wxConfig.
//
//  Regla de datos: si junto al exe existe (o puede crearse) la carpeta
//  "Data", TODO el vault vive ahi (USB, carpeta copiada, cero instalaciones).
//  Si no es escribible, se usa %APPDATA%/LuminaPresentationSuite.
// ============================================================================
#ifndef LUMINA_APPPATHS_H
#define LUMINA_APPPATHS_H

#include <wx/app.h>
#include <wx/config.h>
#include <wx/dir.h>
#include <wx/filefn.h>
#include <wx/filename.h>
#include <wx/stdpaths.h>

class AppPaths
{
public:
    static wxString ExeDir()
    {
        const wxString exe = wxStandardPaths::Get().GetExecutablePath();
        return wxFileName(exe).GetPath();
    }

    static wxString DataDir()
    {
        static wxString cached;
        if (!cached.empty())
            return cached;
        const wxString candidate = ExeDir() + wxFileName::GetPathSeparator() + "Data";
        if (wxDirExists(candidate) || wxMkdir(candidate)) {
            cached = candidate;
        } else {
            cached = wxStandardPaths::Get().GetUserDataDir();
            if (!wxDirExists(cached))
                wxMkdir(cached);
        }
        return cached;
    }

    static wxString DbPath()       { return DataDir() + wxS("/") + "lumina.sqlite3"; }
    static wxString BackupDir()    { return DataDir() + wxS("/") + "backups"; }
    static wxString ServicePath()  { return DataDir() + wxS("/") + "culto_actual.json"; }

    // Biblia RVR1909 incluida junto al exe (resources/data en el paquete)
    static wxString BundledBiblePath()
    {
        const wxString sep = wxFileName::GetPathSeparator();
        const wxString candidates[] = {
            ExeDir() + sep + "resources" + sep + "data" + sep + "bible_rvr1909.json",
            ExeDir() + sep + "data" + sep + "bible_rvr1909.json",
            ExeDir() + sep + "bible_rvr1909.json",
        };
        for (const wxString &c : candidates) {
            if (wxFileExists(c))
                return c;
        }
        return wxString();
    }

    // Logo por defecto para el modo LOGO (Data/logo.png > resources/img/logo.png)
    static wxString LogoPath()
    {
        const wxString sep = wxFileName::GetPathSeparator();
        const wxString candidates[] = {
            DataDir() + sep + "logo.png",
            ExeDir() + sep + "resources" + sep + "img" + sep + "logo.png",
            ExeDir() + sep + "resources" + sep + "img" + sep + "logo128.png",
        };
        for (const wxString &c : candidates) {
            if (wxFileExists(c))
                return c;
        }
        return wxString();
    }

    // --- Ajustes de UI (wxConfig; el vault usa la tabla settings de la BD) ---
    static wxConfig *Cfg()
    {
        static wxConfig cfg("LuminaPresentationSuite", "Isaac");
        return &cfg;
    }

    static wxString CfgStr(const wxString &key, const wxString &def = wxString())
    {
        return Cfg()->Read(key, def);
    }

    static void SetCfgStr(const wxString &key, const wxString &value)
    {
        Cfg()->Write(key, value);
        Cfg()->Flush();
    }

    static long CfgInt(const wxString &key, long def = 0)
    {
        return Cfg()->ReadLong(key, def);
    }

    static void SetCfgInt(const wxString &key, long value)
    {
        Cfg()->Write(key, value);
        Cfg()->Flush();
    }
};

#endif // LUMINA_APPPATHS_H
