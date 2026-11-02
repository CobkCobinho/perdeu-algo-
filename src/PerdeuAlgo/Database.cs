using Microsoft.Data.Sqlite;

namespace PerdeuAlgo;

public sealed class Database : IDisposable
{
    private readonly object gate = new();
    private readonly SqliteConnection connection;
    public Database(string connectionString)
    {
        connection = new(connectionString);
        connection.Open();
        Execute("PRAGMA foreign_keys = ON;");
        Execute("""
            CREATE TABLE IF NOT EXISTS users (
              id INTEGER PRIMARY KEY, name TEXT NOT NULL, email TEXT NOT NULL UNIQUE COLLATE NOCASE, password_hash TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS items (
              id INTEGER PRIMARY KEY, owner_id INTEGER NOT NULL REFERENCES users(id), kind TEXT NOT NULL CHECK(kind IN ('lost','found')),
              title TEXT NOT NULL, category TEXT NOT NULL, location TEXT NOT NULL, event_date TEXT NOT NULL,
              description TEXT NOT NULL, secret TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'open' CHECK(status IN ('open','reserved','returned','cancelled')));
            CREATE TABLE IF NOT EXISTS claims (
              id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL REFERENCES items(id), claimant_id INTEGER NOT NULL REFERENCES users(id),
              proof TEXT NOT NULL, status TEXT NOT NULL DEFAULT 'pending' CHECK(status IN ('pending','approved','rejected','delivered')),
              created_at TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS one_active_claim ON claims(item_id, claimant_id) WHERE status IN ('pending','approved');
            CREATE UNIQUE INDEX IF NOT EXISTS one_approved_claim ON claims(item_id) WHERE status IN ('approved','delivered');
            CREATE INDEX IF NOT EXISTS item_search ON items(status, kind, category);
            """);
    }
    public T Run<T>(Func<T> action) { lock (gate) return action(); }
    public T Transaction<T>(Func<T> action)
    {
        lock (gate)
        {
            using var tx = connection.BeginTransaction();
            transaction = tx;
            try { var result = action(); tx.Commit(); return result; }
            finally { transaction = null; }
        }
    }
    private SqliteTransaction? transaction;
    private SqliteCommand Command(string sql, object?[] values)
    {
        var cmd = connection.CreateCommand(); cmd.CommandText = sql; cmd.Transaction = transaction;
        for (var i = 0; i < values.Length; i++) cmd.Parameters.AddWithValue($"@p{i}", values[i] ?? DBNull.Value);
        return cmd;
    }
    public int Execute(string sql, params object?[] values) { using var cmd = Command(sql, values); return cmd.ExecuteNonQuery(); }
    public long Insert(string sql, params object?[] values) { Execute(sql, values); using var cmd = Command("SELECT last_insert_rowid()", []); return (long)cmd.ExecuteScalar()!; }
    public List<T> Query<T>(string sql, Func<SqliteDataReader, T> map, params object?[] values)
    {
        using var cmd = Command(sql, values); using var reader = cmd.ExecuteReader(); var rows = new List<T>();
        while (reader.Read()) rows.Add(map(reader)); return rows;
    }
    public void Dispose() => connection.Dispose();
}
