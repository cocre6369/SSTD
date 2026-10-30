using System.Security.Cryptography;
using System.Text;
using SSTD.Core;
using SSTD.Security;

namespace SSTD.Automation;

public sealed class ActionExecutor(ISecurityEngine security, IApprovalBroker approvals, IDesktopBackend backend, IActionServices services) : IActionExecutor
{
    public async Task<ActionResult> ExecuteAsync(AgentAction action, CancellationToken ct)
    {
        if (!ActionContract.Names.Contains(action.Name)) return ActionResult.Fail("Unregistered action.");
        var policy = await security.AuthorizeAsync(action, ct).ConfigureAwait(false);
        if (!policy.Allowed) return ActionResult.Fail("Safety policy: " + policy.Reason);
        if (policy.RequiresApproval)
        {
            var details = Describe(action);
            if (action.Name is "ClickElement" or "PressKey") details += "\n\n" + await services.DescribeApprovalAsync(action, ct).ConfigureAwait(false);
            if (action.Name == "CreateDocument") details = "Create NEW file (never overwrite):\n" + action.Str("path") + "\n\nContent:\n" + action.Str("content");
            var request = new ApprovalRequest(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(action.Canonical())))[..24], action.Name, policy.Target ?? "Current application", details, policy.Permission);
            var beforeContext = action.Name is "ClickElement" or "PressKey" ? await services.DescribeApprovalAsync(action, ct).ConfigureAwait(false) : "";
            if (!await approvals.ApproveAsync(request, ct).ConfigureAwait(false)) return ActionResult.Fail("The user cancelled the approval. No action was performed.");
            // Approval is bound to the typed action and observed destination; it never overrides policy.
            var refreshed = await security.AuthorizeAsync(action, ct).ConfigureAwait(false);
            if (!refreshed.Allowed) return ActionResult.Fail("Safety policy changed after approval: " + refreshed.Reason);
            if (refreshed.Target != policy.Target) return ActionResult.Fail("The approval destination changed. Inspect and request approval again.");
            if (beforeContext.Length > 0 && beforeContext != await services.DescribeApprovalAsync(action, ct).ConfigureAwait(false)) return ActionResult.Fail("The visible draft or nearby content changed after approval. No input was sent; inspect and approve the new content.");
            policy = refreshed;
        }
        if (action.Name == "ComposeText")
        {
            var text = await services.ComposeAsync(action.Str("instruction"), ct).ConfigureAwait(false);
            return new(true, true, "Draft generated locally; not yet inserted:\n" + text);
        }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(action.Name is "WaitForElement" or "WaitForApplication" ? Math.Min(30, action.Int("timeoutSeconds", 15)) + 2 : 35));
        return await backend.ExecuteAuthorizedAsync(action, policy, timeout.Token).ConfigureAwait(false);
    }
    private static string Describe(AgentAction a) => a.Name switch
    {
        "TypeText" or "PasteText" => "Text to enter:\n" + a.Str("text"),
        "ClickElement" => "Activate the observed control " + a.Str("elementId") + ".\nThis may send, submit, publish, share, install, or purchase. Review the target application before approving.",
        "PressKey" => "Press " + a.Str("key") + " in the message composer. This may send the current draft.",
        "Navigate" => a.Str("url"),
        _ => string.Join("\n", a.Arguments.Select(x => x.Key + ": " + x.Value.GetRawText()))
    };
}
public interface IActionServices
{
    Task<string> DescribeApprovalAsync(AgentAction action, CancellationToken ct);
    Task<string> ComposeAsync(string instruction, CancellationToken ct);
    Task WriteNewDocumentAsync(string path, string content, CancellationToken ct);
}
