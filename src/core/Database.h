// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Database.h : Motor de persistencia SQLite 3.50 embebido (amalgamation
//  vendorizada) con FTS5 para busqueda instantanea de canciones, biblias
//  multiversion, cultos, temas, historial y ajustes.
//  Se usa la API C de sqlite3 directamente (sin QtSql) para garantizar FTS5.
// ============================================================================
#ifndef LUMINA_DATABASE_H
#define LUMINA_DATABASE_H

#include "Models.h"
#include "BibleRef.h"

#include <QObject>
#include <QString>
#include <QVector>
#include <QVariantMap>
#include <QJsonObject>
#include <functional>

struct sqlite3;
struct sqlite3_stmt;

class Database : public QObject
{
    Q_OBJECT
public:
    explicit Database(QObject *parent = nullptr);
    ~Database() override;

    bool open(const QString &dbFile, QString *error = nullptr);
    void close();
    bool isOpen() const { return m_db != nullptr; }
    QString lastError() const { return m_lastError; }

    // ------------------------------ Canciones -------------------------------
    int  addSong(const Song &s);
    bool updateSong(const Song &s);
    bool deleteSong(int id);
    Song songById(int id);
    QVector<SongRow> searchSongs(const QString &term, int limit = 80);
    QVector<SongRow> allSongs();
    void touchSongUsage(int songId);            // historial + estadisticas

    // ----------------------- Etiquetas (tags) v1.0.3 -----------------------
    // Tags semanticos para canciones (feature del spec Holyrics):
    // asignar palabras clave y luego filtrar por etiqueta.
    int  addTag(const QString &name);                       // idempotente
    bool setSongTags(int songId, const QStringList &tags);  // reemplaza
    QStringList songTags(int songId);
    QVector<QPair<int, QString>> allTags();                 // (id, nombre) ordenado por uso
    QVector<SongRow> searchByTag(const QString &tag);        // canciones con la etiqueta

    // ------------------------------ Biblias ---------------------------------
    QStringList bibleVersions();
    bool importBibleFromJsonResource(const QString &resourcePath, const QString &versionCode,
                                     const QString &licenseNote, QString *error);
    // v1.3.0: importador de Biblias en formato ZEFania XML (.xml) — el formato
    // estándar del ecosistema Holyrics (miles de versiones libres disponibles).
    // <XMLBIBLE><BIBLEBOOK bnumber><CHAPTER cnumber><VERSE vnumber>texto.
    bool importBibleFromZefaniaXml(const QString &filePath, QString *error);
    QVector<BibleRef::Verse> bibleChapter(const QString &version, int book, int chapter);
    BibleRef::Verse bibleVerse(const QString &version, int book, int chapter, int verse);
    QVector<BibleRef::Verse> bibleRange(const QString &version, int book, int chapter, int vFrom, int vTo);
    QVector<QPair<BibleRef::VerseRef, QString>> bibleWordSearch(const QString &version,
                                                                const QString &term, int limit = 60);

    // ------------------------------ Cultos ----------------------------------
    int  createPlaylist(const QString &name);
    bool deletePlaylist(int id);
    QVector<QPair<int, QString>> playlists();
    QVector<ServiceItem> playlistItems(int playlistId);
    int  addPlaylistItem(int playlistId, const ServiceItem &item);   // al final
    bool removePlaylistItem(int itemId);
    bool movePlaylistItem(int itemId, bool up);
    bool clearPlaylistItems(int playlistId);

    // ------------------------------ Temas -----------------------------------
    QVector<QPair<int, QString>> themes();
    Theme themeById(int id);
    // v1.2.0: devuelve el id del tema (insert o update). Antes devolvía bool
    // y el id de un tema recién creado se perdía — "Guardar como nuevo" dejaba
    // el editor apuntando a id 0 y el siguiente guardado fallaba en silencio.
    int  saveTheme(const Theme &t);
    bool deleteTheme(int id);

    // --------------------------- Slides personalizadas ----------------------
    int  addCustomSlide(const QString &name, const QJsonObject &itemsJson);
    bool updateCustomSlide(int id, const QString &name, const QJsonObject &itemsJson);
    bool deleteCustomSlide(int id);
    QVector<QPair<int, QString>> customSlides();
    QJsonObject customSlideJson(int id, bool *ok = nullptr);

    // ------------------------------ Historial -------------------------------
    struct HistoryRow { QString title; int count; QString lastUsed; };
    QVector<HistoryRow> songReport();
    struct UsageRow { QString usedAt; QString title; };
    QVector<UsageRow> recentUsage(int limit = 200);
    void logAlert(const QString &text);

    // ------------------------------ Ajustes ---------------------------------
    void  setSetting(const QString &key, const QString &value);
    QString setting(const QString &key, const QString &defaultValue = QString());

    // -------------------- Copia de seguridad (v1.3.0) -----------------------
    // Alternativa offline-safe a la sincronización en la nube (spec Holyrics:
    // Drive). Usa la Online Backup API de SQLite (consistente, sin bloquear).
    bool backupTo(const QString &destFile, QString *error = nullptr);
    bool restoreFrom(const QString &srcFile, QString *error = nullptr);   // aplicar al reiniciar
    void autoBackupIfNeeded(const QString &backupDir);                    // semanal, rota 4

    // ------------------------------ Utilidades ------------------------------
    bool exec(const QString &sql);
    bool begin()  { return exec(QStringLiteral("BEGIN")); }
    bool commit() { return exec(QStringLiteral("COMMIT")); }
    bool rollback(){ return exec(QStringLiteral("ROLLBACK")); }
    qint64 scalar(const QString &sql);

private:
    bool ensureSchema(QString *error);
    void seedDefaults();
    bool stmtExec(const char *sql, const QVector<QVariant> &binds = QVector<QVariant>());
    sqlite3_stmt *prepare(const QString &sql);

    sqlite3 *m_db = nullptr;
    QString  m_lastError;
};

#endif // LUMINA_DATABASE_H
