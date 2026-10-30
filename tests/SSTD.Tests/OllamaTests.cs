using System.Net;
using System.Text;
using System.Text.Json;
using SSTD.AI;
using SSTD.Core;
using Xunit;

namespace SSTD.Tests;
public class OllamaTests
{
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => Task.FromResult(respond(request)); }
    private static HttpResponseMessage Text(string s, HttpStatusCode code = HttpStatusCode.OK) => new(code) { Content = new StringContent(s, Encoding.UTF8, "application/json") };
    [Fact] public async Task Detects_Version_And_Installed_Models()
    {
        using var client = new OllamaClient(handler: new Handler(r => Text(r.RequestUri!.AbsolutePath == "/api/version" ? "{\"version\":\"0.5.7\"}" : "{\"models\":[{\"name\":\"qwen2.5:7b\",\"size\":4700000000,\"details\":{\"family\":\"qwen2\"}}]}")));
        Assert.Equal("0.5.7", await client.GetVersionAsync(default)); var models = await client.GetModelsAsync(default); Assert.Single(models); Assert.Equal("qwen2.5:7b", models[0].Name);
    }
    [Fact] public async Task Streams_Ndjson_And_Stops_At_Done()
    {
        using var client = new OllamaClient(handler: new Handler(_ => Text("{\"message\":{\"content\":\"Hel\"},\"done\":false}\n{\"message\":{\"content\":\"lo\"},\"done\":false}\n{\"done\":true}\n{\"message\":{\"content\":\"ignored\"}}")));
        var tokens = new List<string>(); await foreach (var t in client.StreamChatAsync([new("user","Hi")], new("test",0.2,2048,128), null, default)) tokens.Add(t);
        Assert.Equal("Hello", string.Concat(tokens));
    }
    [Fact] public async Task Surfaces_Ollama_Model_Error()
    {
        using var client = new OllamaClient(handler: new Handler(_ => Text("{\"error\":\"model not found\"}", HttpStatusCode.NotFound)));
        var ex = await Assert.ThrowsAsync<OllamaException>(async () => { await foreach (var _ in client.StreamChatAsync([], new("missing",0,2048,128), null, default)) { } }); Assert.Contains("model not found", ex.Message);
    }
    [Fact] public async Task Cancellation_Stops_Reading()
    {
        using var client = new OllamaClient(handler: new Handler(_ => Text("{\"done\":false}\n"))); using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await foreach (var _ in client.StreamChatAsync([], new("test",0,2048,128), null, cts.Token)) { } });
    }
    [Fact] public async Task Model_Pull_Reports_Actual_Progress()
    {
        using var client = new OllamaClient(handler: new Handler(_ => Text("{\"status\":\"pulling\",\"completed\":50,\"total\":100}\n{\"status\":\"success\"}\n")));
        var progress = new List<PullProgress>(); await foreach (var p in client.PullModelAsync("test", default)) progress.Add(p);
        Assert.Equal(50, progress[0].Completed); Assert.Equal("success", progress[^1].Status);
    }
    [Fact] public async Task Detects_Vision_Capability()
    {
        using var client = new OllamaClient(handler: new Handler(_ => Text("{\"capabilities\":[\"completion\",\"vision\"]}")));
        Assert.Contains("vision", await client.GetCapabilitiesAsync("vision-model", default));
    }
    [Theory]
    [InlineData("../bad")] [InlineData("model\nother")] [InlineData("https://evil.com/model")] [InlineData("")]
    public void Rejects_Invalid_Model_Names(string name) => Assert.Throws<ArgumentException>(() => OllamaClient.ValidateModel(name));
    [Fact] public void Reply_Extractor_Handles_Streaming_Escapes()
    {
        Assert.Equal("Hel", OllamaPlanner.ExtractReply("{\"reply\":\"Hel"));
        Assert.Equal("A \"quote\"\nnext", OllamaPlanner.ExtractReply("{\"reply\":\"A \\\"quote\\\"\\nnext\",\"action\":null}"));
    }
    [Fact] public async Task Planner_Returns_One_Validated_Action()
    {
        var json = "{\"reply\":\"Opening the app.\",\"action\":{\"name\":\"OpenApplication\",\"arguments\":{\"name\":\"Notepad\"}},\"completed\":false}";
        using var client = new OllamaClient(handler: new Handler(_ => Text(JsonSerializer.Serialize(new { message = new { content = json }, done = true }))));
        var planner = new OllamaPlanner(client, () => new SstdSettings { Model = "test" }, () => false); var tokens = "";
        var reply = await planner.NextAsync("Open Notepad", [], null, null, [], t => tokens += t, default);
        Assert.Equal("OpenApplication", reply.Action!.Name); Assert.False(reply.Completed); Assert.Equal("Opening the app.", tokens);
    }
}
