// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Database.cpp : Implementacion SQLite3 (API C) + FTS5.
// ============================================================================
#include "Database.h"

#include <QFile>
#include <QJsonDocument>
#include <QDateTime>
#include <QDebug>
#include <QLoggingCategory>
#include <QXmlStreamReader>
#include <QRegularExpression>
#include <QDir>
#include <QFileInfo>

#include "sqlite3.h"

Database::Database(QObject *parent) : QObject(parent) {}

Database::~Database() { close(); }

void Database::close()
{
    if (m_db) { sqlite3_close(m_db); m_db = nullptr; }
}

bool Database::exec(const QString &sql)
{
    if (!m_db) return false;
    char *err = nullptr;
    if (sqlite3_exec(m_db, sql.toUtf8().constData(), nullptr, nullptr, &err) != SQLITE_OK) {
        m_lastError = err ? QString::fromUtf8(err) : QStringLiteral("sqlite error");
        if (err) sqlite3_free(err);
        qWarning() << "[DB] exec error:" << m_lastError << "sql:" << sql.left(120);
        return false;
    }
    return true;
}

sqlite3_stmt *Database::prepare(const QString &sql)
{
    if (!m_db) return nullptr;
    sqlite3_stmt *st = nullptr;
    if (sqlite3_prepare_v2(m_db, sql.toUtf8().constData(), -1, &st, nullptr) != SQLITE_OK) {
        m_lastError = QString::fromUtf8(sqlite3_errmsg(m_db));
        qWarning() << "[DB] prepare error:" << m_lastError << "sql:" << sql.left(120);
        return nullptr;
    }
    return st;
}

static void bindVariant(sqlite3_stmt *st, int idx, const QVariant &v)
{
    switch (v.userType()) {
    case QMetaType::Int:
    case QMetaType::UInt:
    case QMetaType::LongLong:
    case QMetaType::ULongLong:
        sqlite3_bind_int64(st, idx, v.toLongLong()); break;
    case QMetaType::Double:
        sqlite3_bind_double(st, idx, v.toDouble()); break;
    case QMetaType::UnknownType:
        sqlite3_bind_null(st, idx); break;
    default:
        sqlite3_bind_text(st, idx, v.toString().toUtf8().constData(), -1, SQLITE_TRANSIENT);
    }
}

bool Database::stmtExec(const char *sql, const QVector<QVariant> &binds)
{
    sqlite3_stmt *st = nullptr;
    if (sqlite3_prepare_v2(m_db, sql, -1, &st, nullptr) != SQLITE_OK) {
        m_lastError = QString::fromUtf8(sqlite3_errmsg(m_db));
        return false;
    }
    for (int i = 0; i < binds.size(); ++i)
        bindVariant(st, i + 1, binds.at(i));
    const int rc = sqlite3_step(st);
    sqlite3_finalize(st);
    if (rc != SQLITE_DONE && rc != SQLITE_ROW) {
        m_lastError = QString::fromUtf8(sqlite3_errmsg(m_db));
        return false;
    }
    return true;
}

qint64 Database::scalar(const QString &sql)
{
    sqlite3_stmt *st = prepare(sql);
    if (!st) return 0;
    qint64 v = 0;
    if (sqlite3_step(st) == SQLITE_ROW)
        v = sqlite3_column_int64(st, 0);
    sqlite3_finalize(st);
    return v;
}

bool Database::open(const QString &dbFile, QString *error)
{
    close();
    const QByteArray path = dbFile.toUtf8();
    const int flags = SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE | SQLITE_OPEN_FULLMUTEX;
    if (sqlite3_open_v2(path.constData(), &m_db, flags, nullptr) != SQLITE_OK) {
        m_lastError = m_db ? QString::fromUtf8(sqlite3_errmsg(m_db)) : QStringLiteral("cannot open");
        if (error) *error = m_lastError;
        // CORRECCION v1.2.0: si la apertura falla, el handle queda != null y
        // isOpen() devolvía true tras un open() fallido (handle erróneo nunca
        // cerrado). Se cierra y se anula en el propio punto de fallo.
        if (m_db) { sqlite3_close(m_db); m_db = nullptr; }
        return false;
    }
    // Rendimiento: WAL + synchronous NORMAL (seguro y rapido en HDD legacy)
    exec(QStringLiteral("PRAGMA journal_mode=WAL"));
    exec(QStringLiteral("PRAGMA synchronous=NORMAL"));
    exec(QStringLiteral("PRAGMA foreign_keys=ON"));
    exec(QStringLiteral("PRAGMA cache_size=-8000"));    // ~8MB cache
    if (!ensureSchema(error)) return false;
    seedDefaults();
    return true;
}

