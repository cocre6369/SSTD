using SSTD.Core;
using SSTD.Memory;
using Xunit;

namespace SSTD.Tests;
public class StorageTests : IAsyncLifetime
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "sstd-tests-" + Guid.NewGuid().ToString("N"));
    private LocalStore _store = null!;
    public async Task InitializeAsync() { _store = new LocalStore(_dir); await _store.LoadAsync(default); }
    public async Task DisposeAsync() { await _store.DisposeAsync(); Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools(); try { Directory.Delete(_dir, true); } catch (IOException) { } }
    [Fact] public async Task Settings_Roundtrip_And_Restart()
    {
        var s = new SstdSettings { Model = "test:7b", Temperature = 0.7, DesktopControl = true }; await _store.SaveAsync(s, default);
        await using var second = new LocalStore(_dir); var loaded = await second.LoadAsync(default); Assert.Equal("test:7b", loaded.Model); Assert.Equal(0.7, loaded.Temperature); Assert.True(loaded.DesktopControl);
    }
    [Fact] public async Task Task_History_Stores_Real_Events_And_Clears()
    {
        await _store.CreateAsync(new("t1", "Write a report", "Running", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), default);
        await _store.EventAsync("t1", new(DateTimeOffset.UtcNow,"Verify","Text verified"), default); await _store.FinishAsync("t1","Completed",default);
        var detail = await _store.GetAsync("t1", default); Assert.NotNull(detail); Assert.Equal("Completed", detail!.Task.Status); Assert.Single(detail.Events);
        await _store.ClearAsync(default); Assert.Empty(await _store.ListAsync(default));
    }
    [Fact] public async Task Disabled_History_Does_Not_Store()
    {
        _store.HistoryEnabled = false; _store.ConversationEnabled = false;
        await _store.CreateAsync(new("t1","private","Running",DateTimeOffset.UtcNow,DateTimeOffset.UtcNow),default);
        await _store.AppendAsync(new("user","private"),default); Assert.Empty(await _store.ListAsync(default)); Assert.Empty(await _store.RecentAsync(10,default));
    }
    [Fact] public async Task Secrets_Never_Reach_Conversation_Storage()
    {
        await _store.AppendAsync(new("user","password=NotForStorage123"), default); Assert.Empty(await _store.RecentAsync(10,default));
    }
    [Fact] public async Task New_Documents_Roundtrip_And_Never_Overwrite()
    {
        var writer = new NewDocumentWriter(); var path = Path.Combine(_dir,"new.txt"); await writer.WriteAsync(path,"Hello\nWorld",default);
        Assert.Equal("Hello\nWorld",await File.ReadAllTextAsync(path)); await Assert.ThrowsAsync<IOException>(() => writer.WriteAsync(path,"replacement",default)); Assert.Equal("Hello\nWorld",await File.ReadAllTextAsync(path));
    }
    [Fact] public async Task New_Docx_Is_Valid_OpenXml_Container()
    {
        var path = Path.Combine(_dir,"new.docx"); await new NewDocumentWriter().WriteAsync(path,"# Heading\n\nA paragraph with <xml> & text.",default);
        using var zip = System.IO.Compression.ZipFile.OpenRead(path); Assert.NotNull(zip.GetEntry("word/document.xml")); Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open()); var xml = await reader.ReadToEndAsync(); Assert.Contains("Heading1",xml); Assert.Contains("&amp;",xml);
    }
    [Fact] public async Task Legacy_Tasks_Table_With_Extra_Columns_Is_Repaired()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sstd-legacy-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "sstd.db") }.ToString();
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(cs))
            {
                await db.OpenAsync();
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"CREATE TABLE tasks(id TEXT PRIMARY KEY, request TEXT NOT NULL, status TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL, c6 TEXT, c7 TEXT, c8 TEXT, c9 TEXT, c10 TEXT);
INSERT INTO tasks VALUES('legacy','old request','Completed','2026-01-01T00:00:00+00:00','2026-01-02T00:00:00+00:00',NULL,NULL,NULL,NULL,NULL);
CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,time TEXT NOT NULL,phase TEXT NOT NULL,message TEXT NOT NULL,success INTEGER NOT NULL);
INSERT INTO events(task_id,time,phase,message,success) VALUES('legacy','2026-01-01T00:00:00+00:00','Verify','kept',1);";
                await cmd.ExecuteNonQueryAsync();
            }
            await using var store = new LocalStore(dir);
            await store.LoadAsync(default);
            await store.CreateAsync(new("new", "new request", "Running", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow), default);
            var list = await store.ListAsync(default);
            Assert.Contains(list, t => t.Id == "legacy");
            Assert.Contains(list, t => t.Id == "new");
            var detail = await store.GetAsync("legacy", default);
            Assert.NotNull(detail);
            Assert.Equal("old request", detail!.Task.Request);
            Assert.Single(detail.Events);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
    [Fact] public async Task Legacy_Messages_With_ConversationId_NotNull_Are_Repaired()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sstd-legacy-msg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "sstd.db") }.ToString();
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(cs))
            {
                await db.OpenAsync();
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"CREATE TABLE messages(id INTEGER PRIMARY KEY AUTOINCREMENT, conversation_id TEXT NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, created_at TEXT);
INSERT INTO messages(conversation_id,role,content) VALUES('c1','user','old question');
INSERT INTO messages(conversation_id,role,content) VALUES('c1','assistant','old answer');
INSERT INTO messages(conversation_id,role,content) VALUES('c1','system','drop me');";
                await cmd.ExecuteNonQueryAsync();
            }
            await using var store = new LocalStore(dir);
            await store.LoadAsync(default);
            await store.AppendAsync(new("user", "new question"), default);
            var recent = await store.RecentAsync(10, default);
            Assert.Contains(recent, m => m.Role == "user" && m.Content == "old question");
            Assert.Contains(recent, m => m.Role == "assistant" && m.Content == "old answer");
            Assert.Contains(recent, m => m.Role == "user" && m.Content == "new question");
            Assert.DoesNotContain(recent, m => m.Content == "drop me");
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(cs))
            {
                await db.OpenAsync();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(messages);";
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
                Assert.Equal(new[] { "id", "role", "content" }, columns);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
    [Fact] public async Task Legacy_Settings_And_Events_With_Extra_Columns_Are_Repaired()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sstd-legacy-set-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var cs = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = Path.Combine(dir, "sstd.db") }.ToString();
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(cs))
            {
                await db.OpenAsync();
                using var cmd = db.CreateCommand();
                cmd.CommandText = @"CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL, extra TEXT NOT NULL DEFAULT 'x');
INSERT INTO settings(key,value) VALUES('settings','{""Model"":""legacy:7b""}');
CREATE TABLE tasks(id TEXT PRIMARY KEY, request TEXT NOT NULL, status TEXT NOT NULL, created TEXT NOT NULL, updated TEXT NOT NULL);
INSERT INTO tasks VALUES('legacy','old request','Completed','2026-01-01T00:00:00+00:00','2026-01-02T00:00:00+00:00');
CREATE TABLE events(id INTEGER PRIMARY KEY AUTOINCREMENT,task_id TEXT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,time TEXT NOT NULL,phase TEXT NOT NULL,message TEXT NOT NULL,success INTEGER NOT NULL,extra TEXT NOT NULL DEFAULT 'x');
INSERT INTO events(task_id,time,phase,message,success) VALUES('legacy','2026-01-01T00:00:00+00:00','Verify','kept',1);";
                await cmd.ExecuteNonQueryAsync();
            }
            await using var store = new LocalStore(dir);
            var loaded = await store.LoadAsync(default);
            Assert.Equal("legacy:7b", loaded.Model);
            await store.SaveAsync(new SstdSettings { Model = "new:7b" }, default);
            Assert.Equal("new:7b", (await store.LoadAsync(default)).Model);
            await store.EventAsync("legacy", new(DateTimeOffset.UtcNow, "Verify", "new event"), default);
            var detail = await store.GetAsync("legacy", default);
            Assert.NotNull(detail);
            Assert.Equal(2, detail!.Events.Count);
            using (var db = new Microsoft.Data.Sqlite.SqliteConnection(cs))
            {
                await db.OpenAsync();
                using var cmd = db.CreateCommand();
                cmd.CommandText = "PRAGMA table_info(events);";
                using var reader = await cmd.ExecuteReaderAsync();
                var columns = new List<string>();
                while (await reader.ReadAsync()) columns.Add(reader.GetString(1));
                Assert.Equal(new[] { "id", "task_id", "time", "phase", "message", "success" }, columns);
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }
    }
}
