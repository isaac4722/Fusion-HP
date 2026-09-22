// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Database.cpp : Implementacion SQLite (schema + CRUD). Port fiel de la
//  edicion Qt v1.6.0 — incluye el esquema FTS5 con remove_diacritics 2,
//  triggers de sincronizacion y el sistema de tags semanticos.
// ============================================================================
#include "Database.h"

#include <wx/arrstr.h>
#include <wx/datetime.h>
#include <wx/filename.h>
#include <wx/tokenzr.h>

#include <algorithm>

// ---------------------------------------------------------------------------
// Ciclo de vida
// ---------------------------------------------------------------------------
Database::~Database()
{
    Close();
}

bool Database::Open(const wxString &path, wxString *error)
{
    Close();
    if (sqlite3_open_v2(path.utf8_str(), &m_db,
                        SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE, nullptr) != SQLITE_OK) {
        m_lastError = m_db ? wxString::FromUTF8(sqlite3_errmsg(m_db)) : wxString("sin memoria");
        if (m_db) { sqlite3_close(m_db); m_db = nullptr; }
        if (error) *error = m_lastError;
        return false;
    }
    // WAL mejora el rendimiento de escritura y la robustez ante cortes
    Exec("PRAGMA journal_mode=WAL;");
    Exec("PRAGMA foreign_keys=ON;");
    if (!EnsureSchema(error)) {
        Close();
        return false;
    }
    SeedDefaults();
    return true;
}

void Database::Close()
{
    if (m_db) {
        sqlite3_close_v2(m_db);
        m_db = nullptr;
    }
}

// ---------------------------------------------------------------------------
// Primitivas
// ---------------------------------------------------------------------------
sqlite3_stmt *Database::Prepare(const wxString &sql)
{
    if (!m_db)
        return nullptr;
    sqlite3_stmt *st = nullptr;
    if (sqlite3_prepare_v2(m_db, sql.utf8_str(), -1, &st, nullptr) != SQLITE_OK) {
        m_lastError = wxString::FromUTF8(sqlite3_errmsg(m_db));
        return nullptr;
    }
    return st;
}

bool Database::BindText(sqlite3_stmt *st, int idx, const wxString &v)
{
    return sqlite3_bind_text(st, idx, v.utf8_str(), -1, SQLITE_TRANSIENT) == SQLITE_OK;
}

wxString Database::ColumnText(sqlite3_stmt *st, int col)
{
    const unsigned char *txt = sqlite3_column_text(st, col);
    return txt ? wxString::FromUTF8(reinterpret_cast<const char *>(txt)) : wxString();
}

bool Database::StepDone(sqlite3_stmt *st)
{
    const int rc = sqlite3_step(st);
    if (rc == SQLITE_DONE || rc == SQLITE_ROW)
        return true;
    m_lastError = wxString::FromUTF8(sqlite3_errmsg(m_db));
    return false;
}

bool Database::Exec(const wxString &sql, wxString *error)
{
    if (!m_db)
        return false;
    char *err = nullptr;
    if (sqlite3_exec(m_db, sql.utf8_str(), nullptr, nullptr, &err) != SQLITE_OK) {
        m_lastError = err ? wxString::FromUTF8(err) : wxString::FromUTF8(sqlite3_errmsg(m_db));
        if (err) sqlite3_free(err);
        if (error) *error = m_lastError;
        return false;
    }
    return true;
}

bool Database::Begin()
{
    return Exec("BEGIN IMMEDIATE;");
}

bool Database::Commit()
{
    return Exec("COMMIT;");
}

void Database::Rollback()
{
    Exec("ROLLBACK;");
}

