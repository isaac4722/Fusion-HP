// ============================================================================
//  Fusion-HP · FusionShared/Bible/SqliteAdoReader.cs — vía ADO.NET (v4.1.0)
//  Motor: System.Data.SQLite.Core 1.0.118 (PD/MIT — [DEPENDENCIAS.md]).
//  Lee módulos e-Sword/.bib y fdb con SQL real (B-trees del motor nativo).
//  · Cadena Read Only + FailIfMissing: nunca escribe ni crea archivos.
//  · Misma superficie que el lector puro (HasTable/ReadTable/SqliteRow) para
//    que SQLiteFileReader pueda delegar sin cambiar a BibleStore.
//  · Si el interop nativo (SQLite.Interop.dll) no está disponible, el ctor
//    lanza y SQLiteFileReader cae al lector puro de B-Tree (perfil B).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Data.SQLite;

namespace Fusion.Shared.Bible
{
    public sealed class SqliteAdoReader : IDisposable
    {
        readonly SQLiteConnection conn;

        public SqliteAdoReader(string path)
        {
            var cs = new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                ReadOnly = true,
                FailIfMissing = true,
                Version = 3
            };
            conn = new SQLiteConnection(cs.ConnectionString);
            conn.Open();
        }

        public bool HasTable(string name)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND lower(name)=$n;";
                cmd.Parameters.AddWithValue("$n", name == null ? "" : name.ToLowerInvariant());
                return Convert.ToInt64(cmd.ExecuteScalar()) > 0;
            }
        }

        public List<SqliteRow> ReadTable(string name)
        {
            // el nombre proviene del esquema interno del lector — validar duro
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("tabla vacía");
            foreach (char c in name)
                if (!char.IsLetterOrDigit(c) && c != '_')
                    throw new ArgumentException("nombre de tabla inválido: " + name);

            var rows = new List<SqliteRow>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT rowid, * FROM \"" + name + "\";";
                using (var r = cmd.ExecuteReader())
                {
                    while (r.Read())
                    {
                        var row = new SqliteRow { RowId = r.GetInt64(0) };
                        for (int i = 1; i < r.FieldCount; i++)
                        {
                            object v = r.GetValue(i);
                            if (v == DBNull.Value || v == null) row.Values.Add(null);
                            else if (v is byte[]) row.Values.Add(v);
                            else if (v is long || v is double || v is string) row.Values.Add(v);
                            else row.Values.Add(Convert.ToString(v));
                        }
                        rows.Add(row);
                    }
                }
            }
            return rows;
        }

        public void Dispose()
        {
            if (conn != null) conn.Dispose();
        }
    }
}