bool Database::ensureSchema(QString *error)
{
    const QString schema = QStringLiteral(R"SQL(
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
    INSERT INTO bible_fts(bible_fts, rowid, text, version) VALUES ('delete', old.id, old.text, old.version);
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
CREATE TABLE IF NOT EXISTS custom_slides (
    id INTEGER PRIMARY KEY AUTOINCREMENT,
    name TEXT NOT NULL,
    json TEXT NOT NULL,
    updated_at TEXT
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
-- v1.0.3: Sistema de etiquetas (tags) semanticas para canciones.
-- Permite asignar palabras clave (ej: "lento", "navidad", "entrada",
-- "ofrenda") y luego filtrar/buscar por etiqueta — feature del spec
-- Holyrics descrito como "inteligente y subestimado".
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
    )SQL");
    if (!exec(schema)) {
        if (error) *error = m_lastError;
        return false;
    }
    return true;
}

void Database::seedDefaults()
{
    if (scalar(QStringLiteral("SELECT COUNT(*) FROM themes")) == 0) {
        // Tema 1: Clasico Azul
        Theme t1 = Theme::defaultTheme();
        t1.name = QStringLiteral("Clásico Azul");
        saveTheme(t1);
        // Tema 2: Dorado Elegante
        Theme t2 = Theme::defaultTheme();
        t2.name = QStringLiteral("Dorado Elegante");
        t2.background = BackgroundStyle();
        t2.background.color1 = QColor(24, 16, 4);
        t2.background.color2 = QColor(96, 66, 10);
        t2.body.color = QColor(255, 216, 130);
        t2.title.color = QColor(255, 232, 170);
        t2.body.outlineColor = QColor(40, 20, 0);
        saveTheme(t2);
        // Tema 3: Minimal Blanco
        Theme t3 = Theme::defaultTheme();
        t3.name = QStringLiteral("Minimal Blanco");
        t3.background.color1 = QColor(250, 250, 250);
        t3.background.color2 = QColor(225, 228, 235);
        t3.body.color = QColor(20, 28, 48);
        t3.title.color = QColor(10, 14, 30);
        t3.body.shadow = false;
        saveTheme(t3);
    }
}

// ---------------------------------------------------------------------------
// Canciones
// ---------------------------------------------------------------------------
int Database::addSong(const Song &s)
{
    stmtExec("INSERT INTO songs(title, artist, key, bpm, lyrics, updated_at) VALUES(?,?,?,?,?,?)",
             { s.title, s.artist, s.key, s.bpm, s.lyrics,
               QDateTime::currentDateTime().toString(Qt::ISODate) });
    const qint64 id = scalar(QStringLiteral("SELECT last_insert_rowid()"));
    return static_cast<int>(id);
}

bool Database::updateSong(const Song &s)
{
    return stmtExec("UPDATE songs SET title=?, artist=?, key=?, bpm=?, lyrics=?, updated_at=? WHERE id=?",
                    { s.title, s.artist, s.key, s.bpm, s.lyrics,
                      QDateTime::currentDateTime().toString(Qt::ISODate), s.id });
}

bool Database::deleteSong(int id)
{
    stmtExec("DELETE FROM songs WHERE id=?", { id });
    stmtExec("DELETE FROM song_stats WHERE song_id=?", { id });
    return true;
}

Song Database::songById(int id)
{
    Song s;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT id,title,artist,key,bpm,lyrics,updated_at FROM songs WHERE id=%1").arg(id));
    if (st && sqlite3_step(st) == SQLITE_ROW) {
        s.id = sqlite3_column_int(st, 0);
        s.title = QString::fromUtf8((const char*)sqlite3_column_text(st, 1));
        s.artist = QString::fromUtf8((const char*)sqlite3_column_text(st, 2));
        s.key = QString::fromUtf8((const char*)sqlite3_column_text(st, 3));
        s.bpm = sqlite3_column_int(st, 4);
        s.lyrics = QString::fromUtf8((const char*)sqlite3_column_text(st, 5));
        s.updatedAt = QString::fromUtf8((const char*)sqlite3_column_text(st, 6));
    }
    if (st) sqlite3_finalize(st);
    return s;
}

static QVector<SongRow> rowsFromStmt(sqlite3_stmt *st)
{
    QVector<SongRow> out;
    if (!st) return out;
    while (sqlite3_step(st) == SQLITE_ROW) {
        SongRow r;
        r.id = sqlite3_column_int(st, 0);
        r.title = QString::fromUtf8((const char*)sqlite3_column_text(st, 1));
        r.artist = QString::fromUtf8((const char*)sqlite3_column_text(st, 2));
        r.key = QString::fromUtf8((const char*)sqlite3_column_text(st, 3));
        r.bpm = sqlite3_column_int(st, 4);
        out.append(r);
    }
    sqlite3_finalize(st);
    return out;
}

QVector<SongRow> Database::searchSongs(const QString &term, int limit)
{
    QString t = term.simplified();
    if (t.isEmpty()) return allSongs();
    // FTS5: prefijos por palabra.
    // CORRECCION: sanitizacion robusta — se envuelve CADA palabra entre
    // comillas dobles para que caracteres como '-', ':', '(' o 'AND' no
    // rompan la sintaxis FTS5 (antes fallaba en silencio y devolvia vacio).
    QStringList words;
    const QStringList parts = t.split(QChar(' '), Qt::SkipEmptyParts);
    for (const QString &p : parts) {
        QString w = p;
        w.remove(QChar('"'));
        w.remove(QChar('\''));
        if (!w.isEmpty()) words << QStringLiteral("\"%1\"*").arg(w);
    }
    if (words.isEmpty()) return allSongs();
    const QString match = words.join(QStringLiteral(" "));
    // CORRECCION CRITICA: el ORDER BY rank estaba en la query EXTERNA
    // (tabla songs, que no tiene columna rank) — la busqueda de canciones
    // fallaba en silencio y devolvia SIEMPRE 0 resultados. El rank pertenece
    // al subquery de songs_fts.
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT s.id, s.title, s.artist, s.key, s.bpm FROM songs s WHERE s.id IN "
        "(SELECT rowid FROM songs_fts WHERE songs_fts MATCH '%1' ORDER BY rank) "
        "ORDER BY s.title COLLATE NOCASE LIMIT %2").arg(match).arg(limit));
    return rowsFromStmt(st);
}

