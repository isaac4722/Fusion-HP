// Contrato: almacenamiento embebido SQLite+FTS5 (ejecutado por C# vía API).
#ifndef LUMINA_STORAGE_H
#define LUMINA_STORAGE_H

#include <string>
#include <vector>
#include <nlohmann/json.hpp>

struct sqlite3;

namespace lumina {

using json = nlohmann::json;
class Database {
public:
    Database() = default;
    ~Database();
    Database(const Database&) = delete;
    Database& operator=(const Database&) = delete;

    // Abre/crea la BD portable; asegura esquema (canciones, biblia+UNIQUE,
    // FTS5) y la migración de dedupe idempotente (herencia v1.6.0).
    bool Open(const std::string& pathUtf8, std::string* err);
    void Close();
    bool IsOpen() const { return db_ != nullptr; }

    // Ejecuta {"sql":"...","params":[...]}:
    //  - params SIEMPRE enlazados (nada de interpolación de usuario)
    //  - SELECT → {"rows":[[...]]} (cap 10000 filas)
    //  - INSERT/UPDATE/DELETE → {"changes":N,"lastId":N}
    // Devuelve false y llena err ante fallo SQL.
    bool ExecJson(const std::string& sqlJson, std::string* outJson, std::string* err);

    // Solo-tests/uso interno: SQL directo con params.
    bool Exec(const std::string& sql, const std::vector<std::string>& params,
              std::string* err);

private:
    bool EnsureSchema(std::string* err);
    sqlite3* db_ = nullptr;
};
} // namespace lumina
#endif