// ---------------------------------------------------------------------------
// Esquema (identico al port Qt v1.6.0)
// ---------------------------------------------------------------------------
bool Database::EnsureSchema(wxString *error)
{
    static const char *SCHEMA = R"SQL(
CREATE TABLE IF NOT EXISTS songs (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    title TEXT NOT NULL,
    artist TEXT DEFAULT '',
    key TEXT DEFAULT '',
    bpm INTEGER DEFAULT 0,
    lyrics TEXT DEFAULT '',
    updated_at TEXT
);
CREATE VIRTUAL TABLE IF NOT EXISTS songs_fts USING fts5(
    title, artist, lyrics, content='songs', content_rowid='id', tokenize='unicode61 remove_diacritics 2'
);
CREATE TRIGGER IF NOT EXISTS songs_ai AFTER INSERT ON songs BEGIN
    INSERT INTO songs_fts(rowid, title, artist, lyrics) VALUES (new.id, new.title, new.artist, new.lyrics);
END;
CREATE TRIGGER IF NOT EXISTS songs_ad AFTER DELETE ON songs BEGIN
    INSERT INTO songs_fts(songs_fts, rowid, title, artist, lyrics) VALUES ('delete', old.id, old.title, old.artist, old.lyrics);
END;
CREATE TRIGGER IF NOT EXISTS songs_au AFTER UPDATE ON songs BEGIN
    INSERT INTO songs_fts(songs_fts, rowid, title, artist, lyrics) VALUES ('delete', old.id, old.title, old.artist, old.lyrics);
    INSERT INTO songs_fts(rowid, title, artist, lyrics) VALUES (new.id, new.title, new.artist, new.lyrics);
END;

CREATE TABLE IF NOT EXISTS bible (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    version TEXT NOT NULL,
    book INTEGER NOT NULL,
    chapter INTEGER NOT NULL,
    verse INTEGER NOT NULL,
    text TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS idx_bible_lookup ON bible(version, book, chapter, verse);
CREATE VIRTUAL TABLE IF NOT EXISTS bible_fts USING fts5(
    text, version UNINDEXED, content='bible', content_rowid='id', tokenize='unicode61 remove_diacritics 2'
);
CREATE TRIGGER IF NOT EXISTS bible_ai AFTER INSERT ON bible BEGIN
    INSERT INTO bible_fts(rowid, text, version) VALUES (new.id, new.text, new.version);
END;
CREATE TRIGGER IF NOT EXISTS bible_ad AFTER DELETE ON bible BEGIN
    INSERT INTO bible_fts(songs_fts, rowid, text, version) VALUES ('delete', old.id, old.text, old.version);
END;

CREATE TABLE IF NOT EXISTS playlists (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    created_at TEXT
);
CREATE TABLE IF NOT EXISTS playlist_items (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    playlist_id INTEGER NOT NULL,
    position INTEGER NOT NULL,
    kind INTEGER NOT NULL,
    ref_id INTEGER DEFAULT 0,
    label TEXT,
    payload TEXT
);
CREATE TABLE IF NOT EXISTS themes (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL,
    json TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS history (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    song_id INTEGER,
    used_at TEXT
);
CREATE TABLE IF NOT EXISTS song_stats (
    song_id INTEGER PRIMARY KEY,
    use_count INTEGER DEFAULT 0,
    last_used TEXT
);
CREATE TABLE IF NOT EXISTS settings (
    key TEXT PRIMARY KEY,
    value TEXT
);
CREATE TABLE IF NOT EXISTS alerts_log (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    text TEXT,
    created_at TEXT
);
CREATE TABLE IF NOT EXISTS tags (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT UNIQUE NOT NULL COLLATE NOCASE
);
CREATE TABLE IF NOT EXISTS song_tags (
    song_id INTEGER NOT NULL,
    tag_id  INTEGER NOT NULL,
    PRIMARY KEY(song_id, tag_id),
    FOREIGN KEY(song_id) REFERENCES songs(id) ON DELETE CASCADE,
    FOREIGN KEY(tag_id)  REFERENCES tags(id)  ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_song_tags_tag ON song_tags(tag_id);
CREATE TABLE IF NOT EXISTS resource_tags (
    kind   TEXT NOT NULL,
    key    TEXT NOT NULL,
    tag_id INTEGER NOT NULL,
    PRIMARY KEY(kind, key, tag_id),
    FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS idx_resource_tags_tag ON resource_tags(tag_id);
CREATE TABLE IF NOT EXISTS media (
    path     TEXT PRIMARY KEY,
    kind     INTEGER NOT NULL,
    added_at TEXT
);
CREATE TABLE IF NOT EXISTS tag_rules (
    id       INTEGER PRIMARY KEY AUTOINCREMENT,
    song_tag TEXT NOT NULL COLLATE NOCASE,
    theme_id INTEGER NOT NULL,
    bg_tag   TEXT DEFAULT '' COLLATE NOCASE,
    enabled  INTEGER DEFAULT 1
);
)SQL";
    // Correccion M21 heredada: el trigger bible_ad referenciaba songs_fts; se
    // crea corregido y ademas se repara instalaciones antiguas si aplicara.
    if (!Exec(SCHEMA, error))
        return false;
    if (!Exec("DROP TRIGGER IF EXISTS bible_ad;"
              "CREATE TRIGGER IF NOT EXISTS bible_ad AFTER DELETE ON bible BEGIN "
              "INSERT INTO bible_fts(bible_fts, rowid, text, version) VALUES ('delete', old.id, old.text, old.version); END;", error))
        return false;
    return true;
}

void Database::SeedDefaults()
{
    // Tema por defecto si la tabla esta vacia
    sqlite3_stmt *st = Prepare("SELECT COUNT(*) FROM themes;");
    if (st) {
        StmtGuard g(st);
        if (sqlite3_step(st) == SQLITE_ROW && sqlite3_column_int(st, 0) == 0) {
            Theme t = Theme::DefaultTheme();
            const int tid = SaveTheme(t);
            SetSettingInt("default_theme", tid);
        }
    }
}

// ---------------------------------------------------------------------------
// Canciones
// ---------------------------------------------------------------------------
int Database::AddSong(const Song &s, wxString *error)
{
    sqlite3_stmt *st = Prepare(
        "INSERT INTO songs(title, artist, key, bpm, lyrics, updated_at) VALUES(?,?,?,?,?,?);");
    if (!st) { if (error) *error = m_lastError; return 0; }
    StmtGuard g(st);
    const wxString now = wxDateTime::Now().FormatISOCombined();
    BindText(st, 1, s.title);
    BindText(st, 2, s.artist);
    BindText(st, 3, s.key);
    sqlite3_bind_int(st, 4, s.bpm);
    BindText(st, 5, s.lyrics);
    BindText(st, 6, now);
    if (!StepDone(st)) { if (error) *error = m_lastError; return 0; }
    return (int)sqlite3_last_insert_rowid(m_db);
}

bool Database::UpdateSong(const Song &s)
{
    sqlite3_stmt *st = Prepare(
        "UPDATE songs SET title=?, artist=?, key=?, bpm=?, lyrics=?, updated_at=? WHERE id=?;");
    if (!st) return false;
    StmtGuard g(st);
    const wxString now = wxDateTime::Now().FormatISOCombined();
    BindText(st, 1, s.title);
    BindText(st, 2, s.artist);
    BindText(st, 3, s.key);
    sqlite3_bind_int(st, 4, s.bpm);
    BindText(st, 5, s.lyrics);
    BindText(st, 6, now);
    sqlite3_bind_int(st, 7, s.id);
    return StepDone(st) && sqlite3_changes(m_db) > 0;
}

bool Database::DeleteSong(int id)
{
    sqlite3_stmt *st = Prepare("DELETE FROM songs WHERE id=?;");
    if (!st) return false;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, id);
    return StepDone(st);
}

Song Database::SongById(int id)
{
    Song s;
    sqlite3_stmt *st = Prepare("SELECT id,title,artist,key,bpm,lyrics,updated_at FROM songs WHERE id=?;");
    if (!st) return s;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, id);
    if (sqlite3_step(st) == SQLITE_ROW) {
        s.id = sqlite3_column_int(st, 0);
        s.title = ColumnText(st, 1);
        s.artist = ColumnText(st, 2);
        s.key = ColumnText(st, 3);
        s.bpm = sqlite3_column_int(st, 4);
        s.lyrics = ColumnText(st, 5);
        s.updatedAt = ColumnText(st, 6);
    }
    return s;
}

std::vector<Song> Database::SearchSongs(const SearchFilter &f, int limit)
{
    std::vector<Song> out;
    // Forma canonica FTS5: subconsulta IN (rowid FROM fts WHERE MATCH ?)
    wxString sql = "SELECT id, title, artist, key, bpm FROM songs WHERE 1 ";
    wxString q;
    if (!f.q.empty()) {
        // Prefijos por palabra: "gra jesus" casa "gracia jesus"
        const wxArrayString toks = wxStringTokenize(f.q, " \t", wxTOKEN_STRTOK);
        for (size_t i = 0; i < toks.size(); ++i) {
            wxString t = toks[i];
            t.Replace("\"", "\"\"");
            q += (i ? " " : "") + t + "*";
        }
        sql += "AND id IN (SELECT rowid FROM songs_fts WHERE songs_fts MATCH ?) ";
    }
    if (!f.tag.empty()) {
        sql += "AND id IN (SELECT st.song_id FROM song_tags st "
               "JOIN tags tg ON tg.id = st.tag_id WHERE tg.name = ? COLLATE NOCASE) ";
    }
    sql += "ORDER BY title COLLATE NOCASE LIMIT ?;";
    sqlite3_stmt *st = Prepare(sql);
    if (!st) return out;
    StmtGuard g(st);
    int idx = 1;
    if (!f.q.empty()) BindText(st, idx++, q);
    if (!f.tag.empty()) BindText(st, idx++, f.tag);
    sqlite3_bind_int(st, idx++, limit);
    while (sqlite3_step(st) == SQLITE_ROW) {
        Song s;
        s.id = sqlite3_column_int(st, 0);
        s.title = ColumnText(st, 1);
        s.artist = ColumnText(st, 2);
        s.key = ColumnText(st, 3);
        s.bpm = sqlite3_column_int(st, 4);
        out.push_back(s);
    }
    return out;
}

void Database::LogSongUse(int songId)
{
    sqlite3_stmt *st = Prepare("INSERT INTO history(song_id, used_at) VALUES(?, datetime('now'));");
    if (st) { StmtGuard g(st); sqlite3_bind_int(st, 1, songId); sqlite3_step(st); }
    st = Prepare("INSERT INTO song_stats(song_id, use_count, last_used) VALUES(?,1,datetime('now')) "
                 "ON CONFLICT(song_id) DO UPDATE SET use_count=use_count+1, last_used=datetime('now');");
    if (st) { StmtGuard g(st); sqlite3_bind_int(st, 1, songId); sqlite3_step(st); }
}

int Database::SongUseCount(int songId)
{
    sqlite3_stmt *st = Prepare("SELECT use_count FROM song_stats WHERE song_id=?;");
    if (!st) return 0;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, songId);
    if (sqlite3_step(st) == SQLITE_ROW)
        return sqlite3_column_int(st, 0);
    return 0;
}