QVector<SongRow> Database::allSongs()
{
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT id,title,artist,key,bpm FROM songs ORDER BY title COLLATE NOCASE"));
    return rowsFromStmt(st);
}

void Database::touchSongUsage(int songId)
{
    const QString now = QDateTime::currentDateTime().toString(Qt::ISODate);
    stmtExec("INSERT INTO history(song_id, used_at) VALUES(?,?)", { songId, now });
    stmtExec("INSERT INTO song_stats(song_id, use_count, last_used) VALUES(?,1,?) "
             "ON CONFLICT(song_id) DO UPDATE SET use_count=use_count+1, last_used=excluded.last_used",
             { songId, now });
}

// ---------------------------------------------------------------------------
// Etiquetas (tags) semanticas — v1.0.3
// ---------------------------------------------------------------------------
int Database::addTag(const QString &name)
{
    QString n = name.trimmed();
    if (n.isEmpty()) return 0;
    // INSERT OR IGNORE: si ya existe (UNIQUE COLLATE NOCASE), no falla.
    stmtExec("INSERT OR IGNORE INTO tags(name) VALUES(?)", { n });
    // Recuperar el id (existente o recien creado) con binding seguro
    // (evita inyeccion SQL y problemas de escape de comillas).
    int id = 0;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT id FROM tags WHERE name=? COLLATE NOCASE"));
    if (st) {
        sqlite3_bind_text(st, 1, n.toUtf8().constData(), -1, SQLITE_TRANSIENT);
        if (sqlite3_step(st) == SQLITE_ROW)
            id = sqlite3_column_int(st, 0);
        sqlite3_finalize(st);
    }
    return id;
}

bool Database::setSongTags(int songId, const QStringList &tagNames)
{
    if (!begin()) return false;
    stmtExec("DELETE FROM song_tags WHERE song_id=?", { songId });
    for (const QString &raw : tagNames) {
        const QString n = raw.trimmed();
        if (n.isEmpty()) continue;
        const int tagId = addTag(n);
        if (tagId > 0)
            stmtExec("INSERT OR IGNORE INTO song_tags(song_id, tag_id) VALUES(?,?)",
                     { songId, tagId });
    }
    return commit();
}

QStringList Database::songTags(int songId)
{
    QStringList out;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT t.name FROM tags t INNER JOIN song_tags st ON st.tag_id=t.id "
        "WHERE st.song_id=%1 ORDER BY t.name COLLATE NOCASE").arg(songId));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW)
            out << QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
        sqlite3_finalize(st);
    }
    return out;
}

QVector<QPair<int, QString>> Database::allTags()
{
    QVector<QPair<int, QString>> out;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT t.id, t.name, COUNT(st.song_id) AS uses "
        "FROM tags t LEFT JOIN song_tags st ON st.tag_id=t.id "
        "GROUP BY t.id ORDER BY uses DESC, t.name COLLATE NOCASE"));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW) {
            out.append(qMakePair(sqlite3_column_int(st, 0),
                                  QString::fromUtf8((const char*)sqlite3_column_text(st, 1))));
        }
        sqlite3_finalize(st);
    }
    return out;
}

QVector<SongRow> Database::searchByTag(const QString &tag)
{
    const QString t = tag.trimmed().replace('\'', QStringLiteral("''"));
    if (t.isEmpty()) return allSongs();
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT s.id, s.title, s.artist, s.key, s.bpm FROM songs s "
        "WHERE s.id IN (SELECT st.song_id FROM song_tags st "
        "               INNER JOIN tags t ON t.id=st.tag_id "
        "               WHERE t.name='%1' COLLATE NOCASE) "
        "ORDER BY s.title COLLATE NOCASE").arg(t));
    return rowsFromStmt(st);
}

// ---------------------------------------------------------------------------
// Biblias
// ---------------------------------------------------------------------------
QStringList Database::bibleVersions()
{
    QStringList out;
    sqlite3_stmt *st = prepare(QStringLiteral("SELECT DISTINCT version FROM bible ORDER BY version"));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW)
            out << QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
        sqlite3_finalize(st);
    }
    return out;
}

