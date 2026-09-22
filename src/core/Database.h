// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Database.h : Acceso SQLite (amalgamation estatica + FTS5) — canciones,
//  biblia, temas, biblioteca de medios, cultos, ajustes y estadisticas.
//  Port de la edicion Qt v1.6.0 (mismo esquema, mismo indice FTS con
//  remove_diacritics, mismos triggers).
// ============================================================================
#ifndef LUMINA_DATABASE_H
#define LUMINA_DATABASE_H

#include "Types.h"

#include <sqlite3.h>

#include <functional>
#include <vector>

// ---------------------------------------------------------------------------
// Guard RAII de statements (StmtGuard del port Qt)
// ---------------------------------------------------------------------------
class StmtGuard
{
public:
    explicit StmtGuard(sqlite3_stmt *st) : m_st(st) {}
    ~StmtGuard() { if (m_st) sqlite3_finalize(m_st); }
    StmtGuard(const StmtGuard &) = delete;
    StmtGuard &operator=(const StmtGuard &) = delete;
    sqlite3_stmt *get() const { return m_st; }
private:
    sqlite3_stmt *m_st;
};

class Database
{
public:
    Database() = default;
    ~Database();

    Database(const Database &) = delete;
    Database &operator=(const Database &) = delete;

    bool Open(const wxString &path, wxString *error = nullptr);
    void Close();
    bool IsOpen() const { return m_db != nullptr; }

    // --- Canciones ----------------------------------------------------------
    int  AddSong(const Song &s, wxString *error = nullptr);
    bool UpdateSong(const Song &s);
    bool DeleteSong(int id);
    Song SongById(int id);
    struct SearchFilter { wxString q; wxString tag; };
    std::vector<Song> SearchSongs(const SearchFilter &f, int limit = 500);
    void LogSongUse(int songId);
    int  SongUseCount(int songId);

    // --- Tags (canciones, temas y fondos) -----------------------------------
    std::vector<wxString> AllTagNames();
    void SetSongTags(int songId, const std::vector<wxString> &tags);
    std::vector<wxString> SongTags(int songId);
    void SetResourceTags(const wxString &kind, const wxString &key, const std::vector<wxString> &tags);
    std::vector<wxString> ResourceTags(const wxString &kind, const wxString &key);

    // --- Biblia -------------------------------------------------------------
    struct BibleRow { int book, chapter, verse; wxString text; };
    bool ImportBible(const wxString &version, const wxString &name,
                     const std::vector<BibleRow> &rows, wxString *error = nullptr,
                     const std::function<bool(int, int)> &progress = {});
    std::vector<wxString> BibleVersions();
    bool BibleHasData();
    int  BibleVerseCount(const wxString &version);
    std::vector<BibleRow> BiblePassage(const wxString &version, int book, int chapter,
                                       int verseFrom, int verseTo);
    std::vector<BibleRow> BibleSearch(const wxString &query, const wxString &version, int limit = 300);

    // --- Temas --------------------------------------------------------------
    int  SaveTheme(const Theme &t);              // inserta o actualiza
    bool DeleteTheme(int id);
    std::vector<Theme> Themes();
    Theme ThemeById(int id);
    void SetDefaultTheme(int id);
    int  DefaultThemeId();

    // --- Biblioteca de medios ------------------------------------------------
    bool AddMedia(const wxString &path, int kind);
    bool RemoveMedia(const wxString &path);
    std::vector<MediaRow> MediaLibrary();

    // --- Cultos (playlists) ---------------------------------------------------
    int  CreatePlaylist(const wxString &name);
    bool DeletePlaylist(int id);
    std::vector<std::pair<int, wxString>> Playlists();
    void ReplaceItems(int playlistId, const std::vector<ServiceItem> &items);
    std::vector<ServiceItem> PlaylistItems(int playlistId);

    // --- Ajustes --------------------------------------------------------------
    wxString GetSetting(const wxString &key, const wxString &def = wxString());
    void SetSetting(const wxString &key, const wxString &value);
    int  GetSettingInt(const wxString &key, int def = 0);
    void SetSettingInt(const wxString &key, int value);

    // --- Utilidades ------------------------------------------------------------
    bool Exec(const wxString &sql, wxString *error = nullptr);
    wxString LastError() const { return m_lastError; }
    bool BackupToFile(const wxString &destPath);

private:
    sqlite3_stmt *Prepare(const wxString &sql);
    bool BindText(sqlite3_stmt *st, int idx, const wxString &v);
    wxString ColumnText(sqlite3_stmt *st, int col);
    bool StepDone(sqlite3_stmt *st);
    bool Begin();
    bool Commit();
    void Rollback();
    bool EnsureSchema(wxString *error = nullptr);
    void SeedDefaults();

    sqlite3 *m_db = nullptr;
    wxString m_lastError;
};

#endif // LUMINA_DATABASE_H
