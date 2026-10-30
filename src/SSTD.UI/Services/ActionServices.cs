using SSTD.AI;
using SSTD.Core;

namespace SSTD.UI.Services;
public sealed class ActionServices(ITextComposer composer, INewDocumentWriter writer, IServiceProvider provider) : SSTD.Automation.IActionServices
{
    public Task<string> DescribeApprovalAsync(AgentAction action, CancellationToken ct)
    {
        var backend = (SSTD.Automation.DesktopBackend?)provider.GetService(typeof(SSTD.Automation.DesktopBackend))
            ?? throw new InvalidOperationException("Desktop backend is unavailable.");
        return backend.DescribeApprovalAsync(action, ct);
    }
    public Task<string> ComposeAsync(string instruction, CancellationToken ct) => composer.ComposeAsync(instruction, ct);
    public Task WriteNewDocumentAsync(string path, string content, CancellationToken ct) => writer.WriteAsync(path, content, ct);
}