bool Database::importBibleFromJsonResource(const QString &resourcePath, const QString &versionCode,
                                           const QString &licenseNote, QString *error)
{
    if (scalar(QStringLiteral("SELECT COUNT(*) FROM bible WHERE version='%1'")
                   .arg(QString(versionCode).replace(QChar('\''), QStringLiteral("''")))) > 0)
        return true;    // ya importada

    QFile f(resourcePath);
    if (!f.open(QIODevice::ReadOnly)) {
        if (error) *error = QStringLiteral("No se pudo abrir el recurso biblico: %1").arg(resourcePath);
        return false;
    }
    QJsonDocument doc = QJsonDocument::fromJson(f.readAll());
    f.close();
    if (!doc.isObject()) {
        if (error) *error = QStringLiteral("JSON biblico invalido");
        return false;
    }
    const QJsonObject root = doc.object();
    const QJsonArray books = root.value(QStringLiteral("books")).toArray();

    // Importacion masiva: transaccion atomica + synchronous OFF (per spec)
    exec(QStringLiteral("PRAGMA synchronous=OFF"));
    begin();
    bool ok = true;
    for (const QJsonValue &bv : books) {
        const QJsonObject bo = bv.toObject();
        const int bookNum = bo.value(QStringLiteral("n")).toInt();
        const QJsonArray chapters = bo.value(QStringLiteral("chapters")).toArray();
        for (int ci = 0; ci < chapters.size(); ++ci) {
            const QJsonArray verses = chapters.at(ci).toArray();
            for (int vi = 0; vi < verses.size(); ++vi) {
                const QString text = verses.at(vi).toString();
                if (!stmtExec("INSERT INTO bible(version,book,chapter,verse,text) VALUES(?,?,?,?,?)",
                              { versionCode, bookNum, ci + 1, vi + 1, text })) {
                    ok = false;
                    break;
                }
            }
        }
    }
    if (ok) commit(); else rollback();
    exec(QStringLiteral("PRAGMA synchronous=NORMAL"));
    qInfo() << "[DB] Biblia" << versionCode << "importada. Licencia:" << licenseNote;
    return ok;
}

QVector<BibleRef::Verse> Database::bibleChapter(const QString &version, int book, int chapter)
{
    QVector<BibleRef::Verse> out;
    sqlite3_stmt *st = nullptr;
    const char *sql = "SELECT book,chapter,verse,text FROM bible WHERE version=? AND book=? AND chapter=? ORDER BY verse";
    if (sqlite3_prepare_v2(m_db, sql, -1, &st, nullptr) == SQLITE_OK) {
        sqlite3_bind_text(st, 1, version.toUtf8().constData(), -1, SQLITE_TRANSIENT);
        sqlite3_bind_int(st, 2, book);
        sqlite3_bind_int(st, 3, chapter);
        while (sqlite3_step(st) == SQLITE_ROW) {
            BibleRef::Verse v;
            v.version = version;
            v.ref.book = sqlite3_column_int(st, 0);
            v.ref.chapter = sqlite3_column_int(st, 1);
            v.ref.verse = sqlite3_column_int(st, 2);
            v.text = QString::fromUtf8((const char*)sqlite3_column_text(st, 3));
            const QVector<BibleRef::BookInfo> &tb = BibleRef::books();
            if (v.ref.book >= 1 && v.ref.book <= tb.size()) v.ref.bookName = tb.at(v.ref.book - 1).name;
            out.append(v);
        }
    }
    if (st) sqlite3_finalize(st);
    return out;
}

BibleRef::Verse Database::bibleVerse(const QString &version, int book, int chapter, int verse)
{
    BibleRef::Verse out;
    const QVector<BibleRef::Verse> v = bibleRange(version, book, chapter, verse, verse);
    if (!v.isEmpty()) out = v.first();
    return out;
}

QVector<BibleRef::Verse> Database::bibleRange(const QString &version, int book, int chapter, int vFrom, int vTo)
{
    QVector<BibleRef::Verse> out;
    sqlite3_stmt *st = nullptr;
    const char *sql = "SELECT book,chapter,verse,text FROM bible WHERE version=? AND book=? AND chapter=? "
                      "AND verse BETWEEN ? AND ? ORDER BY verse";
    if (sqlite3_prepare_v2(m_db, sql, -1, &st, nullptr) == SQLITE_OK) {
        sqlite3_bind_text(st, 1, version.toUtf8().constData(), -1, SQLITE_TRANSIENT);
        sqlite3_bind_int(st, 2, book);
        sqlite3_bind_int(st, 3, chapter);
        sqlite3_bind_int(st, 4, qMin(vFrom, vTo));
        sqlite3_bind_int(st, 5, qMax(vFrom, vTo));
        while (sqlite3_step(st) == SQLITE_ROW) {
            BibleRef::Verse v;
            v.version = version;
            v.ref.book = sqlite3_column_int(st, 0);
            v.ref.chapter = sqlite3_column_int(st, 1);
            v.ref.verse = sqlite3_column_int(st, 2);
            v.text = QString::fromUtf8((const char*)sqlite3_column_text(st, 3));
            const QVector<BibleRef::BookInfo> &tb = BibleRef::books();
            if (v.ref.book >= 1 && v.ref.book <= tb.size()) v.ref.bookName = tb.at(v.ref.book - 1).name;
            out.append(v);
        }
    }
    if (st) sqlite3_finalize(st);
    return out;
}

