using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using SSTD.Core;

namespace SSTD.AI;

public sealed class OllamaClient : IOllamaClient, IDisposable
{
    private readonly HttpClient _http;
    private Uri _endpoint;
    public OllamaClient(Uri? endpoint = null, HttpMessageHandler? handler = null)
    {
        _endpoint = Validate(endpoint ?? new Uri("http://localhost:11434"));
        _http = handler is null ? new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false, UseProxy = false, PooledConnectionLifetime = TimeSpan.FromMinutes(2) }) : new HttpClient(handler, false);
        _http.Timeout = Timeout.InfiniteTimeSpan; // Each request has its own cancellation / inactivity timeout.
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.MaxResponseContentBufferSize = 16 * 1024 * 1024;
    }
    public void Configure(string endpoint)
    {
        var uri = Validate(new Uri(endpoint));
        _endpoint = uri;
    }
    public static Uri Validate(Uri uri)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme is not ("http" or "https") || !uri.IsLoopback || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) || uri.AbsolutePath != "/")
            throw new ArgumentException("Ollama must use a loopback HTTP(S) endpoint, without credentials, redirects, paths, or queries.");
        return new Uri(uri.GetLeftPart(UriPartial.Authority));
    }
    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, new Uri(_endpoint, path));
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        return request;
    }
    private static async Task EnsureSuccess(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = TryJson(body);
        var error = doc?.RootElement.TryGetProperty("error", out var e) == true ? e.GetString() : null;
        throw new OllamaException($"{(int)response.StatusCode} {response.ReasonPhrase}: {SensitiveData.Redact(error ?? "Ollama request failed.")}");
    }
    private static JsonDocument? TryJson(string s) { try { return JsonDocument.Parse(s); } catch (JsonException) { return null; } }
    public async Task<string?> GetVersionAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(4));
        using var request = Request(HttpMethod.Get, "/api/version"); using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        await EnsureSuccess(response, timeout.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : "connected";
    }
    public async Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var request = Request(HttpMethod.Get, "/api/tags"); using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        await EnsureSuccess(response, timeout.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        var models = new List<OllamaModel>();
        if (!doc.RootElement.TryGetProperty("models", out var array) || array.ValueKind != JsonValueKind.Array) return models;
        foreach (var m in array.EnumerateArray())
        {
            var name = m.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrEmpty(name)) continue;
            models.Add(new(name, m.TryGetProperty("size", out var size) && size.TryGetInt64(out var bytes) ? bytes : 0,
                m.TryGetProperty("details", out var d) && d.TryGetProperty("family", out var f) ? f.GetString() : null));
        }
        return models.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public async Task<IReadOnlyList<string>> GetCapabilitiesAsync(string model, CancellationToken ct)
    {
        ValidateModel(model); using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(10));
        using var request = Request(HttpMethod.Post, "/api/show", new { model }); using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
        await EnsureSuccess(response, timeout.Token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false));
        return doc.RootElement.TryGetProperty("capabilities", out var c) && c.ValueKind == JsonValueKind.Array ? c.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToList() : [];
    }
    public async IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ModelMessage> messages, ChatOptions options, string? jsonSchema, [EnumeratorCancellation] CancellationToken ct)
    {
        ValidateModel(options.Model);
        var payload = new Dictionary<string, object?>
        {
            ["model"] = options.Model, ["stream"] = true,
            ["messages"] = messages.Select(m => m.ImageBase64 is null ? new { role = m.Role, content = m.Content } : (object)new { role = m.Role, content = m.Content, images = new[] { m.ImageBase64 } }),
            ["options"] = new { temperature = options.Temperature, num_ctx = options.ContextSize, num_predict = options.MaxTokens },
            ["keep_alive"] = "10m"
        };
        if (jsonSchema is not null) payload["format"] = JsonSerializer.Deserialize<JsonElement>(jsonSchema);
        using var inactivity = CancellationTokenSource.CreateLinkedTokenSource(ct); inactivity.CancelAfter(TimeSpan.FromMinutes(4));
        using var request = Request(HttpMethod.Post, "/api/chat", payload);
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, inactivity.Token).ConfigureAwait(false);
        await EnsureSuccess(response, inactivity.Token).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(inactivity.Token).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var count = 0;
        while (true)
        {
            inactivity.CancelAfter(TimeSpan.FromMinutes(4));
            string? line;
            try { line = await reader.ReadLineAsync(inactivity.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new OllamaException("Ollama stopped streaming. The model may still be loading; retry or choose a smaller model."); }
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = TryJson(line) ?? throw new OllamaException("Ollama returned malformed stream data.");
            if (doc.RootElement.TryGetProperty("error", out var error)) throw new OllamaException(SensitiveData.Redact(error.GetString()));
            if (doc.RootElement.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var token = content.GetString() ?? ""; count += token.Length;
                if (count > 512 * 1024) throw new OllamaException("Model output exceeded the safety size limit.");
                if (token.Length > 0) yield return token;
            }
            if (doc.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True && done.GetBoolean()) yield break;
        }
    }
    public async IAsyncEnumerable<PullProgress> PullModelAsync(string model, [EnumeratorCancellation] CancellationToken ct)
    {
        ValidateModel(model);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(3));
        using var request = Request(HttpMethod.Post, "/api/pull", new { model, stream = true });
        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
        await EnsureSuccess(response, timeout.Token).ConfigureAwait(false);
        using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false); using var reader = new StreamReader(stream);
        while (true)
        {
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var line = await reader.ReadLineAsync(timeout.Token).ConfigureAwait(false); if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;
            using var doc = TryJson(line) ?? throw new OllamaException("Malformed model-download progress.");
            if (doc.RootElement.TryGetProperty("error", out var e)) throw new OllamaException(SensitiveData.Redact(e.GetString()));
            var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() ?? "Downloading" : "Downloading";
            long Number(string name) => doc.RootElement.TryGetProperty(name, out var v) && v.TryGetInt64(out var n) ? n : 0;
            yield return new(SensitiveData.Redact(status), Number("completed"), Number("total"));
            if (status == "success") yield break;
        }
    }
    public static void ValidateModel(string model)
    {
        if (string.IsNullOrWhiteSpace(model) || model.Length > 200 || !System.Text.RegularExpressions.Regex.IsMatch(model, @"^[a-zA-Z0-9][a-zA-Z0-9._:/-]*$") || model.Contains("..", StringComparison.Ordinal)) throw new ArgumentException("Choose an installed Ollama model with a valid name.");
    }
    public void Dispose() => _http.Dispose();
}
public sealed class OllamaException(string message) : Exception(message);

/// <summary>Capability metadata from Ollama /api/show; never inferred from a model name.</summary>
public sealed class ModelCapabilities { public bool Vision { get; set; } }
