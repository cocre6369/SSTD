using System.Text.Json;
using System.Text.Json.Serialization;

namespace SSTD.Core;

public sealed record ModelMessage(string Role, string Content, string? ImageBase64 = null);
public sealed record OllamaModel(string Name, long Size, string? Family = null, IReadOnlyList<string>? Capabilities = null);
public sealed record PullProgress(string Status, long Completed, long Total);
public sealed record ChatOptions(string Model, double Temperature, int ContextSize, int MaxTokens, bool Vision = false);
public sealed record Rect(double X, double Y, double Width, double Height)
{
    public bool HasArea => Width > 0 && Height > 0;
    public double CenterX => X + Width / 2;
    public double CenterY => Y + Height / 2;
}
public sealed record UiElement
{
    public required string Id { get; init; }
    public string Name { get; init; } = "";
    public string ControlType { get; init; } = "";
    public string? AutomationId { get; init; }
    public string? Value { get; init; }
    public Rect Bounds { get; init; } = new(0, 0, 0, 0);
    public bool Enabled { get; init; }
    public bool Offscreen { get; init; }
    public bool Selected { get; init; }
    public bool CanScroll { get; init; }
    public bool CanInvoke { get; init; }
    public bool CanEdit { get; init; }
    public string Source { get; init; } = "UIA";
    public int Depth { get; init; }
}
public sealed record WindowSnapshot
{
    public string WindowId { get; init; } = "0";
    public int ProcessId { get; init; }
    public string Title { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public string ProcessPath { get; init; } = "";
    public string ClassName { get; init; } = "";
    public bool IsBlocked { get; init; }
    public bool IsElevated { get; init; }
    public bool IsPasswordContext { get; init; }
    public string? BlockedReason { get; init; }
    public string? FocusedElementId { get; init; }
    public IReadOnlyList<UiElement> Elements { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    [JsonIgnore] public string? ScreenshotBase64 { get; init; }
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.UtcNow;
    [JsonIgnore] public nint Handle => nint.TryParse(WindowId, out var h) ? h : 0;
    public UiElement? Find(string id) => Elements.FirstOrDefault(e => e.Id == id);
}
public sealed record ApplicationCandidate(string Name, string Path, string? AppUserModelId = null, string Source = "Start menu")
{
    public string OriginalName { get; init; } = "";
    public string Description { get; init; } = "";
}
public sealed record ActionResult(bool Success, bool Verified, string Message, WindowSnapshot? Observation = null, string? ImageBase64 = null)
{
    public static ActionResult Fail(string message) => new(false, false, message);
}
public enum Permission { ReadOnly, DesktopInteraction, ExternalCommit, NewFile }
public sealed record ApprovalRequest(string Id, string Action, string Target, string Details, Permission Permission);
public sealed record SecurityDecision(bool Allowed, string Reason, Permission Permission = Permission.ReadOnly, bool RequiresApproval = false, string? Target = null)
{
    public static SecurityDecision Deny(string reason) => new(false, reason);
}
public sealed record TaskEvent(DateTimeOffset Time, string Phase, string Message, bool Success = true);
public sealed record TaskRecord(string Id, string Request, string Status, DateTimeOffset Created, DateTimeOffset Updated);
public sealed record TaskDetail(TaskRecord Task, IReadOnlyList<TaskEvent> Events);
public sealed record AgentReply(string Message, AgentAction? Action, bool Completed);
public sealed record AgentRunResult(bool Success, bool Verified, string Message, int Steps);

public sealed class SstdSettings
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = "";
    public double Temperature { get; set; } = 0.2;
    public int ContextSize { get; set; } = 8192;
    public int MaxResponseTokens { get; set; } = 2048;
    public bool DesktopControl { get; set; }
    public bool UiAutomation { get; set; } = true;
    public bool VisualRecognition { get; set; } = true;
    public bool SendScreenshotsToVisionModel { get; set; }
    public int AutomationTimeoutSeconds { get; set; } = 300;
    public int MaxSteps { get; set; } = 40;
    public bool TaskHistory { get; set; } = true;
    public bool ConversationHistory { get; set; } = true;
    public bool ShowProgress { get; set; } = true;
    public bool Animations { get; set; } = true;
    public bool Notifications { get; set; } = true;
    public bool SoundEffects { get; set; }
    public bool SystemTray { get; set; }
    public string Theme { get; set; } = "Charcoal";
    public string Validate()
    {
        if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            !uri.IsLoopback || uri.UserInfo.Length != 0 || uri.Query.Length != 0 ||
            uri.Fragment.Length != 0 || uri.AbsolutePath != "/")
            return "Use a loopback Ollama URL such as http://localhost:11434 (no credentials, path, or query).";
        if (!double.IsFinite(Temperature) || Temperature is < 0 or > 2) return "Temperature must be between 0 and 2.";
        if (ContextSize is < 2048 or > 131072) return "Context size must be between 2048 and 131072.";
        if (MaxResponseTokens is < 128 or > 16384) return "Maximum response must be between 128 and 16384 tokens.";
        if (AutomationTimeoutSeconds is < 15 or > 1800) return "Task timeout must be between 15 and 1800 seconds.";
        if (MaxSteps is < 1 or > 100) return "Step limit must be between 1 and 100.";
        if (Model.Length > 200) return "Model name is too long.";
        if (Theme is not ("Charcoal" or "Midnight")) return "Choose Charcoal or Midnight.";
        if (DesktopControl && !UiAutomation && !VisualRecognition) return "Enable UI Automation or visual recognition for desktop control.";
        return "";
    }
    public SstdSettings Clone() => (SstdSettings)MemberwiseClone();
}