// ---------------------------------------------------------------------------
// Tags
// ---------------------------------------------------------------------------
std::vector<wxString> Database::AllTagNames()
{
    std::vector<wxString> out;
    sqlite3_stmt *st = Prepare("SELECT name FROM tags ORDER BY name COLLATE NOCASE;");
    if (!st) return out;
    StmtGuard g(st);
    while (sqlite3_step(st) == SQLITE_ROW)
        out.push_back(ColumnText(st, 0));
    return out;
}

void Database::SetSongTags(int songId, const std::vector<wxString> &tags)
{
    Begin();
    bool ok = true;
    sqlite3_stmt *del = Prepare("DELETE FROM song_tags WHERE song_id=?;");
    if (del) {
        StmtGuard g(del);
        sqlite3_bind_int(del, 1, songId);
        ok = sqlite3_step(del) == SQLITE_DONE;
    } else ok = false;

    if (ok) {
        for (const wxString &tag : tags) {
            const wxString t = wxString(tag).Trim(true).Trim(false);
            if (t.empty()) continue;
            sqlite3_stmt *tg = Prepare("INSERT INTO tags(name) VALUES(?) ON CONFLICT(name) DO NOTHING;");
            if (!tg) { ok = false; break; }
            { StmtGuard g(tg); BindText(tg, 1, t); if (sqlite3_step(tg) != SQLITE_DONE) ok = false; }
            if (!ok) break;
            sqlite3_stmt *link = Prepare(
                "INSERT INTO song_tags(song_id, tag_id) VALUES(?, (SELECT id FROM tags WHERE name=? COLLATE NOCASE)) "
                "ON CONFLICT DO NOTHING;");
            if (!link) { ok = false; break; }
            { StmtGuard g(link); sqlite3_bind_int(link, 1, songId); BindText(link, 2, t);
              if (sqlite3_step(link) != SQLITE_DONE) ok = false; }
            if (!ok) break;
        }
    }
    if (ok) Commit(); else Rollback();
}

