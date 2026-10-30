using SSTD.AI;
using SSTD.Core;

namespace SSTD.UI.Services;
public sealed class ActionServices(ITextComposer composer, INewDocumentWriter writer, SSTD.Automation.DesktopBackend backend) : SSTD.Automation.IActionServices
{
    public Task<string> DescribeApprovalAsync(AgentAction action, CancellationToken ct) => backend.DescribeApprovalAsync(action, ct);
    public Task<string> ComposeAsync(string instruction, CancellationToken ct) => composer.ComposeAsync(instruction, ct);
    public Task WriteNewDocumentAsync(string path, string content, CancellationToken ct) => writer.WriteAsync(path, content, ct);
}
