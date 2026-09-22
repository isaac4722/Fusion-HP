// ============================================================================
//  Fusion-HP / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Storage.cpp : SQLite embebido (estático) + FTS5. Parámetros SIEMPRE
//  enlazados (herencia de la auditoría v1.6.0: cero interpolación SQL).
// ============================================================================
#include "Storage.h"
#include "Utf8.h"

#include <sqlite3.h>
#include <mutex>

namespace fusion {

Database::~Database() { Close(); }

bool Database::Open(const std::string& pathUtf8, std::string* err) {
    Close();
    const int flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX;
    if (sqlite3_open_v2(pathUtf8.c_str(), &db_, flags, nullptr) != SQLITE_OK) {
        if (err && db_) *err = sqlite3_errmsg(db_);
        else if (err) *err = "sqlite3_open falló";
        Close();
        return false;
    }
    sqlite3_busy_timeout(db_, 5000);
    // Durabilidad portable (herencia v1.x): WAL off + NORMAL
    if (!Exec("PRAGMA journal_mode=DELETE;", {}, err) ||
        !Exec("PRAGMA synchronous=NORMAL;", {}, err)) {
        Close();
        return false;
    }
    if (!EnsureSchema(err)) { Close(); return false; }
    return true;
}

void Database::Close() {
    if (db_) { sqlite3_close_v2(db_); db_ = nullptr; }
}

bool Database::Exec(const std::string& sql, const std::vector<std::string>& params,
                    std::string* err) {
    // Itera TODAS las sentencias del script (sqlite3_prepare_v2 deja el
    // resto en pzTail; quedarse solo con la primera dejaba la BD sin
    // tabla bible/fts cuando el esquema venía en un solo string).
    const char* p = sql.c_str();
    const char* end = p + sql.size();
    while (p < end) {
        // salta espacios/punto y coma sueltos entre sentencias
        while (p < end && (*p == ' ' || *p == '\t' || *p == '\r' || *p == '\n' || *p == ';')) ++p;
        if (p >= end) break;
        sqlite3_stmt* st = nullptr;
        const char* tail = nullptr;
        if (sqlite3_prepare_v2(db_, p, (int)(end - p), &st, &tail) != SQLITE_OK) {
            if (err) *err = sqlite3_errmsg(db_);
            return false;
        }
        if (!st) {                       // sentencia vacía (comentario solo)
            p = tail;
            continue;
        }
        for (size_t i = 0; i < params.size() && i < 64; ++i) {
            sqlite3_bind_text(st, (int)i + 1, params[i].data(), (int)params[i].size(), SQLITE_TRANSIENT);
        }
        const int rc = sqlite3_step(st);
        if (rc != SQLITE_DONE && rc != SQLITE_ROW) {
            if (err) *err = sqlite3_errmsg(db_);
            sqlite3_finalize(st);
            return false;
        }
        sqlite3_finalize(st);
        p = tail ? tail : end;
    }
    return true;
}

bool Database::EnsureSchema(std::string* err) {
    // ¿Existe ya el índice UNIQUE de biblia? (idempotente, herencia M21 v1.6.0)
    static const char* kHasIndex =
        "SELECT count(*) FROM sqlite_master WHERE type='index' AND name='idx_bible_unique'";
    static const char* kHasBibleTable =
        "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='bible'";
    auto CountMaster = [&](const char* sql) -> int {
        sqlite3_stmt* st = nullptr;
        int n = 0;
        if (sqlite3_prepare_v2(db_, sql, -1, &st, nullptr) == SQLITE_OK) {
            if (sqlite3_step(st) == SQLITE_ROW) n = sqlite3_column_int(st, 0);
        }
        sqlite3_finalize(st);
        return n;
    };
    const bool hasIndex = CountMaster(kHasIndex) > 0;
    // Dedupe de migración: SOLO si la tabla existe (BD v1.5 pre-existentes);
    // en una BD nueva la tabla aún no está y el DELETE fallaría.
    if (!hasIndex && CountMaster(kHasBibleTable) > 0) {
        static const char* kDedupe =
            "DELETE FROM bible WHERE rowid NOT IN "
            "(SELECT MIN(rowid) FROM bible GROUP BY version,book,chapter,verse)";
        if (!Exec(kDedupe, {}, err)) return false;
    }
    static const char* kSchema =
        "CREATE TABLE IF NOT EXISTS songs("
        "  id INTEGER PRIMARY KEY AUTOINCREMENT,"
        "  title TEXT NOT NULL,"
        "  author TEXT DEFAULT '',"
        "  key TEXT DEFAULT '',"
        "  bpm INTEGER DEFAULT 0,"
        "  tags TEXT DEFAULT '',"
        "  lyrics TEXT DEFAULT '',"
        "  created_at TEXT DEFAULT (datetime('now')));"
        "CREATE TABLE IF NOT EXISTS bible("
        "  version TEXT NOT NULL,"
        "  book INTEGER NOT NULL,"
        "  chapter INTEGER NOT NULL,"
        "  verse INTEGER NOT NULL,"
        "  text TEXT NOT NULL);"
        "CREATE UNIQUE INDEX IF NOT EXISTS idx_bible_unique"
        "  ON bible(version,book,chapter,verse);"
        "CREATE VIRTUAL TABLE IF NOT EXISTS songs_fts USING fts5(title, lyrics, tags);"
        "CREATE VIRTUAL TABLE IF NOT EXISTS bible_fts USING fts5(version UNINDEXED, book UNINDEXED, chapter UNINDEXED, verse UNINDEXED, text);"
        "CREATE TRIGGER IF NOT EXISTS songs_ai AFTER INSERT ON songs BEGIN"
        "  INSERT INTO songs_fts(rowid,title,lyrics,tags) VALUES(new.id,new.title,new.lyrics,new.tags); END;"
        "CREATE TRIGGER IF NOT EXISTS songs_ad AFTER DELETE ON songs BEGIN"
        "  INSERT INTO songs_fts(songs_fts,rowid,title,lyrics,tags) VALUES('delete',old.id,old.title,old.lyrics,old.tags); END;"
        "CREATE TRIGGER IF NOT EXISTS songs_au AFTER UPDATE ON songs BEGIN"
        "  INSERT INTO songs_fts(songs_fts,rowid,title,lyrics,tags) VALUES('delete',old.id,old.title,old.lyrics,old.tags);"
        "  INSERT INTO songs_fts(rowid,title,lyrics,tags) VALUES(new.id,new.title,new.lyrics,new.tags); END;"
        "CREATE TRIGGER IF NOT EXISTS bible_ai AFTER INSERT ON bible BEGIN"
        "  INSERT INTO bible_fts(version,book,chapter,verse,text) VALUES(new.version,new.book,new.chapter,new.verse,new.text); END;"
        "CREATE TRIGGER IF NOT EXISTS bible_ad AFTER DELETE ON bible BEGIN"
        "  INSERT INTO bible_fts(bible_fts,version,book,chapter,verse,text) VALUES('delete',old.version,old.book,old.chapter,old.verse,old.text); END;"
        "CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT);";
    return Exec(kSchema, {}, err);
}

bool Database::ExecJson(const std::string& sqlJson, std::string* outJson, std::string* err) {
    outJson->clear();
    json req;
    try {
        req = json::parse(sqlJson);
    } catch (const json::exception& e) {
        if (err) *err = std::string("JSON inválido: ") + e.what();
        return false;
    }
    if (!req.contains("sql") || !req["sql"].is_string()) {
        if (err) *err = "falta \"sql\"";
        return false;
    }
    std::vector<std::pair<bool, std::string>> params;   // (isNull, valor)
    if (req.contains("params") && req["params"].is_array()) {
        for (const auto& p : req["params"]) {
            if (p.is_null()) params.emplace_back(true, std::string());
            else if (p.is_string()) params.emplace_back(false, p.get<std::string>());
            else if (p.is_number_integer()) params.emplace_back(false, std::to_string(p.get<long long>()));
            else if (p.is_number_float()) params.emplace_back(false, json(p).dump());
            else if (p.is_boolean()) params.emplace_back(false, p.get<bool>() ? "1" : "0");
            else params.emplace_back(false, p.dump());
        }
    }
    sqlite3_stmt* st = nullptr;
    const std::string& sql = req["sql"].get_ref<const std::string&>();
    if (sqlite3_prepare_v2(db_, sql.c_str(), (int)sql.size(), &st, nullptr) != SQLITE_OK) {
        if (err) *err = sqlite3_errmsg(db_);
        return false;
    }
    for (size_t i = 0; i < params.size() && i < 64; ++i) {
        if (params[i].first) {
            sqlite3_bind_null(st, (int)i + 1);
        } else {
            sqlite3_bind_text(st, (int)i + 1, params[i].second.data(),
                              (int)params[i].second.size(), SQLITE_TRANSIENT);
        }
    }
    json res;
    res["rows"] = json::array();
    const int kMaxRows = 10000;
    int rows = 0;
    for (;;) {
        const int rc = sqlite3_step(st);
        if (rc == SQLITE_ROW) {
            if (rows < kMaxRows) {
                json row = json::array();
                const int nc = sqlite3_column_count(st);
                for (int c = 0; c < nc; ++c) {
                    switch (sqlite3_column_type(st, c)) {
                        case SQLITE_INTEGER: row.push_back(sqlite3_column_int64(st, c)); break;
                        case SQLITE_FLOAT:   row.push_back(sqlite3_column_double(st, c)); break;
                        case SQLITE_NULL:    row.push_back(nullptr); break;
                        default: {
                            const unsigned char* t = sqlite3_column_text(st, c);
                            const int tb = sqlite3_column_bytes(st, c);
                            row.push_back(t ? std::string(reinterpret_cast<const char*>(t), (size_t)tb)
                                            : std::string());
                        } break;
                    }
                }
                res["rows"].push_back(row);
                ++rows;
            }
        } else if (rc == SQLITE_DONE) {
            break;
        } else {
            if (err) *err = sqlite3_errmsg(db_);
            sqlite3_finalize(st);
            return false;
        }
    }
    sqlite3_finalize(st);
    res["changes"] = sqlite3_changes(db_);
    res["lastId"]  = sqlite3_last_insert_rowid(db_);
    res["truncated"] = (rows >= kMaxRows) ? 1 : 0;
    *outJson = res.dump();
    return true;
}

} // namespace fusion
