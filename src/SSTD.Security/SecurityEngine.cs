using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using SSTD.Core;

namespace SSTD.Security;

/// <summary>No reference to AI/planner. All decisions use typed actions and live OS metadata.</summary>
public sealed class SecurityEngine(IPolicyContext context) : ISecurityEngine
{
    private static readonly HashSet<string> Keys = new(StringComparer.OrdinalIgnoreCase)
    { "enter","return","tab","escape","esc","up","down","left","right","home","end","pageup","pagedown","backspace","delete","insert","space","f1","f2","f3","f4","f5","f6","f7","f8","f9","f10","f11","f12","a","b","c","d","e","f","g","h","i","j","k","l","m","n","o","p","q","r","s","t","u","v","w","x","y","z","0","1","2","3","4","5","6","7","8","9",".","-","+",",","/",";" };
    private static readonly HashSet<string> Modifiers = new(StringComparer.OrdinalIgnoreCase) { "ctrl", "control", "shift", "alt" };
    private static readonly HashSet<string> Shortcuts = new(StringComparer.OrdinalIgnoreCase)
    { "ctrl+a","ctrl+c","ctrl+v","ctrl+x","ctrl+z","ctrl+y","ctrl+f","ctrl+b","ctrl+i","ctrl+u","ctrl+l","ctrl+e","ctrl+r","ctrl+home","ctrl+end","shift+home","shift+end","ctrl+shift+left","ctrl+shift+right","shift+left","shift+right","shift+up","shift+down","alt+left","alt+right","ctrl+shift+l","ctrl+shift+b" };
    public async Task<SecurityDecision> AuthorizeAsync(AgentAction action, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (!ActionContract.Names.Contains(action.Name)) return SecurityDecision.Deny("This action is not in the controlled registry.");
        var settings = context.Settings;
        var permission = ActionContract.Classification(action.Name);
        if (action.Name is "ComposeText") return new(true, "Local model composition.", permission);
        if (action.Name is "CreateDocument") return AuthorizeNewFile(action);
        if (!settings.DesktopControl) return SecurityDecision.Deny("Desktop control is off. Enable it in Settings after reviewing the safety boundary.");
        if (action.Name == "OpenApplication")
        {
            var name = action.Str("name");
            if (ApplicationPolicy.IsBlockedName(name) || ApplicationPolicy.IsBlocked("", name, name)) return SecurityDecision.Deny("Terminals, scripting hosts, administration tools, and credential managers are blocked regardless of the requested command.");
            if (name.Contains('\\') || name.Contains('/') || name.Contains(':'))
            {
                if (!IsSafeExecutablePath(name, out var reason)) return SecurityDecision.Deny(reason);
                return new(true, "Validated local application; no arguments or elevation.", permission, Target: name);
            }
            var apps = await context.ApplicationsAsync(name, ct).ConfigureAwait(false);
            if (apps.Count == 0) return SecurityDecision.Deny("No installed application matches that name. Ask the user for its installed name or local executable path.");
            if (apps.Count > 1) return SecurityDecision.Deny("Several applications match. Ask the user to choose: " + string.Join(", ", apps.Take(6).Select(a => a.Name)) + ".");
            var app = apps[0];
            if (ApplicationPolicy.IsBlocked(app.Path, app.OriginalName + " " + app.Description) || ApplicationPolicy.IsBlockedName(app.Name + ".exe") || (app.AppUserModelId is not null && ApplicationPolicy.IsBlockedPackage(app.AppUserModelId, app.Name)) || (app.AppUserModelId is null && ApplicationPolicy.IsScriptOrInstaller(app.Path))) return SecurityDecision.Deny("The resolved application is outside the ordinary-application safety boundary.");
            return new(true, "Resolved installed application passed the application policy.", permission, Target: app.AppUserModelId ?? app.Path);
        }
        if (action.Name == "OpenDocument")
        {
            var path = action.Str("path");
            if (!IsSafeReadPath(path, out var reason, allowDirectory: true)) return SecurityDecision.Deny(reason);
            string[] extensions = [".txt", ".md", ".rtf", ".docx", ".xlsx", ".pptx", ".pdf", ".csv", ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".mp3", ".mp4", ".mkv", ".wav", ".mov"];
            if (!Directory.Exists(path) && !extensions.Contains(Path.GetExtension(path).ToLowerInvariant())) return SecurityDecision.Deny("This document type is not permitted. Executable, script, macro-enabled, and shortcut files cannot be opened.");
            return new(true, "Read/open only; an existing file will never be replaced.", permission, Target: path);
        }
        WindowSnapshot? target = null;
        if (action.Arguments.ContainsKey("windowId")) target = await context.WindowAsync(action.Str("windowId"), ct).ConfigureAwait(false);
        else if (action.Name is not "WaitForApplication") target = await context.CurrentAsync(ct).ConfigureAwait(false);
        if (action.Arguments.ContainsKey("windowId") && target is null) return SecurityDecision.Deny("The target window no longer exists. Observe the desktop again.");
        if (target is not null)
        {
            var blocked = TargetBlock(target);
            if (blocked.Length > 0) return SecurityDecision.Deny(blocked);
        }
        if (action.Name is "InspectWindow" or "InspectUIHierarchy" or "WaitForApplication") return new(true, "Bounded, read-only observation.", permission, Target: target?.Title);
        if (target is null) return SecurityDecision.Deny("No foreground target window is available.");
        if (target.IsElevated) return SecurityDecision.Deny("SSTD does not automate elevated applications. Use a normal, non-administrator application window.");
        if (target.IsPasswordContext || target.Elements.Any(e => e.ControlType == "Password" || PasswordLabel(e.Name) || PasswordLabel(e.AutomationId ?? ""))) return SecurityDecision.Deny("A password or authentication field is present. Stop and ask the user to authenticate manually; no observation or input will be performed.");
        if (action.Name == "WaitForElement") return new(true, "Bounded UI observation.", permission, Target: target.Title);
        if (!settings.UiAutomation && action.Name is not ("CaptureRelevantScreenRegion" or "Navigate")) return SecurityDecision.Deny("UI Automation is disabled. Only application launch, observation, and permitted navigation are available; visual-only clicks require password screening.");
        var element = action.Arguments.ContainsKey("elementId") ? target.Find(action.Str("elementId")) : target.Find(target.FocusedElementId ?? "");
        if (action.Arguments.ContainsKey("elementId") && element is null) return SecurityDecision.Deny("The element ID is stale or belongs to another window. Inspect the current interface again.");
        var label = string.Join(" ", element?.Name, element?.AutomationId);
        if (ApplicationPolicy.IsDangerousControl(label) || ApplicationPolicy.IsDangerousControl(target.Title)) return SecurityDecision.Deny("This control or context can open a command/system/credential surface and is blocked.");
        if (element is not null && !element.Enabled && action.Name is not ("ReadText" or "FindElement" or "CaptureRelevantScreenRegion")) return SecurityDecision.Deny("The target control is disabled.");
        if (action.Name is "TypeText" or "PasteText")
        {
            var text = action.Str("text");
            if (SensitiveData.ContainsSecret(text)) return SecurityDecision.Deny("Credentials, authentication codes, and private keys must never be entered or recorded by SSTD.");
            if (element is not null && (PasswordLabel(element.Name) || PasswordLabel(element.AutomationId ?? ""))) return SecurityDecision.Deny("The target is a credential field. Ask the user to enter it manually.");
            if (IsShellInputContext(target, element)) return SecurityDecision.Deny("Typing into shell launchers, file-manager address bars, command palettes, or system dialogs is blocked. Use the validated OpenApplication or OpenDocument action.");
            if (IsExternalEditor(target, element) || LooksLikeCommitDraft(text, target)) return new(true, "External text must be reviewed immediately before entry.", permission, true, target.Title);
            return new(true, "Normal text entry in the validated application.", permission, Target: target.Title);
        }
        if (action.Name == "PressKey")
        {
            var key = action.Str("key");
            if (!Keys.Contains(key)) return SecurityDecision.Deny("The key is not on the productivity allow-list. Windows/system shortcuts are forbidden.");
            if (IsShellInputContext(target, element)) return SecurityDecision.Deny("Keyboard launch/navigation in a shell or system input surface is blocked.");
            if (IsFileManager(target) && key is "delete" or "backspace" or "f2" or "enter") return SecurityDecision.Deny("File deletion, renaming, and blind activation in file lists are blocked. Open a validated document explicitly.");
            if (key is "delete" or "backspace" && (element is null || !IsTextEdit(element))) return SecurityDecision.Deny("Delete/Backspace is only allowed in an identified text editor, never in a file or account list.");
            if (key is "f2" or "f12" or "f5") return SecurityDecision.Deny("This function key can open a save, rename, developer, or execution surface; use a safer registered action.");
            if (key is "enter" or "return" && element is not null && IsExternalEditor(target, element)) return External(permission, target, "Press Enter in a message composer");
            return new(true, "Allow-listed productivity key.", permission, Target: target.Title);
        }
        if (action.Name == "UseShortcut")
        {
            var keys = action.Arguments["keys"].EnumerateArray().Select(x => x.GetString()!).ToArray();
            if (keys.Any(k => !Keys.Contains(k) && !Modifiers.Contains(k))) return SecurityDecision.Deny("Unregistered key or system modifier.");
            var combo = string.Join("+", keys).ToLowerInvariant();
            if (!Shortcuts.Contains(combo)) return SecurityDecision.Deny("Only normal editing, selection, search, and navigation shortcuts are allowed. Run, terminal, task manager, developer tools, save/overwrite, and OS shortcuts are blocked.");
            if (IsShellInputContext(target, element)) return SecurityDecision.Deny("Shortcuts cannot be used in shell/system input surfaces.");
            if (IsFileManager(target) && combo is "ctrl+x" or "ctrl+a" or "ctrl+v") return SecurityDecision.Deny("File move/replace operations are blocked in file managers.");
            if (combo == "ctrl+x" && (element is null || !IsTextEdit(element))) return SecurityDecision.Deny("Cut is only allowed in an identified text editor.");
            if (combo == "ctrl+l" && IsFileManager(target)) return SecurityDecision.Deny("The file-manager address bar can execute commands and is blocked.");
            return new(true, "Allow-listed productivity shortcut.", permission, Target: target.Title);
        }
        if (action.Name == "Navigate") return AuthorizeUrl(action.Str("url"), target, permission);
        if (action.Name == "ClickElement")
        {
            if (element is null) return SecurityDecision.Deny("A live element ID is required; arbitrary screen coordinates are not accepted.");
            if (ApplicationPolicy.IsDestructiveControl(label)) return SecurityDecision.Deny("Destructive file/account operations and overwrite commands are permanently blocked.");
            if (ApplicationPolicy.IsCommitControl(label)) return External(permission, target, "Activate " + label);
            if (IsFileManager(target) && Regex.IsMatch(element.Name, @"\.(exe|cmd|bat|ps1|msi|scr|vbs|reg|msc)\b", RegexOptions.IgnoreCase)) return SecurityDecision.Deny("Launching programs from file lists bypasses application validation. Use OpenApplication.");
            return new(true, "Validated live UI element.", permission, Target: target.Title + " / " + element.Name);
        }
        if (action.Name == "CaptureRelevantScreenRegion" && !settings.VisualRecognition) return SecurityDecision.Deny("Visual recognition is disabled.");
        if (action.Name is "Scroll" or "SelectText" or "CopyText" && IsFileManager(target) && action.Name == "SelectText") return SecurityDecision.Deny("File-list selection manipulation is not permitted.");
        return new(true, "Action is within the ordinary-application boundary.", permission, Target: target.Title);
    }
    private static SecurityDecision External(Permission p, WindowSnapshot target, string label) =>
        new(true, "Explicit approval is required for this external or save operation.", p, true, target.Title + " / " + label);
    public static string TargetBlock(WindowSnapshot w)
    {
        if (w.IsBlocked || ApplicationPolicy.IsBlocked(w.ProcessPath, w.ProcessName, w.Title, w.ClassName)) return "The foreground/target application is blocked: " + (w.BlockedReason ?? "terminal, system administration, or credential capability.");
        if (w.ProcessId == Environment.ProcessId) return "SSTD cannot automate its own interface or approve its own dialogs.";
        if (SensitiveData.IsSensitivePath(w.ProcessPath)) return "Credential applications and stores cannot be inspected or automated.";
        if (ApplicationPolicy.IsDangerousControl(w.Title)) return "A terminal, developer console, macro, or system configuration surface is visible.";
        if (Regex.IsMatch(w.Title, @"passwords? and autofill|payment methods|windows security|credential manager|sign in|log in|authentication|two.factor|recovery code|verify your identity", RegexOptions.IgnoreCase)) return "Authentication or credential content must be handled manually by the user.";
        return "";
    }
    private static bool PasswordLabel(string s) => Regex.IsMatch(s, @"password|passwd|passcode|one.time code|verification code|authentication code|recovery code|security code|private key|api key|\botp\b|\bcvv\b", RegexOptions.IgnoreCase);
    private static bool IsTextEdit(UiElement e) => e.CanEdit || e.ControlType is "Edit" or "Document";
    private static bool IsFileManager(WindowSnapshot w) => w.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) || w.ClassName is "CabinetWClass" or "ExploreWClass";
    private static bool IsShellInputContext(WindowSnapshot w, UiElement? e)
    {
        if (w.ClassName is "#32770" or "Shell_TrayWnd" or "WorkerW" or "Progman") return true;
        if (w.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase) && w.Handle != 0 && !IsFileManager(w)) return true;
        var label = (e?.Name ?? "") + " " + (e?.AutomationId ?? "");
        return Regex.IsMatch(label, @"address.*bar|address and search|run dialog|command.*(?:input|line|palette)|terminal.*input|powershell|omnibox|location bar|file name|save.*name", RegexOptions.IgnoreCase) &&
               !Regex.IsMatch(label, @"document|editor|page content", RegexOptions.IgnoreCase);
    }
    private static bool IsExternalEditor(WindowSnapshot w, UiElement? e) => e is not null &&
        Regex.IsMatch(w.ProcessName + " " + w.Title + " " + e.Name + " " + e.AutomationId, @"discord|slack|teams|whatsapp|telegram|outlook|message|chat|compose|reply|email|tweet|comment", RegexOptions.IgnoreCase) && IsTextEdit(e);
    private static bool LooksLikeCommitDraft(string text, WindowSnapshot w) => Regex.IsMatch(w.Title, @"discord|slack|teams|gmail|outlook|twitter|reddit", RegexOptions.IgnoreCase) && Regex.IsMatch(text, @"https?://|www\.", RegexOptions.IgnoreCase);
    private static SecurityDecision AuthorizeNewFile(AgentAction a)
    {
        var path = a.Str("path");
        if (!IsSafeReadPath(path, out var reason, false, forNew: true)) return SecurityDecision.Deny(reason);
        if (Path.GetExtension(path).ToLowerInvariant() is not (".txt" or ".md" or ".docx")) return SecurityDecision.Deny("New documents may be .txt, .md, or .docx only.");
        if (File.Exists(path) || Directory.Exists(path)) return SecurityDecision.Deny("The file already exists. SSTD creates new files only and never replaces or overwrites existing documents.");
        if (SensitiveData.ContainsSecret(a.Str("content"))) return SecurityDecision.Deny("Credential material cannot be written to documents or history.");
        return new(true, "Create a new file using exclusive creation. Existing files are never overwritten.", Permission.NewFile, true, path);
    }
    public static bool IsSafeReadPath(string input, out string reason, bool allowDirectory, bool forNew = false)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(input) || !Path.IsPathFullyQualified(input) || input.Contains(':', StringComparison.Ordinal) && input.IndexOf(':') != 1 || input.Contains("..", StringComparison.Ordinal))
        { reason = "Use an absolute local path inside your Documents or Desktop folder, without traversal or alternate data streams."; return false; }
        try
        {
            var full = Path.GetFullPath(input);
            var root = Path.GetPathRoot(full)!;
            if (new DriveInfo(root).DriveType != DriveType.Fixed) { reason = "Only local fixed-drive user documents are permitted."; return false; }
            string[] allowed = [Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Desktop")];
            if (!allowed.Where(x => x.Length > 0).Any(x => full.StartsWith(Path.GetFullPath(x) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
            { reason = "File access is restricted to your Documents and Desktop folders."; return false; }
            if (SensitiveData.IsSensitivePath(full)) { reason = "Credential and secret paths are blocked."; return false; }
            for (var p = new DirectoryInfo(full); p is not null; p = p.Parent) if (p.Exists && p.Attributes.HasFlag(FileAttributes.ReparsePoint))
            { reason = "Symbolic links and junctions are not followed across the file safety boundary."; return false; }
            if (!allowDirectory && !Directory.Exists(Path.GetDirectoryName(full))) { reason = "The destination folder does not exist."; return false; }
            if (!allowDirectory && !forNew && !File.Exists(full) && !Directory.Exists(full)) { reason = "The requested document or folder does not exist."; return false; }
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException) { reason = "The path is unavailable or invalid."; return false; }
    }
    private static bool IsSafeExecutablePath(string input, out string reason)
    {
        reason = "";
        try
        {
            if (!Path.IsPathFullyQualified(input) || !File.Exists(input) || Path.GetExtension(input).ToLowerInvariant() != ".exe" || input.IndexOf(':', 2) >= 0)
            { reason = "Provide the full path to an existing local .exe, not a command or script."; return false; }
            var full = Path.GetFullPath(input);
            if (new DriveInfo(Path.GetPathRoot(full)!).DriveType != DriveType.Fixed || ApplicationPolicy.IsBlocked(full) || ApplicationPolicy.IsScriptOrInstaller(full) || SensitiveData.IsSensitivePath(full))
            { reason = "The executable is blocked by application capability policy."; return false; }
            for (var p = new DirectoryInfo(full); p is not null; p = p.Parent) if (p.Exists && p.Attributes.HasFlag(FileAttributes.ReparsePoint))
            { reason = "Executable aliases and reparse points are not allowed."; return false; }
            return true;
        }
        catch (Exception e) when (e is IOException or ArgumentException or UnauthorizedAccessException) { reason = "Invalid executable path."; return false; }
    }
    private static SecurityDecision AuthorizeUrl(string input, WindowSnapshot w, Permission p)
    {
        if (!Uri.TryCreate(input, UriKind.Absolute, out var u) || u.Scheme is not ("http" or "https") || u.UserInfo.Length > 0 || u.Fragment.Length > 0 || input.Length > 2048)
            return SecurityDecision.Deny("Only plain HTTP(S) URLs are allowed; script, file, browser-internal, and custom protocol URLs are blocked.");
        if (u.IsLoopback || u.HostNameType == UriHostNameType.IPv4 && IsPrivate(IPAddress.Parse(u.Host)) || u.HostNameType == UriHostNameType.IPv6 && IsPrivate(IPAddress.Parse(u.Host))) return SecurityDecision.Deny("Browser automation cannot access loopback or private-network administration surfaces.");
        if (Regex.IsMatch(u.Host, @"(^|\.)(localhost|local|internal|lan)$", RegexOptions.IgnoreCase) || Regex.IsMatch(u.AbsolutePath, @"password|credential|/login|/signin|/signup|account/security|wallet|checkout|payment", RegexOptions.IgnoreCase)) return SecurityDecision.Deny("Authentication, payment, and device-administration pages are outside the automation boundary.");
        if (!w.ProcessName.Contains("chrome", StringComparison.OrdinalIgnoreCase) && !w.ProcessName.Contains("msedge", StringComparison.OrdinalIgnoreCase) && !w.ProcessName.Contains("firefox", StringComparison.OrdinalIgnoreCase)) return SecurityDecision.Deny("Focus a browser window before navigating.");
        return new(true, "Public HTTP(S) navigation in the current browser.", p, Target: u.ToString());
    }
    private static bool IsPrivate(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        var b = ip.GetAddressBytes();
        return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || b.Length == 4 && (b[0] is 0 or 10 or 127 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254 || b[0] == 100 && b[1] is >= 64 and <= 127);
    }
}
