// ============================================================================
//  Fusion-HP · SqliteDb.cpp — implementación del lector SQLite embebido
// ============================================================================
#include "SqliteDb.h"

namespace fusion {

// ------------------------------------------------------------------- Stmt
SqliteDb::Stmt::~Stmt() {
    if (st_) sqlite3_finalize(st_);
    st_ = nullptr;
}

SqliteDb::Stmt::Stmt(Stmt&& o) noexcept : st_(o.st_), done_(o.done_) {
    o.st_ = nullptr; o.done_ = true;
}

SqliteDb::Stmt& SqliteDb::Stmt::operator=(Stmt&& o) noexcept {
    if (this != &o) {
        if (st_) sqlite3_finalize(st_);
        st_ = o.st_; done_ = o.done_;
        o.st_ = nullptr; o.done_ = true;
    }
    return *this;
}

bool SqliteDb::Stmt::Step() {
    if (!st_ || done_) return false;
    int rc = sqlite3_step(st_);
    if (rc == SQLITE_ROW) return true;
    if (rc == SQLITE_DONE) { done_ = true; return false; }
    done_ = true;
    return false;
}

bool SqliteDb::Stmt::BindInt(int idx, long long v) {
    return st_ && sqlite3_bind_int64(st_, idx, v) == SQLITE_OK;
}

bool SqliteDb::Stmt::BindText(int idx, const std::string& v) {
    return st_ && sqlite3_bind_text(st_, idx, v.c_str(), (int)v.size(), SQLITE_TRANSIENT) == SQLITE_OK;
}

long long SqliteDb::Stmt::ColumnInt(int idx) const {
    return st_ ? sqlite3_column_int64(st_, idx) : 0;
}

std::string SqliteDb::Stmt::ColumnText(int idx) const {
    if (!st_) return std::string();
    const unsigned char* t = sqlite3_column_text(st_, idx);
    int n = sqlite3_column_bytes(st_, idx);
    return (t && n > 0) ? std::string(reinterpret_cast<const char*>(t), (size_t)n) : std::string();
}

std::string SqliteDb::Stmt::ColumnBlob(int idx) const {
    if (!st_) return std::string();
    const void* b = sqlite3_column_blob(st_, idx);
    int n = sqlite3_column_bytes(st_, idx);
    return (b && n > 0) ? std::string(reinterpret_cast<const char*>(b), (size_t)n) : std::string();
}

void SqliteDb::Stmt::Reset() {
    if (st_) { sqlite3_reset(st_); sqlite3_clear_bindings(st_); }
    done_ = false;
}

// ------------------------------------------------------------------ SqliteDb
SqliteDb::~SqliteDb() { Close(); }

void SqliteDb::Close() {
    if (db_) sqlite3_close_v2(db_);
    db_ = nullptr;
}

bool SqliteDb::OpenUtf8(const std::string& utf8, int flags) {
    Close();
    if (sqlite3_open_v2(utf8.c_str(), &db_, flags, nullptr) != SQLITE_OK) {
        LastError(); // deja el texto por si acaso
        if (db_) { sqlite3_close_v2(db_); db_ = nullptr; }
        return false;
    }
    return db_ != nullptr;
}

bool SqliteDb::OpenReadOnly(const std::wstring& path) {
    return OpenUtf8(ToUtf8(path), SQLITE_OPEN_READONLY);
}

bool SqliteDb::OpenReadWrite(const std::wstring& path) {
    return OpenUtf8(ToUtf8(path), SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE);
}

bool SqliteDb::OpenMemory() {
    return OpenUtf8(":memory:", SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE);
}

bool SqliteDb::Exec(const char* sql) {
    if (!db_) return false;
    char* err = nullptr;
    int rc = sqlite3_exec(db_, sql, nullptr, nullptr, &err);
    if (err) sqlite3_free(err);
    return rc == SQLITE_OK;
}

bool SqliteDb::Prepare(const char* sql, Stmt* out) {
    if (!db_ || !out) return false;
    *out = Stmt();
    if (sqlite3_prepare_v2(db_, sql, -1, &out->st_, nullptr) != SQLITE_OK) {
        out->st_ = nullptr;
        return false;
    }
    return true;
}

long long SqliteDb::LastInsertRowId() const {
    return db_ ? sqlite3_last_insert_rowid(db_) : 0;
}

int SqliteDb::RowsChanged() const {
    return db_ ? sqlite3_changes(db_) : 0;
}

std::string SqliteDb::LastError() const {
    return db_ ? std::string(sqlite3_errmsg(db_)) : std::string("bd no abierta");
}

} // namespace fusion