std::vector<wxString> Database::SongTags(int songId)
{
    std::vector<wxString> out;
    sqlite3_stmt *st = Prepare(
        "SELECT tg.name FROM song_tags st JOIN tags tg ON tg.id=st.tag_id "
        "WHERE st.song_id=? ORDER BY tg.name COLLATE NOCASE;");
    if (!st) return out;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, songId);
    while (sqlite3_step(st) == SQLITE_ROW)
        out.push_back(ColumnText(st, 0));
    return out;
}

void Database::SetResourceTags(const wxString &kind, const wxString &key, const std::vector<wxString> &tags)
{
    Begin();
    bool ok = true;
    sqlite3_stmt *del = Prepare("DELETE FROM resource_tags WHERE kind=? AND key=?;");
    if (del) {
        StmtGuard g(del);
        BindText(del, 1, kind); BindText(del, 2, key);
        ok = sqlite3_step(del) == SQLITE_DONE;
    } else ok = false;

    if (ok) {
        for (const wxString &tag : tags) {
            const wxString t = wxString(tag).Trim(true).Trim(false);
            if (t.empty()) continue;
            sqlite3_stmt *tg = Prepare("INSERT INTO tags(name) VALUES(?) ON CONFLICT(name) DO NOTHING;");
            if (!tg) { ok = false; break; }
            { StmtGuard g(tg); BindText(tg, 1, t); sqlite3_step(tg); }
            sqlite3_stmt *link = Prepare(
                "INSERT INTO resource_tags(kind, key, tag_id) VALUES(?,?,(SELECT id FROM tags WHERE name=? COLLATE NOCASE)) "
                "ON CONFLICT DO NOTHING;");
            if (!link) { ok = false; break; }
            { StmtGuard g(link); BindText(link, 1, kind); BindText(link, 2, key); BindText(link, 3, t);
              sqlite3_step(link); }
        }
    }
    if (ok) Commit(); else Rollback();
}