QVector<QPair<BibleRef::VerseRef, QString>> Database::bibleWordSearch(const QString &version,
                                                                      const QString &term, int limit)
{
    QVector<QPair<BibleRef::VerseRef, QString>> out;
    // CORRECCION: misma sanitizacion FTS5 robusta que searchSongs.
    QStringList words;
    for (const QString &p : term.simplified().split(QChar(' '), Qt::SkipEmptyParts)) {
        QString w = p;
        w.remove(QChar('"'));
        w.remove(QChar('\''));
        if (!w.isEmpty()) words << QStringLiteral("\"%1\"*").arg(w);
    }
    if (words.isEmpty()) return out;
    const QString match = words.join(QStringLiteral(" "));
    QString verSafe = version;
    verSafe.replace(QChar('\''), QStringLiteral("''"));
    sqlite3_stmt *st = nullptr;
    const QString sql = QStringLiteral(
        "SELECT b.book,b.chapter,b.verse,b.text FROM bible b WHERE b.id IN "
        "(SELECT rowid FROM bible_fts WHERE bible_fts MATCH '%1' AND version='%2') "
        "AND b.version='%2' ORDER BY b.book,b.chapter,b.verse LIMIT %3")
            .arg(match).arg(verSafe).arg(limit);
    if (sqlite3_prepare_v2(m_db, sql.toUtf8().constData(), -1, &st, nullptr) == SQLITE_OK) {
        while (sqlite3_step(st) == SQLITE_ROW) {
            BibleRef::VerseRef r;
            r.book = sqlite3_column_int(st, 0);
            r.chapter = sqlite3_column_int(st, 1);
            r.verse = sqlite3_column_int(st, 2);
            const QVector<BibleRef::BookInfo> &tb = BibleRef::books();
            if (r.book >= 1 && r.book <= tb.size()) r.bookName = tb.at(r.book - 1).name;
            out.append({ r, QString::fromUtf8((const char*)sqlite3_column_text(st, 3)) });
        }
        sqlite3_finalize(st);
    }
    return out;
}

// ---------------------------------------------------------------------------
// Cultos / Playlists
// ---------------------------------------------------------------------------
int Database::createPlaylist(const QString &name)
{
    stmtExec("INSERT INTO playlists(name, created_at) VALUES(?,?)",
             { name, QDateTime::currentDateTime().toString(Qt::ISODate) });
    return static_cast<int>(scalar(QStringLiteral("SELECT last_insert_rowid()")));
}

bool Database::deletePlaylist(int id)
{
    stmtExec("DELETE FROM playlist_items WHERE playlist_id=?", { id });
    return stmtExec("DELETE FROM playlists WHERE id=?", { id });
}

QVector<QPair<int, QString>> Database::playlists()
{
    QVector<QPair<int, QString>> out;
    sqlite3_stmt *st = prepare(QStringLiteral("SELECT id,name FROM playlists ORDER BY id DESC"));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW)
            out.append({ sqlite3_column_int(st, 0), QString::fromUtf8((const char*)sqlite3_column_text(st, 1)) });
        sqlite3_finalize(st);
    }
    return out;
}

QVector<ServiceItem> Database::playlistItems(int playlistId)
{
    QVector<ServiceItem> out;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT id,kind,ref_id,label,payload FROM playlist_items WHERE playlist_id=%1 ORDER BY position")
            .arg(playlistId));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW) {
            ServiceItem it;
            it.id = sqlite3_column_int(st, 0);
            it.kind = sqlite3_column_int(st, 1);
            it.refId = sqlite3_column_int(st, 2);
            it.label = QString::fromUtf8((const char*)sqlite3_column_text(st, 3));
            it.payload = QString::fromUtf8((const char*)sqlite3_column_text(st, 4));
            out.append(it);
        }
        sqlite3_finalize(st);
    }
    return out;
}

int Database::addPlaylistItem(int playlistId, const ServiceItem &item)
{
    const qint64 maxPos = scalar(QStringLiteral(
        "SELECT COALESCE(MAX(position),0) FROM playlist_items WHERE playlist_id=%1").arg(playlistId));
    stmtExec("INSERT INTO playlist_items(playlist_id,position,kind,ref_id,label,payload) VALUES(?,?,?,?,?,?)",
             { playlistId, static_cast<int>(maxPos) + 1, item.kind, item.refId, item.label, item.payload });
    return static_cast<int>(scalar(QStringLiteral("SELECT last_insert_rowid()")));
}

bool Database::removePlaylistItem(int itemId)
{
    return stmtExec("DELETE FROM playlist_items WHERE id=?", { itemId });
}

bool Database::movePlaylistItem(int itemId, bool up)
{
    // Intercambia posiciones con el vecino
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT playlist_id, position FROM playlist_items WHERE id=%1").arg(itemId));
    int pid = 0, pos = 0;
    if (st && sqlite3_step(st) == SQLITE_ROW) {
        pid = sqlite3_column_int(st, 0);
        pos = sqlite3_column_int(st, 1);
    }
    if (st) sqlite3_finalize(st);
    if (!pid) return false;
    const int other = up ? pos - 1 : pos + 1;
    sqlite3_stmt *st2 = prepare(QStringLiteral(
        "SELECT id FROM playlist_items WHERE playlist_id=%1 AND position=%2").arg(pid).arg(other));
    int otherId = 0;
    if (st2 && sqlite3_step(st2) == SQLITE_ROW) otherId = sqlite3_column_int(st2, 0);
    if (st2) sqlite3_finalize(st2);
    if (!otherId) return false;
    stmtExec("UPDATE playlist_items SET position=? WHERE id=?", { other, itemId });
    stmtExec("UPDATE playlist_items SET position=? WHERE id=?", { pos, otherId });
    return true;
}

bool Database::clearPlaylistItems(int playlistId)
{
    return stmtExec("DELETE FROM playlist_items WHERE playlist_id=?", { playlistId });
}

