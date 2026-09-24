// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  LuminaStorage.cs : ayudantes de la capa de datos sobre lumina_db_exec.
//  REGLAS: el SQL SIEMPRE usa parámetros enlazados ("params") — jamás se
//  interpola texto de usuario (contrato de la arquitectura). El esquema vive
//  en el núcleo (Storage.cpp: songs / bible / *_fts / settings); aquí solo se
//  construyen las sentencias de la aplicación y se leen los resultados JSON.
// ============================================================================
using System;
using System.Collections.Generic;
using lumina.core;

namespace lumina.bridge
{
    public static class LuminaStorage
    {
        /* ------------------------------------------------- construir requests */

        /// <summary>Construye el JSON de request {"sql":…,"params":[…]} con escapes correctos.</summary>
        public static string BuildExecJson(string sql, params object[] parameters)
        {
            if (sql == null) throw new ArgumentNullException("sql");
            Dictionary<string, object> o = new Dictionary<string, object>();
            o["sql"] = sql;
            List<object> ps = new List<object>();
            if (parameters != null) ps.AddRange(parameters);
            o["params"] = ps;
            return MiniJson.Serialize(o);
        }

        /* -------------------------------------------------- leer respuestas -- */

        /// <summary>Filas de {"rows":[[…]]} como listas de celdas (vacío si no hay).</summary>
        public static List<List<object>> Rows(string execResultJson)
        {
            List<List<object>> rows = new List<List<object>>();
            if (string.IsNullOrEmpty(execResultJson)) return rows;
            Dictionary<string, object> o;
            try { o = MiniJson.Parse(execResultJson); }
            catch (FormatException) { return rows; }
            foreach (object ro in MiniJson.GetArray(o, "rows"))
            {
                List<object> src = ro as List<object>;
                if (src != null) rows.Add(src);
                else
                {
                    object[] arr = ro as object[];
                    if (arr != null) rows.Add(new List<object>(arr));
                }
            }
            return rows;
        }

        /// <summary>Primera celda de la primera fila (o null).</summary>
        public static object Scalar(string execResultJson)
        {
            List<List<object>> rows = Rows(execResultJson);
            if (rows.Count == 0 || rows[0].Count == 0) return null;
            return rows[0][0];
        }

        /// <summary>"changes" de la respuesta (INSERT/UPDATE/DELETE).</summary>
        public static long Changes(string execResultJson)
        {
            if (string.IsNullOrEmpty(execResultJson)) return 0;
            try
            {
                Dictionary<string, object> o = MiniJson.Parse(execResultJson);
                return MiniJson.GetInt(o, "changes", 0);
            }
            catch (FormatException) { return 0; }
        }

        /// <summary>"lastId" de la respuesta (rowid del INSERT).</summary>
        public static long LastId(string execResultJson)
        {
            if (string.IsNullOrEmpty(execResultJson)) return 0;
            try
            {
                Dictionary<string, object> o = MiniJson.Parse(execResultJson);
                return MiniJson.GetInt(o, "lastId", 0);
            }
            catch (FormatException) { return 0; }
        }

        /* ------------------------------------------- SQL de la aplicación ---- */

        /// <summary>INSERT de canción (tabla songs del núcleo) con 6 parámetros.</summary>
        public static string InsertSongRequest(string title, string author, string key,
                                               int bpm, string tags, string lyrics)
        {
            return BuildExecJson(
                "INSERT INTO songs(title,author,key,bpm,tags,lyrics) VALUES(?,?,?,?,?,?)",
                title ?? string.Empty,
                author ?? string.Empty,
                key ?? string.Empty,
                bpm,
                tags ?? string.Empty,
                lyrics ?? string.Empty);
        }