std::vector<wxString> Database::ResourceTags(const wxString &kind, const wxString &key)
{
    std::vector<wxString> out;
    sqlite3_stmt *st = Prepare(
        "SELECT tg.name FROM resource_tags rt JOIN tags tg ON tg.id=rt.tag_id "
        "WHERE rt.kind=? AND rt.key=? ORDER BY tg.name COLLATE NOCASE;");
    if (!st) return out;
    StmtGuard g(st);
    BindText(st, 1, kind);
    BindText(st, 2, key);
    while (sqlite3_step(st) == SQLITE_ROW)
        out.push_back(ColumnText(st, 0));
    return out;
}

// ---------------------------------------------------------------------------
// Biblia
// ---------------------------------------------------------------------------
bool Database::ImportBible(const wxString &version, const wxString &name,
                           const std::vector<BibleRow> &rows, wxString *error,
                           const std::function<bool(int, int)> &progress)
{
    // Guard anti-duplicado (M21): borrar version previa antes de importar
    sqlite3_stmt *del = Prepare("DELETE FROM bible WHERE version=?;");
    if (!del) { if (error) *error = m_lastError; return false; }
    { StmtGuard g(del); BindText(del, 1, version); sqlite3_step(del); }
    // El trigger bible_ad sincroniza el FTS externo con el borrado; el
    // trigger bible_ai reindexa cada insercion. Sin borrados manuales del FTS.
    if (!Begin()) { if (error) *error = m_lastError; return false; }
    sqlite3_stmt *st = Prepare("INSERT INTO bible(version, book, chapter, verse, text) VALUES(?,?,?,?,?);");
    if (!st) { Rollback(); if (error) *error = m_lastError; return false; }
    const int total = (int)rows.size();
    for (int i = 0; i < total; ++i) {
        sqlite3_reset(st);
        sqlite3_clear_bindings(st);
        BindText(st, 1, version);
        sqlite3_bind_int(st, 2, rows[i].book);
        sqlite3_bind_int(st, 3, rows[i].chapter);
        sqlite3_bind_int(st, 4, rows[i].verse);
        BindText(st, 5, rows[i].text);
        if (sqlite3_step(st) != SQLITE_DONE) {
            m_lastError = wxString::FromUTF8(sqlite3_errmsg(m_db));
            sqlite3_finalize(st);
            Rollback();
            if (error) *error = m_lastError;
            return false;
        }
        if (progress && (i % 2000 == 0)) {
            if (!progress(i, total)) {   // cancelado por el usuario
                sqlite3_finalize(st);
                Rollback();
                return false;
            }
        }
    }
    sqlite3_finalize(st);
    if (!Commit()) {
        if (error) *error = m_lastError;
        return false;
    }
    (void)name;
    return true;
}

std::vector<wxString> Database::BibleVersions()
{
    std::vector<wxString> out;
    sqlite3_stmt *st = Prepare("SELECT DISTINCT version FROM bible ORDER BY version;");
    if (!st) return out;
    StmtGuard g(st);
    while (sqlite3_step(st) == SQLITE_ROW)
        out.push_back(ColumnText(st, 0));
    return out;
}

