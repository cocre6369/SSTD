using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using SSTD.Core;

namespace SSTD.Memory;
public sealed class LocalStore : ISettingsStore, ITaskStore, IConversationStore, IAsyncDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;
    public string DataDirectory { get; }
    public bool HistoryEnabled { get; set; } = true;
    public bool ConversationEnabled { get; set; } = true;
    public LocalStore(string? directory = null)
    {
        DataDirectory = directory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSTD");
        Directory.CreateDirectory(DataDirectory);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = Path.Combine(DataDirectory, "sstd.db"), Mode = SqliteOpenMode.ReadWriteCreate, ForeignKeys = true }.ToString();
    }
    private static readonly string[] TaskColumns = ["id", "request", "status", "created", "updated"];
    private const string CreateTasksSql = "CREATE TABLE IF NOT EXISTS tasks(id TEXT PRIMARY KEY NOT NULL,request TEXT NOT NULL,status TEXT NOT NULL,created TEXT NOT NULL,updated TEXT NOT NULL);";
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var db = new SqliteConnection(_connectionString); await db.OpenAsync(ct).ConfigureAwait(false);
        if (!_initialized)
        {
            await EnsureSchemaAsync(db, ct).ConfigureAwait(false);
            _initialized = true;
        }
        return db;
    }
    private static async Task EnsureSchemaAsync(SqliteConnection db, CancellationToken ct)
    {
        await ExecAsync(db, "PRAGMA journal_mode=WAL;", ct).ConfigureAwait(false);
        await ExecAsync(db, "CREATE TABLE IF NOT EXISTS settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);", ct).ConfigureAwait(false);
        await ExecAsync(db, "CREATE TABLE IF NOT EXISTS messages(id INTEGER PRIMARY KEY AUTOINCREMENT,role TEXT NOT NULL,content TEXT NOT NULL);", ct).ConfigureAwait(false);
        await MigrateTasksAsync(db, ct).ConfigureAwait(false);
        await ExecAsync(db, "PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);
        await ExecAsync(db, "CREATE TABLE IF NOT EXISTS events(id INTEGER PRIMARY KEY AUTOINCREMENT,task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,time TEXT NOT NULL,phase TEXT NOT NULL,message TEXT NOT NULL,success INTEGER NOT NULL); CREATE INDEX IF NOT EXISTS events_task ON events(task_id,id);", ct).ConfigureAwait(false);
    }
    private static async Task MigrateTasksAsync(SqliteConnection db, CancellationToken ct)
    {
        var columns = await GetColumnsAsync(db, "tasks", ct).ConfigureAwait(false);
        if (columns.Count == 0)
        {
            await ExecAsync(db, CreateTasksSql, ct).ConfigureAwait(false);
            return;
        }
        // Older builds can leave a wider or differently ordered tasks table. Normalize it instead of
        // relying on INSERT INTO tasks VALUES(...), which fails when the column count differs.
        if (columns.Count == TaskColumns.Length && TaskColumns.All(columns.Contains)) return;

        var now = DateTimeOffset.UtcNow.ToString("O");
        string DefaultFor(string column) => column switch
        {
            "id" => "lower(hex(randomblob(16)))",
            "request" => Literal(""),
            "status" => Literal("Unknown"),
            _ => Literal(now)
        };
        string Expression(string column) => columns.Contains(column)
            ? $"COALESCE(NULLIF(CAST({Quote(column)} AS TEXT), ''), {DefaultFor(column)})"
            : DefaultFor(column);

        await ExecAsync(db, "PRAGMA foreign_keys=OFF;", ct).ConfigureAwait(false);
        try
        {
            using var tx = db.BeginTransaction();
            await ExecAsync(db, "DROP TABLE IF EXISTS tasks_migrated;", ct, tx).ConfigureAwait(false);
            await ExecAsync(db, "CREATE TABLE tasks_migrated(id TEXT PRIMARY KEY NOT NULL,request TEXT NOT NULL,status TEXT NOT NULL,created TEXT NOT NULL,updated TEXT NOT NULL);", ct, tx).ConfigureAwait(false);
            await ExecAsync(db, $"INSERT OR IGNORE INTO tasks_migrated(id,request,status,created,updated) SELECT {string.Join(", ", TaskColumns.Select(Expression))} FROM tasks;", ct, tx).ConfigureAwait(false);
            await ExecAsync(db, "DROP TABLE tasks;", ct, tx).ConfigureAwait(false);
            await ExecAsync(db, "ALTER TABLE tasks_migrated RENAME TO tasks;", ct, tx).ConfigureAwait(false);
            tx.Commit();
        }
        finally
        {
            await ExecAsync(db, "PRAGMA foreign_keys=ON;", ct).ConfigureAwait(false);
        }
    }
    private static async Task<HashSet<string>> GetColumnsAsync(SqliteConnection db, string table, CancellationToken ct)
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
        using var reader = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(ct).ConfigureAwait(false)) columns.Add(reader.GetString(1));
        return columns;
    }
    private static async Task ExecAsync(SqliteConnection db, string sql, CancellationToken ct, SqliteTransaction? tx = null)
    {
        using var cmd = db.CreateCommand(); cmd.Transaction = tx; cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
    }
    private static string Quote(string identifier) => "\"" + identifier.Replace("\"", "\"\"") + "\"";
    private static string Literal(string value) => "'" + value.Replace("'", "''") + "'";
    private async Task<T> WithDb<T>(Func<SqliteConnection, Task<T>> work, CancellationToken ct)
    {
        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try { await using var db = await OpenAsync(ct).ConfigureAwait(false); return await work(db).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    public Task<SstdSettings> LoadAsync(CancellationToken ct) => WithDb(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT value FROM settings WHERE key='settings'";
        var value = await cmd.ExecuteScalarAsync(ct).ConfigureAwait(false) as string;
        try { var s = value is null ? new SstdSettings() : JsonSerializer.Deserialize<SstdSettings>(value) ?? new SstdSettings(); return s.Validate().Length == 0 ? s : new SstdSettings(); }
        catch (JsonException) { return new SstdSettings(); }
    }, ct);
    public Task SaveAsync(SstdSettings settings, CancellationToken ct)
    {
        var error = settings.Validate(); if (error.Length > 0) throw new ArgumentException(error);
        return WithDb(async db => { using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO settings(key,value) VALUES('settings',$v) ON CONFLICT(key) DO UPDATE SET value=excluded.value"; cmd.Parameters.AddWithValue("$v", JsonSerializer.Serialize(settings)); await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true; }, ct);
    }
    public Task CreateAsync(TaskRecord r, CancellationToken ct) => !HistoryEnabled ? Task.CompletedTask : WithDb(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO tasks(id,request,status,created,updated) VALUES($id,$request,$status,$created,$updated)";
        cmd.Parameters.AddWithValue("$id", r.Id); cmd.Parameters.AddWithValue("$request", SensitiveData.Redact(r.Request)); cmd.Parameters.AddWithValue("$status", r.Status);
        cmd.Parameters.AddWithValue("$created", r.Created.ToString("O")); cmd.Parameters.AddWithValue("$updated", r.Updated.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true;
    }, ct);
    public Task EventAsync(string id, TaskEvent ev, CancellationToken ct) => !HistoryEnabled ? Task.CompletedTask : WithDb(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO events(task_id,time,phase,message,success) VALUES($id,$time,$phase,$message,$ok)";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$time", ev.Time.ToString("O")); cmd.Parameters.AddWithValue("$phase", ev.Phase);
        // Do not persist UI text, generated drafts, clipboard content, or full action arguments.
        cmd.Parameters.AddWithValue("$message", SensitiveData.Redact(Short(ev.Message))); cmd.Parameters.AddWithValue("$ok", ev.Success);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true;
    }, ct);
    public Task FinishAsync(string id, string status, CancellationToken ct) => !HistoryEnabled ? Task.CompletedTask : WithDb(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "UPDATE tasks SET status=$status,updated=$updated WHERE id=$id";
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$status", status); cmd.Parameters.AddWithValue("$updated", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true;
    }, ct);
    public Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken ct) => WithDb<IReadOnlyList<TaskRecord>>(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,request,status,created,updated FROM tasks ORDER BY created DESC LIMIT 200"; using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false);
        var list = new List<TaskRecord>(); while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new(r.GetString(0), r.GetString(1), r.GetString(2), DateTimeOffset.Parse(r.GetString(3)), DateTimeOffset.Parse(r.GetString(4)))); return list;
    }, ct);
    public Task<TaskDetail?> GetAsync(string id, CancellationToken ct) => WithDb<TaskDetail?>(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT id,request,status,created,updated FROM tasks WHERE id=$id"; cmd.Parameters.AddWithValue("$id", id);
        TaskRecord? task = null; using (var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false)) if (await r.ReadAsync(ct).ConfigureAwait(false)) task = new(r.GetString(0), r.GetString(1), r.GetString(2), DateTimeOffset.Parse(r.GetString(3)), DateTimeOffset.Parse(r.GetString(4)));
        if (task is null) return null;
        using var events = db.CreateCommand(); events.CommandText = "SELECT time,phase,message,success FROM events WHERE task_id=$id ORDER BY id"; events.Parameters.AddWithValue("$id", id);
        using var er = await events.ExecuteReaderAsync(ct).ConfigureAwait(false); var list = new List<TaskEvent>();
        while (await er.ReadAsync(ct).ConfigureAwait(false)) list.Add(new(DateTimeOffset.Parse(er.GetString(0)), er.GetString(1), er.GetString(2), er.GetBoolean(3)));
        return new(task, list);
    }, ct);
    public Task ClearAsync(CancellationToken ct) => WithDb(async db => { using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM events; DELETE FROM tasks;"; await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true; }, ct);
    public Task AppendAsync(ModelMessage m, CancellationToken ct) => !ConversationEnabled || m.Role is not ("user" or "assistant") || SensitiveData.ContainsSecret(m.Content) ? Task.CompletedTask : WithDb(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "INSERT INTO messages(role,content) VALUES($role,$content); DELETE FROM messages WHERE id NOT IN (SELECT id FROM messages ORDER BY id DESC LIMIT 100)";
        cmd.Parameters.AddWithValue("$role", m.Role); cmd.Parameters.AddWithValue("$content", SensitiveData.Redact(m.Content)[..Math.Min(16000, SensitiveData.Redact(m.Content).Length)]);
        await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true;
    }, ct);
    public Task<IReadOnlyList<ModelMessage>> RecentAsync(int count, CancellationToken ct) => WithDb<IReadOnlyList<ModelMessage>>(async db =>
    {
        using var cmd = db.CreateCommand(); cmd.CommandText = "SELECT role,content FROM (SELECT id,role,content FROM messages ORDER BY id DESC LIMIT $count) ORDER BY id"; cmd.Parameters.AddWithValue("$count", Math.Clamp(count, 1, 100));
        using var r = await cmd.ExecuteReaderAsync(ct).ConfigureAwait(false); var list = new List<ModelMessage>();
        while (await r.ReadAsync(ct).ConfigureAwait(false)) list.Add(new(r.GetString(0), r.GetString(1))); return list;
    }, ct);
    public Task ClearConversationsAsync(CancellationToken ct) => WithDb(async db => { using var cmd = db.CreateCommand(); cmd.CommandText = "DELETE FROM messages"; await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true; }, ct);
    Task IConversationStore.ClearAsync(CancellationToken ct) => ClearConversationsAsync(ct);
    public async Task ClearAllAsync(CancellationToken ct) { await ClearAsync(ct).ConfigureAwait(false); await ClearConversationsAsync(ct).ConfigureAwait(false); await WithDb(async db => { using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE); VACUUM;"; await cmd.ExecuteNonQueryAsync(ct).ConfigureAwait(false); return true; }, ct).ConfigureAwait(false); }
    private static string Short(string s) { s = s.Split('\n')[0]; return s.Length <= 400 ? s : s[..400] + "…"; }
    public async ValueTask DisposeAsync() { await _gate.WaitAsync().ConfigureAwait(false); try { SqliteConnection.ClearAllPools(); } finally { _gate.Release(); _gate.Dispose(); } }
}