        /// <summary>Búsqueda FTS de canciones (tabla songs_fts) con MATCH enlazado.</summary>
        public static string SearchSongsRequest(string term, int limit)
        {
            if (limit <= 0) limit = 50;
            return BuildExecJson(
                "SELECT s.id, s.title, s.author, s.lyrics FROM songs_fts f" +
                " JOIN songs s ON s.id = f.rowid" +
                " WHERE f MATCH ?1 LIMIT ?2",
                term ?? string.Empty, limit);
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #7): TODA la biblioteca de canciones
        /// SIN buscar — ordenada por título (la lista completa se muestra al
        /// abrir la BD; el cuadro de texto queda como FILTRO opcional).
        /// </summary>
        public static string ListAllSongsRequest(int limit)
        {
            if (limit <= 0) limit = 1000;
            return BuildExecJson(
                "SELECT id, title, author, lyrics FROM songs" +
                " ORDER BY title COLLATE NOCASE LIMIT ?1",
                limit);
        }

        /// <summary>
        /// v7.1.0 «OPERADOR» (feedback #7): versiones bíblicas INSTALADAS en
        /// la BD (para los selectores de la página Biblia).
        /// </summary>
        public static string ListBibleVersionsRequest()
        {
            return BuildExecJson(
                "SELECT DISTINCT version FROM bible ORDER BY version");
        }

        /// <summary>SELECT de una canción por id (para cargarla al editor).</summary>
        public static string SelectSongByIdRequest(long id)
        {
            return BuildExecJson(
                "SELECT id, title, author, key, bpm, tags, lyrics FROM songs WHERE id = ?1", id);
        }

        /// <summary>INSERT de versículo bíblico (tabla bible; UNIQUE version/book/ch/verse).</summary>
        public static string InsertBibleVerseRequest(string version, long book, long chapter,
                                                     long verse, string text)
        {
            return BuildExecJson(
                "INSERT OR IGNORE INTO bible(version,book,chapter,verse,text) VALUES(?,?,?,?,?)",
                version ?? string.Empty, book, chapter, verse, text ?? string.Empty);
        }

        /// <summary>Versículos de una referencia ya resuelta (para vista previa en la UI).</summary>
        public static string SelectVersesRequest(string version, long book, long chapter,
                                                 long verseFrom, long verseTo)
        {
            return BuildExecJson(
                "SELECT verse, text FROM bible WHERE version=?1 AND book=?2 AND chapter=?3" +
                " AND verse>=?4 AND verse<=?5 ORDER BY verse",
                version ?? string.Empty, book, chapter, verseFrom, verseTo);
        }

        /// <summary>Transacción explícita (BEGIN/COMMIT los orquesta el llamador).</summary>
        public static string RawSqlRequest(string sql)
        {
            return BuildExecJson(sql);
        }

        /* ==================================================================
         *  v1.0.0-beta.3 — cierre de brechas F1.06/F1.07/F2.12/F2.14.
         *  Mismo contrato: SQL SIEMPRE con parámetros enlazados; el esquema
         *  vive en el núcleo (Storage.cpp) — aquí solo sentencias de app.
         * ================================================================ */

        /* ------------------------------------------------- settings F1.06 -- */

        /// <summary>Lee un valor de la tabla settings (null si no existe).</summary>
        public static string SettingsGetRequest(string key)
        {
            return BuildExecJson("SELECT value FROM settings WHERE key = ?1", key ?? string.Empty);
        }

        /// <summary>UPSERT de settings (INSERT OR REPLACE, clave primaria).</summary>
        public static string SettingsSetRequest(string key, string value)
        {
            return BuildExecJson("INSERT OR REPLACE INTO settings(key, value) VALUES(?1, ?2)",
                key ?? string.Empty, value ?? string.Empty);
        }

        /* ------------------------------------- canciones F2.12 (uso/anot) -- */

        /// <summary>Registra un uso: usage_count+1 y last_used_at=ahora (F2.12).</summary>
        public static string RecordSongUseRequest(long songId)
        {
            return BuildExecJson(
                "UPDATE songs SET usage_count = usage_count + 1, last_used_at = datetime('now')" +
                " WHERE id = ?1", songId);
        }

        /// <summary>Guarda la anotación del operador sobre una canción (F2.12).</summary>
        public static string UpdateSongAnnotationsRequest(long songId, string annotations)
        {
            return BuildExecJson("UPDATE songs SET annotations = ?2 WHERE id = ?1",
                songId, annotations ?? string.Empty);
        }

        /// <summary>
        /// Canciones más usadas (popularidad/frecuencia, F2.12): orden por
        /// usage_count DESC y last_used_at DESC como desempate.
        /// </summary>
        public static string TopSongsRequest(int limit)
        {
            if (limit <= 0) limit = 50;
            return BuildExecJson(
                "SELECT id, title, author, usage_count, COALESCE(last_used_at,''), annotations" +
                " FROM songs ORDER BY usage_count DESC, last_used_at DESC, title COLLATE NOCASE LIMIT ?1",
                limit);
        }

        /* ------------------------------------------- búsqueda caliente F1.07 */

        /// <summary>
        /// Búsqueda global de VERSÍCULOS por palabra (bible_fts MATCH) para la
        /// búsqueda en caliente durante la proyección (F1.07). Sin cargar la
        /// biblioteca completa: solo el índice FTS y un LIMIT.
        /// </summary>
        public static string SearchVersesRequest(string term, int limit)
        {
            if (limit <= 0) limit = 20;
            return BuildExecJson(
                "SELECT b.version, b.book, b.chapter, b.verse, b.text" +
                " FROM bible_fts f JOIN bible b ON b.rowid = f.rowid" +
                " WHERE f MATCH ?1 LIMIT ?2",
                term ?? string.Empty, limit);
        }

        /* ------------------------------------------- recursos F2.14 -------- */

        /// <summary>
        /// Registra un recurso (imagen/video) con ruta RELATIVA al directorio
        /// de datos (portable, F2.14). El llamador convierte a relativa.
        /// </summary>
        public static string InsertResourceRequest(string kind, string name, string relativePath,
                                                   string tags)
        {
            return BuildExecJson("INSERT INTO resources(kind, name, path, tags) VALUES(?,?,?,?)",
                (kind ?? "image"), (name ?? string.Empty), (relativePath ?? string.Empty),
                (tags ?? string.Empty));
        }

        /// <summary>Toda la biblioteca de recursos ordenada por nombre (F2.14).</summary>
        public static string ListResourcesRequest(int limit)
        {
            if (limit <= 0) limit = 1000;
            return BuildExecJson(
                "SELECT id, kind, name, path, tags FROM resources" +
                " ORDER BY name COLLATE NOCASE LIMIT ?1", limit);
        }

        /// <summary>Búsqueda FTS de recursos por nombre/etiquetas (F2.14).</summary>
        public static string SearchResourcesRequest(string term, int limit)
        {
            if (limit <= 0) limit = 100;
            return BuildExecJson(
                "SELECT r.id, r.kind, r.name, r.path, r.tags FROM resources_fts f" +
                " JOIN resources r ON r.id = f.rowid" +
                " WHERE resources_fts MATCH ?1 LIMIT ?2",
                term ?? string.Empty, limit);
        }

        /// <summary>Actualiza nombre/etiquetas de un recurso (edición F2.14).</summary>
        public static string UpdateResourceRequest(long id, string name, string tags)
        {
            return BuildExecJson("UPDATE resources SET name = ?2, tags = ?3 WHERE id = ?1",
                id, name ?? string.Empty, tags ?? string.Empty);
        }

        /// <summary>Elimina el registro (el archivo físico NO se toca).</summary>
        public static string DeleteResourceRequest(long id)
        {
            return BuildExecJson("DELETE FROM resources WHERE id = ?1", id);
        }

        /// <summary>
        /// Re-vinculación portable (F2.14): sustituye el prefijo de ruta viejo
        /// por el nuevo en TODOS los recursos cuya ruta empiece por oldRoot.
        /// Rutas RELATIVAS → una sola UPDATE parametrizada, sin rescan.
        /// </summary>
        public static string RelinkResourcesRequest(string oldRoot, string newRoot)
        {
            string oldRootNorm = (oldRoot ?? string.Empty).Replace('\\', '/');
            string newRootNorm = (newRoot ?? string.Empty).Replace('\\', '/');
            return BuildExecJson(
                "UPDATE resources SET path = ?2 || substr(path, ?3)" +
                " WHERE substr(path, 1, length(?1)) = ?1",
                oldRootNorm, newRootNorm, (oldRootNorm.Length + 1).ToString());
        }

        /* ------------------------------------------------------ utilidades -- */

        /// <summary>
        /// Extrae el primer valor escalar string de un resultado ExecJson
        /// (azúcar sobre <see cref="Scalar"/> para settings/lookups).
        /// </summary>
        public static string GetSettingValue(string execResultJson)
        {
            object v = Scalar(execResultJson);
            return v == null ? null : v.ToString();
        }
    }
}