bool Database::BibleHasData()
{
    sqlite3_stmt *st = Prepare("SELECT EXISTS(SELECT 1 FROM bible LIMIT 1);");
    if (!st) return false;
    StmtGuard g(st);
    return sqlite3_step(st) == SQLITE_ROW && sqlite3_column_int(st, 0) == 1;
}

int Database::BibleVerseCount(const wxString &version)
{
    sqlite3_stmt *st = Prepare("SELECT COUNT(*) FROM bible WHERE version=?;");
    if (!st) return 0;
    StmtGuard g(st);
    BindText(st, 1, version);
    if (sqlite3_step(st) == SQLITE_ROW)
        return sqlite3_column_int(st, 0);
    return 0;
}

std::vector<Database::BibleRow> Database::BiblePassage(const wxString &version, int book,
                                                       int chapter, int verseFrom, int verseTo)
{
    std::vector<BibleRow> out;
    sqlite3_stmt *st = Prepare(
        "SELECT book, chapter, verse, text FROM bible "
        "WHERE version=? AND book=? AND chapter=? AND verse>=? AND verse<=? ORDER BY verse;");
    if (!st) return out;
    StmtGuard g(st);
    BindText(st, 1, version);
    sqlite3_bind_int(st, 2, book);
    sqlite3_bind_int(st, 3, chapter);
    sqlite3_bind_int(st, 4, verseFrom > 0 ? verseFrom : 1);
    sqlite3_bind_int(st, 5, verseTo >= verseFrom ? verseTo : (verseFrom > 0 ? verseFrom : 9999));
    while (sqlite3_step(st) == SQLITE_ROW) {
        BibleRow r;
        r.book = sqlite3_column_int(st, 0);
        r.chapter = sqlite3_column_int(st, 1);
        r.verse = sqlite3_column_int(st, 2);
        r.text = ColumnText(st, 3);
        out.push_back(r);
    }
    return out;
}

std::vector<Database::BibleRow> Database::BibleSearch(const wxString &query, const wxString &version, int limit)
{
    std::vector<BibleRow> out;
    if (wxString(query).Trim(true).Trim(false).empty())
        return out;
    wxString q;
    const wxArrayString toks = wxStringTokenize(query, " \t", wxTOKEN_STRTOK);
    for (size_t i = 0; i < toks.size(); ++i) {
        wxString t = toks[i];
        t.Replace("\"", "\"\"");
        q += (i ? " " : "") + t + "*";
    }
    sqlite3_stmt *st = Prepare(
        "SELECT b.book, b.chapter, b.verse, b.text FROM bible b "
        "WHERE b.version=? AND b.id IN (SELECT rowid FROM bible_fts WHERE bible_fts MATCH ?) "
        "ORDER BY b.book, b.chapter, b.verse LIMIT ?;");
    if (!st) return out;
    StmtGuard g(st);
    BindText(st, 2, q);
    BindText(st, 1, version);
    sqlite3_bind_int(st, 3, limit);
    while (sqlite3_step(st) == SQLITE_ROW) {
        BibleRow r;
        r.book = sqlite3_column_int(st, 0);
        r.chapter = sqlite3_column_int(st, 1);
        r.verse = sqlite3_column_int(st, 2);
        r.text = ColumnText(st, 3);
        out.push_back(r);
    }
    return out;
}

// ---------------------------------------------------------------------------
// Temas
// ---------------------------------------------------------------------------
int Database::SaveTheme(const Theme &t)
{
    const std::string js = t.toJson().dump();
    if (t.id > 0) {
        sqlite3_stmt *st = Prepare("UPDATE themes SET name=?, json=? WHERE id=?;");
        if (!st) return t.id;
        StmtGuard g(st);
        BindText(st, 1, t.name);
        sqlite3_bind_text(st, 2, js.c_str(), (int)js.size(), SQLITE_TRANSIENT);
        sqlite3_bind_int(st, 3, t.id);
        if (StepDone(st))
            return t.id;
        return t.id;
    }
    sqlite3_stmt *st = Prepare("INSERT INTO themes(name, json) VALUES(?,?);");
    if (!st) return 0;
    StmtGuard g(st);
    BindText(st, 1, t.name);
    sqlite3_bind_text(st, 2, js.c_str(), (int)js.size(), SQLITE_TRANSIENT);
    if (!StepDone(st))
        return 0;
    return (int)sqlite3_last_insert_rowid(m_db);
}