// ---------------------------------------------------------------------------
// Temas
// ---------------------------------------------------------------------------
QVector<QPair<int, QString>> Database::themes()
{
    QVector<QPair<int, QString>> out;
    sqlite3_stmt *st = prepare(QStringLiteral("SELECT id,name FROM themes ORDER BY name COLLATE NOCASE"));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW)
            out.append({ sqlite3_column_int(st, 0), QString::fromUtf8((const char*)sqlite3_column_text(st, 1)) });
        sqlite3_finalize(st);
    }
    return out;
}

Theme Database::themeById(int id)
{
    Theme t = Theme::defaultTheme();
    sqlite3_stmt *st = prepare(QStringLiteral("SELECT json FROM themes WHERE id=%1").arg(id));
    if (st && sqlite3_step(st) == SQLITE_ROW) {
        const QString json = QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
        t = Theme::fromJson(QJsonDocument::fromJson(json.toUtf8()).object());
        t.id = id;
    }
    if (st) sqlite3_finalize(st);
    return t;
}

int Database::saveTheme(const Theme &t)
{
    const QString json = QString::fromUtf8(QJsonDocument(t.toJson()).toJson(QJsonDocument::Compact));
    if (t.id > 0) {
        if (!stmtExec("UPDATE themes SET name=?, json=? WHERE id=?", { t.name, json, t.id }))
            return -1;
        return t.id;
    }
    if (!stmtExec("INSERT INTO themes(name, json) VALUES(?,?)", { t.name, json }))
        return -1;
    return static_cast<int>(scalar(QStringLiteral("SELECT last_insert_rowid()")));
}

bool Database::deleteTheme(int id)
{
    return stmtExec("DELETE FROM themes WHERE id=?", { id });
}

// ---------------------------------------------------------------------------
// Slides personalizadas (lienzo vectorial)
// ---------------------------------------------------------------------------
int Database::addCustomSlide(const QString &name, const QJsonObject &itemsJson)
{
    const QString json = QString::fromUtf8(QJsonDocument(itemsJson).toJson(QJsonDocument::Compact));
    stmtExec("INSERT INTO custom_slides(name,json,updated_at) VALUES(?,?,?)",
             { name, json, QDateTime::currentDateTime().toString(Qt::ISODate) });
    return static_cast<int>(scalar(QStringLiteral("SELECT last_insert_rowid()")));
}

bool Database::updateCustomSlide(int id, const QString &name, const QJsonObject &itemsJson)
{
    const QString json = QString::fromUtf8(QJsonDocument(itemsJson).toJson(QJsonDocument::Compact));
    return stmtExec("UPDATE custom_slides SET name=?, json=?, updated_at=? WHERE id=?",
                    { name, json, QDateTime::currentDateTime().toString(Qt::ISODate), id });
}

bool Database::deleteCustomSlide(int id)
{
    return stmtExec("DELETE FROM custom_slides WHERE id=?", { id });
}

QVector<QPair<int, QString>> Database::customSlides()
{
    QVector<QPair<int, QString>> out;
    sqlite3_stmt *st = prepare(QStringLiteral("SELECT id,name FROM custom_slides ORDER BY id DESC"));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW)
            out.append({ sqlite3_column_int(st, 0), QString::fromUtf8((const char*)sqlite3_column_text(st, 1)) });
        sqlite3_finalize(st);
    }
    return out;
}

QJsonObject Database::customSlideJson(int id, bool *ok)
{
    if (ok) *ok = false;
    sqlite3_stmt *st = prepare(QStringLiteral("SELECT json FROM custom_slides WHERE id=%1").arg(id));
    QJsonObject out;
    if (st && sqlite3_step(st) == SQLITE_ROW) {
        const QString json = QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
        out = QJsonDocument::fromJson(json.toUtf8()).object();
        if (ok) *ok = !out.isEmpty() || json == QStringLiteral("{}");
    }
    if (st) sqlite3_finalize(st);
    return out;
}

// ---------------------------------------------------------------------------
// Historial / reportes
// ---------------------------------------------------------------------------
QVector<Database::HistoryRow> Database::songReport()
{
    QVector<HistoryRow> out;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT s.title, COALESCE(st.use_count,0), COALESCE(st.last_used,'') FROM songs s "
        "LEFT JOIN song_stats st ON st.song_id=s.id ORDER BY st.use_count DESC, s.title COLLATE NOCASE"));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW) {
            HistoryRow r;
            r.title = QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
            r.count = sqlite3_column_int(st, 1);
            r.lastUsed = QString::fromUtf8((const char*)sqlite3_column_text(st, 2));
            out.append(r);
        }
        sqlite3_finalize(st);
    }
    return out;
}

QVector<Database::UsageRow> Database::recentUsage(int limit)
{
    QVector<UsageRow> out;
    sqlite3_stmt *st = prepare(QStringLiteral(
        "SELECT h.used_at, COALESCE(s.title,'?') FROM history h LEFT JOIN songs s ON s.id=h.song_id "
        "ORDER BY h.id DESC LIMIT %1").arg(limit));
    if (st) {
        while (sqlite3_step(st) == SQLITE_ROW) {
            UsageRow r;
            r.usedAt = QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
            r.title = QString::fromUtf8((const char*)sqlite3_column_text(st, 1));
            out.append(r);
        }
        sqlite3_finalize(st);
    }
    return out;
}

void Database::logAlert(const QString &text)
{
    stmtExec("INSERT INTO alerts_log(text, created_at) VALUES(?,?)",
             { text, QDateTime::currentDateTime().toString(Qt::ISODate) });
}

