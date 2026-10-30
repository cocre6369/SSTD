using System.Text.Json;
using SSTD.Core;
using SSTD.Planner;
using SSTD.Security;
using Xunit;

namespace SSTD.Tests;
public class SecurityTests
{
    private static readonly SstdSettings Enabled = new() { DesktopControl = true, Model = "test" };
    private static readonly WindowSnapshot App = new() { WindowId = "123", ProcessId = 9999, ProcessName = "TestApp.exe", ProcessPath = @"C:\Users\Test\AppData\Local\Programs\TestApp\TestApp.exe", Title = "Untitled — Test App", ClassName = "TestWindow", Elements = [new UiElement { Id = "e1", Name = "Document", ControlType = "Document", Enabled = true, CanEdit = true }] };
    private static AgentAction A(string name, object args) => new(name, JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone()));
    private static SecurityEngine Engine(SstdSettings? settings = null, WindowSnapshot? app = null, IReadOnlyList<ApplicationCandidate>? candidates = null) => new(new PolicyContext(settings ?? Enabled, app ?? App, candidates));
    private static Task<SecurityDecision> Check(string name, object args, SstdSettings? settings = null, WindowSnapshot? app = null) => Engine(settings, app).AuthorizeAsync(A(name,args), CancellationToken.None);
    [Theory]
    [InlineData("cmd.exe")] [InlineData("CMD")] [InlineData("powershell")] [InlineData("pwsh.exe")] [InlineData("Windows Terminal")] [InlineData("wt.exe")]
    [InlineData("Registry Editor")] [InlineData("regedit.exe")] [InlineData("Device Manager")] [InlineData("devmgmt.msc")] [InlineData("Task Scheduler")]
    [InlineData("services.msc")] [InlineData("gpedit.msc")] [InlineData("secpol.msc")] [InlineData("KeePass")] [InlineData("1Password")] [InlineData("Bitwarden")]
    public void Blocks_Terminals_Admin_Tools_And_Credential_Apps(string name)
    {
        Assert.True(ApplicationPolicy.IsBlockedName(name) || ApplicationPolicy.IsBlocked("", name, name));
    }
    [Theory]
    [InlineData(@"C:\Windows\System32\cmd.exe")]
    [InlineData(@"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\wt.exe")]
    [InlineData(@"C:\Users\me\AppData\Local\Microsoft\WindowsApps\Microsoft.WindowsTerminal_8wekyb3d8bbwe\wt.exe")]
    [InlineData(@"C:\Windows\System32\Taskschd.msc")]
    [InlineData(@"C:\Windows\System32\unknown-admin-tool.exe")]
    public void Blocks_Alternative_System_Paths(string path) => Assert.True(ApplicationPolicy.IsBlocked(path));
    [Theory]
    [InlineData("Run as administrator")] [InlineData("Open Terminal")] [InlineData("New Terminal")] [InlineData("Developer Tools")] [InlineData("Visual Basic Editor")] [InlineData("Enable Macros")]
    public void Blocks_Dangerous_Controls(string label) => Assert.True(ApplicationPolicy.IsDangerousControl(label));
    [Fact] public async Task Disabled_Desktop_Denies_All_Desktop_Actions()
    {
        var result = await Check("OpenApplication", new { name = "Notepad" }, new SstdSettings()); Assert.False(result.Allowed);
    }
    [Fact] public async Task Model_Cannot_Trust_Itself_Into_Bypassing_Policy()
    {
        var engine = Engine(new SstdSettings());
        var result = await engine.AuthorizeAsync(A("OpenApplication", new { name = "cmd", trusted = true, approval = "approved" }), default);
        Assert.False(result.Allowed);
    }
    [Theory]
    [InlineData("powershell.exe")] [InlineData("bash.exe")] [InlineData("mshta.exe")] [InlineData("wscript.exe")] [InlineData("python.exe")]
    public async Task Blocks_Scripting_And_Command_Launches(string name) => Assert.False((await Check("OpenApplication", new { name })).Allowed);
    [Fact] public async Task Allows_Generic_Installed_App()
    {
        var app = new ApplicationCandidate("Unfamiliar App", @"C:\Users\Test\AppData\Local\Programs\Unfamiliar\app.exe");
        var result = await Engine(candidates: [app]).AuthorizeAsync(A("OpenApplication", new { name = "Unfamiliar App" }), default);
        Assert.True(result.Allowed); Assert.Equal(app.Path, result.Target);
    }
    [Fact] public async Task Does_Not_Choose_First_Ambiguous_App()
    {
        var candidates = new[] { new ApplicationCandidate("Editor A", @"C:\Program Files\A\a.exe"), new ApplicationCandidate("Editor B", @"C:\Program Files\B\b.exe") };
        Assert.False((await Engine(candidates: candidates).AuthorizeAsync(A("OpenApplication", new { name = "Editor" }), default)).Allowed);
    }
    [Theory]
    [InlineData("OpenApplication", "{\"name\":\"Notepad\",\"arguments\":\"--elevated\"}")]
    [InlineData("UseShortcut", "{\"keys\":[\"win\",\"r\"]}")]
    [InlineData("UseShortcut", "{\"keys\":[\"ctrl\",\"shift\",\"esc\"]}")]
    [InlineData("UseShortcut", "{\"keys\":[\"alt\",\"f4\"]}")]
    [InlineData("UseShortcut", "{\"keys\":[\"ctrl\",\"s\"]}")]
    [InlineData("PressKey", "{\"key\":\"delete\"}")]
    public async Task Blocks_Keyboard_Escalation_Destructive_Defaults(string name, string args)
    {
        var result = await Engine().AuthorizeAsync(new AgentAction(name, JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(args)!), default); Assert.False(result.Allowed);
    }
    [Theory]
    [InlineData("ctrl+c")] [InlineData("ctrl+v")] [InlineData("ctrl+a")] [InlineData("ctrl+z")] [InlineData("ctrl+f")] [InlineData("ctrl+b")]
    public async Task Allows_Normal_Editing_In_Text_Field(string combo)
    {
        var app = App with { FocusedElementId = "e1" };
        var result = await Check("UseShortcut", new { keys = combo.Split('+') }, app: app); Assert.True(result.Allowed);
    }
    [Theory]
    [InlineData("Password", "Edit")] [InlineData("Enter verification code", "Edit")] [InlineData("API key", "Edit")] [InlineData("CVV", "Edit")]
    public async Task Protects_Credential_Fields(string label, string type)
    {
        var app = App with { Elements = [new UiElement { Id = "e1", Name = label, ControlType = type, Enabled = true, CanEdit = true }] };
        Assert.False((await Check("TypeText", new { text = "secret", elementId = "e1" }, app: app)).Allowed);
        Assert.False((await Check("CaptureRelevantScreenRegion", new { }, app: app)).Allowed);
    }
    [Fact] public async Task Protected_Password_Flag_Stops_All_Input()
    {
        var app = App with { Elements = [new UiElement { Id = "e1", Name = "Field", ControlType = "Password" }] };
        Assert.False((await Check("ReadText", new { }, app: app)).Allowed);
    }
    [Theory]
    [InlineData("Send")] [InlineData("Post reply")] [InlineData("Buy now")] [InlineData("Submit")] [InlineData("Upload file")] [InlineData("Share file")] [InlineData("Pay now")]
    public void External_Commit_Controls_Are_Recognized(string text) => Assert.True(ApplicationPolicy.IsCommitControl(text));
    [Fact] public async Task External_Commit_Requires_Approval()
    {
        var app = App with { Elements = [new UiElement { Id = "e1", Name = "Send", ControlType = "Button", Enabled = true, CanInvoke = true }] };
        var result = await Check("ClickElement", new { elementId = "e1" }, app: app); Assert.True(result.Allowed); Assert.True(result.RequiresApproval);
    }
    [Theory]
    [InlineData("Delete file")] [InlineData("Rename")] [InlineData("Move to")] [InlineData("Empty Recycle Bin")] [InlineData("Format drive")] [InlineData("Overwrite")] [InlineData("Replace existing")]
    public void Destructive_Controls_Are_Recognized(string text) => Assert.True(ApplicationPolicy.IsDestructiveControl(text));
    [Fact] public async Task Destructive_Click_Is_Not_Rescued_By_Approval()
    {
        var app = App with { Elements = [new UiElement { Id = "e1", Name = "Delete file", ControlType = "Button", Enabled = true }] };
        Assert.False((await Check("ClickElement", new { elementId = "e1" }, app: app)).Allowed);
    }
    [Fact] public async Task No_Stale_Element_Execution()
    {
        Assert.False((await Check("ClickElement", new { elementId = "unknown" })).Allowed);
    }
    [Theory]
    [InlineData("javascript:alert(1)")] [InlineData("file:///C:/Windows/System32/cmd.exe")] [InlineData("ms-settings:windowsupdate")] [InlineData("chrome://settings/passwords")] [InlineData("http://127.0.0.1/admin")] [InlineData("http://192.168.1.1")] [InlineData("http://10.0.0.1")] [InlineData("http://user:pass@example.com")] [InlineData("https://example.com/account/security")]
    public async Task Navigation_Rejects_Dangerous_Protocols_And_Surfaces(string url)
    {
        var browser = App with { ProcessName = "chrome.exe" };
        Assert.False((await Check("Navigate", new { url }, app: browser)).Allowed);
    }
    [Fact] public async Task Allows_Public_Research_Url()
    {
        Assert.True((await Check("Navigate", new { url = "https://example.com/research?q=history" }, app: App with { ProcessName = "chrome.exe" })).Allowed);
    }
    [Fact] public async Task Cannot_Target_SSTD_Approval_Window()
    {
        Assert.False((await Check("ClickElement", new { elementId = "e1" }, app: App with { ProcessId = Environment.ProcessId })).Allowed);
    }
    [Fact] public async Task Elevated_Targets_Are_Not_Automated() => Assert.False((await Check("TypeText", new { text = "Hello", elementId = "e1" }, app: App with { IsElevated = true })).Allowed);
    [Fact] public void Ocr_And_Settings_Cannot_Disable_Hard_Policy()
    {
        Assert.True(ApplicationPolicy.IsBlocked(@"C:\Windows\System32\regedit.exe"));
        Assert.True(ApplicationPolicy.IsBlockedPackage("Microsoft.WindowsTerminal_8wekyb3d8bbwe!App", "Terminal"));
    }
    [Fact] public async Task ReadOnly_Inspection_Does_Not_Bypass_Blocked_Window()
    {
        var terminal = App with { ProcessName = "cmd.exe", ProcessPath = @"C:\Windows\System32\cmd.exe" };
        Assert.False((await Check("InspectUIHierarchy", new { }, app: terminal)).Allowed);
    }
    [Fact] public void Secrets_Are_Redacted_Before_Storage()
    {
        const string secret = "password=SuperSecret123! and sk-abcdefghijklmnopqrstuvwxyz123456";
        var sanitized = SensitiveData.Redact(secret); Assert.DoesNotContain("SuperSecret", sanitized); Assert.DoesNotContain("sk-abcdefghijklmnopqrstuvwxyz", sanitized); Assert.True(SensitiveData.ContainsSecret(secret));
    }
}
public class AdditionalSecurityTests
{
    [Fact] public async Task VisualOnly_Mode_Cannot_Click_Without_Password_Screening()
    {
        var settings = new SstdSettings { DesktopControl=true, UiAutomation=false };
        var w = new WindowSnapshot { WindowId="1", ProcessId=9999, ProcessName="app.exe", ProcessPath=@"C:\Program Files\App\app.exe", Elements=[new UiElement{Id="e1",Name="Button",Enabled=true}] };
        var engine = new SecurityEngine(new PolicyContext(settings,w));
        var decision=await engine.AuthorizeAsync(new AgentAction("ClickElement",new Dictionary<string,JsonElement>{["elementId"]=JsonSerializer.SerializeToElement("e1")}),default);
        Assert.False(decision.Allowed);
    }
    [Fact] public void Inbox_Notepad_Is_Not_Blocked_By_System_Directory_Default()
    {
        Assert.False(ApplicationPolicy.IsBlocked(@"C:\Windows\System32\notepad.exe","notepad.exe","Untitled - Notepad","Notepad"));
    }
}

