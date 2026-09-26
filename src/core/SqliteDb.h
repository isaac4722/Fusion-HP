// ============================================================================
//  Fusion-HP · SqliteDb — lector/escritor SQLite embebido (v4.1.0)
//  Amalgamation 3.45.1 (dominio público) compilada en el núcleo: B-trees,
//  WAL y encodings reales para fdb/biblias [SPEC §7, §10].
//  CODE_STYLE: sin excepciones — Open/Exec/Prepare devuelven bool y
//  LastError() explica el fallo. RAII: el handle y los statements mueren solos.
// ============================================================================
#pragma once
#include "Common.h"
#include <sqlite3.h>

namespace fusion {

class SqliteDb {
public:
    class Stmt {
    public:
        Stmt() = default;
        ~Stmt();
        Stmt(Stmt&& o) noexcept;
        Stmt& operator=(Stmt&& o) noexcept;
        Stmt(const Stmt&) = delete;
        Stmt& operator=(const Stmt&) = delete;

        bool Step();                                   // true = fila; false = fin o error
        bool BindInt(int idx, long long v);
        bool BindText(int idx, const std::string& v);
        long long ColumnInt(int idx) const;
        std::string ColumnText(int idx) const;
        std::string ColumnBlob(int idx) const;
        void Reset();

        sqlite3_stmt* Handle() const { return st_; }

    private:
        friend class SqliteDb;
        sqlite3_stmt* st_ = nullptr;
        bool done_ = false;
    };

    SqliteDb() = default;
    ~SqliteDb();
    SqliteDb(const SqliteDb&) = delete;
    SqliteDb& operator=(const SqliteDb&) = delete;

    bool OpenReadOnly(const std::wstring& path);
    bool OpenReadWrite(const std::wstring& path);
    bool OpenMemory();
    bool Exec(const char* sql);
    bool Prepare(const char* sql, Stmt* out);
    long long LastInsertRowId() const;
    int RowsChanged() const;
    std::string LastError() const;
    bool IsOpen() const { return db_ != nullptr; }
    void Close();

private:
    bool OpenUtf8(const std::string& utf8, int flags);
    sqlite3* db_ = nullptr;
};

} // namespace fusion