// ---------------------------------------------------------------------------
// Ajustes
// ---------------------------------------------------------------------------
void Database::setSetting(const QString &key, const QString &value)
{
    stmtExec("INSERT INTO settings(key,value) VALUES(?,?) "
             "ON CONFLICT(key) DO UPDATE SET value=excluded.value", { key, value });
}

QString Database::setting(const QString &key, const QString &defaultValue)
{
    sqlite3_stmt *st = nullptr;
    const char *sql = "SELECT value FROM settings WHERE key=?";
    QString out = defaultValue;
    if (sqlite3_prepare_v2(m_db, sql, -1, &st, nullptr) == SQLITE_OK) {
        sqlite3_bind_text(st, 1, key.toUtf8().constData(), -1, SQLITE_TRANSIENT);
        if (sqlite3_step(st) == SQLITE_ROW && sqlite3_column_text(st, 0))
            out = QString::fromUtf8((const char*)sqlite3_column_text(st, 0));
        sqlite3_finalize(st);
    }
    return out;
}

// ---------------------------------------------------------------------------
// v1.3.0 — Importador de Biblias ZEFania XML (formato del ecosistema Holyrics)
// ---------------------------------------------------------------------------
// Estructura esperada:
//   <XMLBIBLE biblename="Reina Valera 1960" ...>
//     <BIBLEBOOK bnumber="1" bname="Génesis">
//       <CHAPTER cnumber="1">
//         <VERSE vnumber="1">En el principio creó Dios...</VERSE>
// Especificación: https://www.bgfdb.de/zefania/ — miles de versiones libres.
bool Database::importBibleFromZefaniaXml(const QString &filePath, QString *error)
{
    QFile f(filePath);
    if (!f.open(QIODevice::ReadOnly | QIODevice::Text)) {
        if (error) *error = QStringLiteral("No se pudo abrir el archivo: %1").arg(filePath);
        return false;
    }

    QXmlStreamReader xml(&f);
    QString versionCode;         // p.ej. "RV1960" (biblename saneado)
    QString description;
    int book = 0, chapter = 0;
    qint64 inserted = 0;
    bool inVerse = false;
    QString verseText;
    int verseNum = 0;

    // Importacion atomica: transaccion + synchronous OFF (mismo patron que JSON)
    exec(QStringLiteral("PRAGMA synchronous=OFF"));
    begin();
    bool ok = true;

    while (!xml.atEnd()) {
        const QXmlStreamReader::TokenType tok = xml.readNext();
        if (tok == QXmlStreamReader::Invalid)
            break;
        if (tok == QXmlStreamReader::StartElement) {
            const QString name = xml.name().toString();   // nombre LOCAL (sin prefijo)
            if (name == QLatin1String("XMLBIBLE")) {
                for (const QXmlStreamAttribute &a : xml.attributes()) {
                    if (a.name() == QLatin1String("biblename"))
                        description = a.value().toString().trimmed();
                }
                versionCode = QFileInfo(filePath).completeBaseName().toUpper();
                versionCode.remove(QRegularExpression(QStringLiteral("[^A-Z0-9]")));
                if (versionCode.size() > 16) versionCode = versionCode.left(16);
                if (versionCode.isEmpty()) versionCode = QStringLiteral("ZEFANIA");
            } else if (name == QLatin1String("BIBLEBOOK")) {
                book = xml.attributes().value(QLatin1String("bnumber")).toInt();
            } else if (name == QLatin1String("CHAPTER")) {
                chapter = xml.attributes().value(QLatin1String("cnumber")).toInt();
            } else if (name == QLatin1String("VERSE")) {
                verseNum = xml.attributes().value(QLatin1String("vnumber")).toInt();
                verseText.clear();
                inVerse = true;
            } else if (inVerse) {
                // etiquetas anidadas dentro del versiculo (p.ej. <BR/>, <STYLE>):
                // separador de linea para BR, contenido textual del resto
                if (name == QLatin1String("BR"))
                    verseText += QStringLiteral(" ");
            }
        } else if (tok == QXmlStreamReader::Characters && inVerse) {
            verseText += xml.text();
        } else if (tok == QXmlStreamReader::EndElement) {
            const QString name = xml.name().toString();
            if (name == QLatin1String("VERSE")) {
                inVerse = false;
                if (book > 0 && chapter > 0 && verseNum > 0) {
                    const QString txt = verseText.simplified();
                    if (!txt.isEmpty()) {
                        if (!stmtExec("INSERT INTO bible(version,book,chapter,verse,text) VALUES(?,?,?,?,?)",
                                      { versionCode, book, chapter, verseNum, txt })) {
                            ok = false;
                            break;
                        }
                        ++inserted;
                    }
                }
            }
        }
    }
    f.close();

    if (xml.hasError()) {
        rollback();
        exec(QStringLiteral("PRAGMA synchronous=NORMAL"));
        if (error) *error = QStringLiteral("XML inválido (ZEFania): %1").arg(xml.errorString());
        return false;
    }
    if (!ok || inserted == 0) {
        rollback();
        exec(QStringLiteral("PRAGMA synchronous=NORMAL"));
        if (error) *error = QStringLiteral("No se encontraron versículos válidos en el archivo.");
        return false;
    }
    commit();
    exec(QStringLiteral("PRAGMA synchronous=NORMAL"));
    qInfo() << "[DB] Biblia ZEFania importada:" << versionCode << description << "-" << inserted << "versículos";
    if (error) *error = description;      // descripción para mostrar al usuario
    return true;
}