bool Database::DeleteTheme(int id)
{
    sqlite3_stmt *st = Prepare("DELETE FROM themes WHERE id=?;");
    if (!st) return false;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, id);
    return StepDone(st);
}

std::vector<Theme> Database::Themes()
{
    std::vector<Theme> out;
    sqlite3_stmt *st = Prepare("SELECT id, json FROM themes ORDER BY name COLLATE NOCASE;");
    if (!st) return out;
    StmtGuard g(st);
    while (sqlite3_step(st) == SQLITE_ROW) {
        // Parseo desde los bytes UTF-8 crudos (sin conversion por locale)
        const char *js = reinterpret_cast<const char *>(sqlite3_column_text(st, 1));
        Theme t = Theme::fromJson(json::parse(js ? js : "{}", nullptr, false));
        t.id = sqlite3_column_int(st, 0);
        out.push_back(t);
    }
    return out;
}

Theme Database::ThemeById(int id)
{
    Theme t = Theme::DefaultTheme();
    sqlite3_stmt *st = Prepare("SELECT json FROM themes WHERE id=?;");
    if (!st) return t;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, id);
    if (sqlite3_step(st) == SQLITE_ROW) {
        const char *js = reinterpret_cast<const char *>(sqlite3_column_text(st, 0));
        t = Theme::fromJson(json::parse(js ? js : "{}", nullptr, false));
        t.id = id;
    }
    return t;
}

void Database::SetDefaultTheme(int id)
{
    SetSettingInt("default_theme", id);
}

int Database::DefaultThemeId()
{
    return GetSettingInt("default_theme", 0);
}

// ---------------------------------------------------------------------------
// Medios
// ---------------------------------------------------------------------------
bool Database::AddMedia(const wxString &path, int kind)
{
    sqlite3_stmt *st = Prepare(
        "INSERT INTO media(path, kind, added_at) VALUES(?,?,datetime('now')) "
        "ON CONFLICT(path) DO UPDATE SET kind=excluded.kind;");
    if (!st) return false;
    StmtGuard g(st);
    BindText(st, 1, path);
    sqlite3_bind_int(st, 2, kind);
    return StepDone(st);
}

bool Database::RemoveMedia(const wxString &path)
{
    sqlite3_stmt *st = Prepare("DELETE FROM media WHERE path=?;");
    if (!st) return false;
    StmtGuard g(st);
    BindText(st, 1, path);
    return StepDone(st);
}

std::vector<MediaRow> Database::MediaLibrary()
{
    std::vector<MediaRow> out;
    sqlite3_stmt *st = Prepare("SELECT path, kind FROM media ORDER BY path;");
    if (!st) return out;
    StmtGuard g(st);
    while (sqlite3_step(st) == SQLITE_ROW) {
        MediaRow r;
        r.path = ColumnText(st, 0);
        r.kind = sqlite3_column_int(st, 1);
        out.push_back(r);
    }
    return out;
}

// ---------------------------------------------------------------------------
// Cultos (playlists)
// ---------------------------------------------------------------------------
int Database::CreatePlaylist(const wxString &name)
{
    sqlite3_stmt *st = Prepare("INSERT INTO playlists(name, created_at) VALUES(?, datetime('now'));");
    if (!st) return 0;
    StmtGuard g(st);
    BindText(st, 1, name);
    if (!StepDone(st))
        return 0;
    return (int)sqlite3_last_insert_rowid(m_db);
}

bool Database::DeletePlaylist(int id)
{
    bool ok = false;
    sqlite3_stmt *st = Prepare("DELETE FROM playlist_items WHERE playlist_id=?;");
    if (st) { StmtGuard g(st); sqlite3_bind_int(st, 1, id); ok = sqlite3_step(st) == SQLITE_DONE; }
    st = Prepare("DELETE FROM playlists WHERE id=?;");
    if (st) { StmtGuard g(st); sqlite3_bind_int(st, 1, id); ok = sqlite3_step(st) == SQLITE_DONE; }
    return ok;
}

std::vector<std::pair<int, wxString>> Database::Playlists()
{
    std::vector<std::pair<int, wxString>> out;
    sqlite3_stmt *st = Prepare("SELECT id, name FROM playlists ORDER BY created_at DESC, id DESC;");
    if (!st) return out;
    StmtGuard g(st);
    while (sqlite3_step(st) == SQLITE_ROW)
        out.emplace_back(sqlite3_column_int(st, 0), ColumnText(st, 1));
    return out;
}