public class IdentitySecurityTests
{
    private sealed class Context(ApplicationCandidate candidate, WindowSnapshot? window = null) : IPolicyContext
    {
        public SstdSettings Settings => new() { DesktopControl = true };
        public Task<WindowSnapshot> CurrentAsync(CancellationToken ct) => Task.FromResult(window ?? new WindowSnapshot { IsBlocked = true });
        public Task<WindowSnapshot?> WindowAsync(string id, CancellationToken ct) => Task.FromResult(window);
        public Task<IReadOnlyList<ApplicationCandidate>> ApplicationsAsync(string name, CancellationToken ct) => Task.FromResult<IReadOnlyList<ApplicationCandidate>>([candidate]);
    }
    private static AgentAction A(string name, object args) => new(name, JsonSerializer.SerializeToElement(args).EnumerateObject().ToDictionary(x => x.Name, x => x.Value.Clone()));
    [Theory]
    [InlineData("cmd.exe","Windows Command Processor")]
    [InlineData("pwsh.exe","PowerShell")]
    [InlineData("powershell.exe","Windows PowerShell")]
    [InlineData("regedit.exe","Registry Editor")]
    public async Task Renamed_System_Executable_Is_Blocked_By_PE_Identity(string original, string description)
    {
        var c=new ApplicationCandidate("Friendly editor", @"C:\Program Files\Friendly\editor.exe",null,"Test",original,description);
        var d=await new SecurityEngine(new Context(c)).AuthorizeAsync(A("OpenApplication",new{name="Friendly editor"}),CancellationToken.None);
        Assert.False(d.Allowed);
    }
    [Fact]
    public async Task Renamed_Console_Window_Is_Blocked()
    {
        var w=new WindowSnapshot{WindowId="1",Title="Friendly editor",ProcessPath=@"C:\Program Files\Friendly\editor.exe",ProcessName="cmd.exe",ClassName="ConsoleWindowClass"};
        var d=await new SecurityEngine(new Context(new("editor","editor",null,"Test"),w)).AuthorizeAsync(A("ReadText",new{}),CancellationToken.None);
        Assert.False(d.Allowed);
    }
}