// ---------------------------------------------------------------------------
// v1.3.0 — Copia de seguridad / restauración (Online Backup API de SQLite)
// ---------------------------------------------------------------------------
bool Database::backupTo(const QString &destFile, QString *error)
{
    if (!m_db) {
        if (error) *error = QStringLiteral("La base de datos no está abierta.");
        return false;
    }
    sqlite3 *dest = nullptr;
    if (sqlite3_open_v2(destFile.toUtf8().constData(), &dest,
                        SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE, nullptr) != SQLITE_OK) {
        if (dest) sqlite3_close(dest);
        if (error) *error = QStringLiteral("No se pudo crear el archivo de copia.");
        return false;
    }
    sqlite3_backup *bak = sqlite3_backup_init(dest, "main", m_db, "main");
    if (!bak) {
        sqlite3_close(dest);
        if (error) *error = QStringLiteral("Backup init falló: %1").arg(lastError());
        return false;
    }
    const int rc = sqlite3_backup_step(bak, -1);       // copia completa en un paso
    sqlite3_backup_finish(bak);
    const int destErr = sqlite3_errcode(dest);
    sqlite3_close(dest);
    if (rc != SQLITE_DONE || destErr != SQLITE_OK) {
        QFile::remove(destFile);
        if (error) *error = QStringLiteral("La copia de seguridad quedó incompleta (intenta de nuevo).");
        return false;
    }
    return true;
}

bool Database::restoreFrom(const QString &srcFile, QString *error)
{
    if (!m_db) {
        if (error) *error = QStringLiteral("La base de datos no está abierta.");
        return false;
    }
    // Validar que el origen sea un SQLite real ANTES de tocar el vault activo
    {
        sqlite3 *src = nullptr;
        if (sqlite3_open_v2(srcFile.toUtf8().constData(), &src, SQLITE_OPEN_READONLY, nullptr) != SQLITE_OK
            || !src) {
            if (src) sqlite3_close(src);
            if (error) *error = QStringLiteral("El archivo no es una base de datos válida.");
            return false;
        }
        sqlite3_stmt *st = nullptr;
        const bool sane = (sqlite3_prepare_v2(src, "SELECT COUNT(*) FROM sqlite_master", -1,
                                              &st, nullptr) == SQLITE_OK);
        if (st) sqlite3_finalize(st);
        const int err = sqlite3_errcode(src);
        sqlite3_close(src);
        if (!sane || err != SQLITE_OK) {
            if (error) *error = QStringLiteral("El archivo no es una base de datos válida.");
            return false;
        }
    }
    // Copiar el origen DENTRO de la conexión activa (Online Backup API inversa).
    // Consistente incluso con la BD en uso; los paneles recargarán al reiniciar.
    sqlite3 *src = nullptr;
    if (sqlite3_open_v2(srcFile.toUtf8().constData(), &src, SQLITE_OPEN_READONLY, nullptr) != SQLITE_OK) {
        if (error) *error = QStringLiteral("No se pudo abrir la copia de seguridad.");
        return false;
    }
    sqlite3_backup *bak = sqlite3_backup_init(m_db, "main", src, "main");
    if (!bak) {
        sqlite3_close(src);
        if (error) *error = QStringLiteral("Restore init falló: %1").arg(lastError());
        return false;
    }
    const int rc = sqlite3_backup_step(bak, -1);
    sqlite3_backup_finish(bak);
    sqlite3_close(src);
    if (rc != SQLITE_DONE || sqlite3_errcode(m_db) != SQLITE_OK) {
        if (error) *error = QStringLiteral("La restauración quedó incompleta (intenta de nuevo).");
        return false;
    }
    // Reasegurar esquema (por si la copia venía de una versión anterior)
    QString schemaErr;
    ensureSchema(&schemaErr);
    return true;
}

void Database::autoBackupIfNeeded(const QString &backupDir)
{
    const QString last = setting(QStringLiteral("last_auto_backup"));
    const QDateTime lastAt = QDateTime::fromString(last, Qt::ISODate);
    if (lastAt.isValid() && lastAt.daysTo(QDateTime::currentDateTime()) < 7)
        return;                       // copia de esta semana vigente

    QDir().mkpath(backupDir);
    const QString stamp = QDateTime::currentDateTime().toString(QStringLiteral("yyyyMMdd_HHmm"));
    const QString dest = backupDir + QStringLiteral("/auto_%1.db").arg(stamp);
    QString err;
    if (backupTo(dest, &err)) {
        setSetting(QStringLiteral("last_auto_backup"),
                   QDateTime::currentDateTime().toString(Qt::ISODate));
        // Rotación: conservar solo las 4 copias automáticas más recientes
        QDir d(backupDir);
        const QStringList autos = d.entryList(
            QStringList() << QStringLiteral("auto_*.db"),
            QDir::Files, QDir::Name | QDir::Reversed);   // nuevas primero
        for (int i = 4; i < autos.size(); ++i)
            d.remove(autos.at(i));
        qInfo() << "[DB] Copia automática creada:" << dest;
    } else {
        qWarning() << "[DB] Copia automática falló:" << err;
    }
}
