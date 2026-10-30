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
}
