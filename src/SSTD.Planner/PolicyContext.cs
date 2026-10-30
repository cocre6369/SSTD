using SSTD.Core;

namespace SSTD.Planner;
/// <summary>Adapter for tests and composition; the security project remains independent of Windows and Ollama.</summary>
public sealed class PolicyContext(SstdSettings settings, WindowSnapshot? current, IReadOnlyList<ApplicationCandidate>? apps = null, Func<string, WindowSnapshot?>? windows = null) : IPolicyContext
{
    public SstdSettings Settings => settings;
    public Task<WindowSnapshot> CurrentAsync(CancellationToken ct) => Task.FromResult(current ?? new WindowSnapshot { IsBlocked = true, BlockedReason = "No window" });
    public Task<WindowSnapshot?> WindowAsync(string id, CancellationToken ct) => Task.FromResult(windows?.Invoke(id) ?? (current?.WindowId == id ? current : null));
    public Task<IReadOnlyList<ApplicationCandidate>> ApplicationsAsync(string name, CancellationToken ct) => Task.FromResult(apps ?? []);
}
