namespace SSTD.Core;

public interface IOllamaClient
{
    Task<string?> GetVersionAsync(CancellationToken ct);
    Task<IReadOnlyList<OllamaModel>> GetModelsAsync(CancellationToken ct);
    Task<IReadOnlyList<string>> GetCapabilitiesAsync(string model, CancellationToken ct);
    IAsyncEnumerable<string> StreamChatAsync(IReadOnlyList<ModelMessage> messages, ChatOptions options, string? jsonSchema, CancellationToken ct);
    IAsyncEnumerable<PullProgress> PullModelAsync(string model, CancellationToken ct);
}
public interface IApplicationResolver
{
    Task<IReadOnlyList<ApplicationCandidate>> FindAsync(string name, CancellationToken ct);
}
public interface IWindowsSnapshotProvider
{
    Task<WindowSnapshot> CaptureAsync(CancellationToken ct);
    Task<WindowSnapshot?> FindWindowAsync(string name, CancellationToken ct);
}
public interface IPolicyContext
{
    SstdSettings Settings { get; }
    Task<WindowSnapshot> CurrentAsync(CancellationToken ct);
    Task<WindowSnapshot?> WindowAsync(string id, CancellationToken ct);
    Task<IReadOnlyList<ApplicationCandidate>> ApplicationsAsync(string name, CancellationToken ct);
}
public interface ISecurityEngine
{
    Task<SecurityDecision> AuthorizeAsync(AgentAction action, CancellationToken ct);
}
public interface IApprovalBroker
{
    Task<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct);
}
public interface IActionExecutor
{
    /// <summary>Implementations MUST independently authorize every call; approval is not a bypass.</summary>
    Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken ct);
}
public interface IAuditedActionExecutor : IActionExecutor { event Action<TaskEvent>? Activity; }
public interface IDesktopBackend
{
    Task<ActionResult> ExecuteAuthorizedAsync(AgentAction action, SecurityDecision decision, CancellationToken ct);
}
public interface IAgentPlanner
{
    Task<AgentReply> NextAsync(string request, IReadOnlyList<ModelMessage> conversation, WindowSnapshot? observation,
        string? lastResult, IReadOnlyList<string> previousActions, Action<string> onToken, CancellationToken ct);
}
public interface ISettingsStore { Task<SstdSettings> LoadAsync(CancellationToken ct); Task SaveAsync(SstdSettings settings, CancellationToken ct); }
public interface ITaskStore
{
    Task CreateAsync(TaskRecord record, CancellationToken ct);
    Task EventAsync(string taskId, TaskEvent ev, CancellationToken ct);
    Task FinishAsync(string taskId, string status, CancellationToken ct);
    Task<IReadOnlyList<TaskRecord>> ListAsync(CancellationToken ct);
    Task<TaskDetail?> GetAsync(string id, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
public interface IConversationStore
{
    Task AppendAsync(ModelMessage message, CancellationToken ct);
    Task<IReadOnlyList<ModelMessage>> RecentAsync(int count, CancellationToken ct);
    Task ClearAsync(CancellationToken ct);
}
public interface IVisualObserver
{
    Task<ActionResult> InspectAsync(WindowSnapshot window, string? elementId, bool allowModelImage, CancellationToken ct);
}
public interface INewDocumentWriter { Task WriteAsync(string path, string content, CancellationToken ct); }