void Database::ReplaceItems(int playlistId, const std::vector<ServiceItem> &items)
{
    Begin();
    bool ok = true;
    sqlite3_stmt *del = Prepare("DELETE FROM playlist_items WHERE playlist_id=?;");
    if (del) {
        StmtGuard g(del);
        sqlite3_bind_int(del, 1, playlistId);
        ok = sqlite3_step(del) == SQLITE_DONE;
    } else ok = false;
    if (ok) {
        sqlite3_stmt *st = Prepare(
            "INSERT INTO playlist_items(playlist_id, position, kind, ref_id, label, payload) VALUES(?,?,?,?,?,?);");
        if (!st) ok = false;
        else {
            StmtGuard g(st);
            for (size_t i = 0; i < items.size() && ok; ++i) {
                sqlite3_reset(st);
                sqlite3_clear_bindings(st);
                sqlite3_bind_int(st, 1, playlistId);
                sqlite3_bind_int(st, 2, (int)i);
                sqlite3_bind_int(st, 3, items[i].kind);
                sqlite3_bind_int(st, 4, items[i].refId);
                BindText(st, 5, items[i].label);
                BindText(st, 6, items[i].payload);
                ok = sqlite3_step(st) == SQLITE_DONE;
            }
        }
    }
    if (ok) Commit(); else Rollback();
}

std::vector<ServiceItem> Database::PlaylistItems(int playlistId)
{
    std::vector<ServiceItem> out;
    sqlite3_stmt *st = Prepare(
        "SELECT id, kind, ref_id, label, payload FROM playlist_items "
        "WHERE playlist_id=? ORDER BY position;");
    if (!st) return out;
    StmtGuard g(st);
    sqlite3_bind_int(st, 1, playlistId);
    while (sqlite3_step(st) == SQLITE_ROW) {
        ServiceItem it;
        it.id = sqlite3_column_int(st, 0);
        it.kind = sqlite3_column_int(st, 1);
        it.refId = sqlite3_column_int(st, 2);
        it.label = ColumnText(st, 3);
        it.payload = ColumnText(st, 4);
        out.push_back(it);
    }
    return out;
}

// ---------------------------------------------------------------------------
// Respaldo (API sqlite3_backup — consistente incluso con la BD en uso)
// ---------------------------------------------------------------------------
bool Database::BackupToFile(const wxString &destPath)
{
    if (!m_db)
        return false;
    sqlite3 *dst = nullptr;
    if (sqlite3_open_v2(destPath.utf8_str(), &dst,
                        SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE, nullptr) != SQLITE_OK) {
        if (dst) sqlite3_close(dst);
        return false;
    }
    sqlite3_backup *bk = sqlite3_backup_init(dst, "main", m_db, "main");
    bool ok = false;
    if (bk) {
        ok = sqlite3_backup_step(bk, -1) == SQLITE_DONE;
        sqlite3_backup_finish(bk);
    }
    sqlite3_close(dst);
    return ok;
}

// ---------------------------------------------------------------------------
// Ajustes
// ---------------------------------------------------------------------------
wxString Database::GetSetting(const wxString &key, const wxString &def)
{
    sqlite3_stmt *st = Prepare("SELECT value FROM settings WHERE key=?;");
    if (!st) return def;
    StmtGuard g(st);
    BindText(st, 1, key);
    if (sqlite3_step(st) == SQLITE_ROW)
        return ColumnText(st, 0);
    return def;
}

void Database::SetSetting(const wxString &key, const wxString &value)
{
    sqlite3_stmt *st = Prepare(
        "INSERT INTO settings(key, value) VALUES(?,?) "
        "ON CONFLICT(key) DO UPDATE SET value=excluded.value;");
    if (!st) return;
    StmtGuard g(st);
    BindText(st, 1, key);
    BindText(st, 2, value);
    sqlite3_step(st);
}

int Database::GetSettingInt(const wxString &key, int def)
{
    const wxString v = GetSetting(key);
    if (v.empty()) return def;
    long out = def;
    if (v.ToLong(&out))
        return (int)out;
    return def;
}

void Database::SetSettingInt(const wxString &key, int value)
{
    SetSetting(key, wxString::Format("%d", value));
}
