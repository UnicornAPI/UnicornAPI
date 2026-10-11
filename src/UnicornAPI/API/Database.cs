using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
using Unicorn.Internal;

namespace Unicorn.API
{
    /// <summary>
    /// Общая база SQLite: UnicornAPI/unicorn.db. Её используют сам UnicornAPI (профили игроков) и плагины
    /// (свои таблицы — с префиксом имени плагина, например levels_players). Вызывать с главного потока.
    /// </summary>
    public static class Database
    {
        private static SqliteConnection _conn;

        public static string FilePath => Path.Combine(Paths.Root, "unicorn.db");
        public static bool IsAvailable => SqliteBootstrap.Ready;

        /// <summary>Открытое соединение (одно на процесс).</summary>
        public static SqliteConnection Connection
        {
            get
            {
                if (_conn != null) return _conn;
                if (!SqliteBootstrap.Ready) throw new InvalidOperationException("SQLite не инициализирован (см. лог [SQLite])");
                _conn = new SqliteConnection($"Data Source={FilePath}");
                _conn.Open();
                using (var c = _conn.CreateCommand())
                {
                    c.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=3000;";
                    c.ExecuteNonQuery();
                }
                return _conn;
            }
        }

        private static SqliteCommand Cmd(string sql, object[] args)
        {
            var c = Connection.CreateCommand();
            c.CommandText = sql;
            // параметры: $1, $2 ... по порядку
            for (int i = 0; i < args.Length; i++) c.Parameters.AddWithValue("$" + (i + 1), args[i] ?? DBNull.Value);
            return c;
        }

        /// <summary>INSERT/UPDATE/DELETE/CREATE. Параметры в SQL — $1, $2, ...</summary>
        public static int Execute(string sql, params object[] args) { using var c = Cmd(sql, args); return c.ExecuteNonQuery(); }

        /// <summary>Одно значение (первая колонка первой строки) или null.</summary>
        public static object Scalar(string sql, params object[] args)
        {
            using var c = Cmd(sql, args);
            var v = c.ExecuteScalar();
            return v is DBNull ? null : v;
        }

        /// <summary>Строки как словари «колонка → значение» (DBNull → null).</summary>
        public static List<Dictionary<string, object>> Query(string sql, params object[] args)
        {
            var res = new List<Dictionary<string, object>>();
            using var c = Cmd(sql, args);
            using var r = c.ExecuteReader();
            while (r.Read())
            {
                var row = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i);
                res.Add(row);
            }
            return res;
        }

        internal static void Close() { try { _conn?.Close(); _conn?.Dispose(); } catch { } _conn = null; }
    }
}
