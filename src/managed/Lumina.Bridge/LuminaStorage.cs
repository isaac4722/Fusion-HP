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
    }
}
