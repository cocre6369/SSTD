using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using SSTD.Core;
using SSTD.Security;
using SSTD.Vision;

namespace SSTD.Automation;

public sealed class DesktopBackend(WindowsDesktop desktop, InputController input, ISecurityEngine security, Func<SstdSettings> settings, IVisualObserver vision, IActionServices services, WindowsVisualObserver visualObserver) : IDesktopBackend, IPolicyContext
{
    private WindowSnapshot? _last;
    private string? _draft;
    public SstdSettings Settings => settings();
    public async Task<WindowSnapshot> CurrentAsync(CancellationToken ct)
    {
        desktop.EnableStructuredInspection = settings().UiAutomation;
        var w = await desktop.CaptureAsync(ct).ConfigureAwait(false);
        return _last = w;
    }
    public async Task<WindowSnapshot?> WindowAsync(string id, CancellationToken ct)
    {
        var h = long.TryParse(id, out var n) ? new nint(n) : 0;
        if (!Native.IsWindow(h)) return null;
        desktop.EnableStructuredInspection = settings().UiAutomation;
        return await Task.Run(() => desktop.Describe(h, true, ct), ct).ConfigureAwait(false);
    }
    public Task<IReadOnlyList<ApplicationCandidate>> ApplicationsAsync(string name, CancellationToken ct) => desktop.FindAsync(name, ct);
    public async Task<ActionResult> ExecuteAuthorizedAsync(AgentAction a, SecurityDecision decision, CancellationToken ct)
    {
        // Defense in depth: the backend cannot bypass the independent policy either.
        var check = await security.AuthorizeAsync(a, ct).ConfigureAwait(false);
        if (!check.Allowed) return ActionResult.Fail(check.Reason);
        try
        {
            return a.Name switch
            {
                "OpenApplication" => await OpenApplication(a.Str("name"), ct),
                "OpenDocument" => await OpenDocument(a.Str("path"), ct),
                "FocusWindow" or "SwitchWindow" => await FocusWindow(a.Str("windowId"), ct),
                "CloseApplication" => await CloseWindow(a.Str("windowId"), ct),
                "InspectWindow" or "InspectUIHierarchy" => await Inspect(ct),
                "CaptureRelevantScreenRegion" => await Capture(a, ct),
                "WaitForApplication" => await WaitApp(a, ct),
                "WaitForElement" => await WaitElement(a, ct),
                "FindElement" => await Find(a, ct),
                "CreateDocument" => await Create(a, ct),
                _ => await Interact(a, ct)
            };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception e) when (e is InvalidOperationException or ElementNotAvailableException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException or ArgumentException or NotSupportedException)
        { return ActionResult.Fail(SensitiveData.SafeError(e)); }
    }
    public async Task<string> DescribeApprovalAsync(AgentAction action, CancellationToken ct)
    {
        var w = await CurrentAsync(ct).ConfigureAwait(false);
        if (w.IsPasswordContext || SecurityEngine.TargetBlock(w).Length > 0) return "Protected context: approval is not available.";
        var drafts = w.Elements.Where(e => !e.Offscreen && IsMessageEditor(w, e) && !string.IsNullOrWhiteSpace(e.Value)).Select(e => e.Name + ":\n" + e.Value).ToList();
        var visible = string.Join("\n", w.Elements.Where(e => !e.Offscreen && e.ControlType is "Text" or "ListItem").Take(20).Select(e => e.Name));
        return "Target: " + w.Title + "\n\n" + (drafts.Count > 0 ? "Visible draft content:\n" + string.Join("\n", drafts) : "The application did not expose draft text. Review the destination window; cancel if the recipient or content is not exactly intended.") + "\n\nNearby visible labels:\n" + visible;
    }
    private static bool IsMessageEditor(WindowSnapshot w, UiElement e) => (e.CanEdit || e.ControlType is "Edit" or "Document") && System.Text.RegularExpressions.Regex.IsMatch(w.ProcessName + " " + w.Title + " " + e.Name, @"message|chat|compose|reply|email|discord|slack|teams|comment", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    private async Task<ActionResult> OpenApplication(string name, CancellationToken ct)
    {
        var candidates = await desktop.FindAsync(name, ct).ConfigureAwait(false);
        if (candidates.Count != 1) return ActionResult.Fail(candidates.Count == 0 ? "Application was not found in installed apps." : "Application name is ambiguous.");
        var candidate = candidates[0];
        var before = desktop.Handles().ToHashSet();
        var pid = await Task.Run(() => ApplicationDiscovery.Launch(candidate), ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew(); nint found = 0;
        while (sw.Elapsed < TimeSpan.FromSeconds(20))
        {
            ct.ThrowIfCancellationRequested(); await Task.Delay(250, ct).ConfigureAwait(false);
            found = desktop.Handles().FirstOrDefault(h => Native.IsWindowVisible(h) && (Native.Pid(h) == (uint)pid || !before.Contains(h)) && Native.Title(h).Length > 0);
            if (found != 0) break;
        }
        if (found == 0) return new(true, false, "Windows accepted the launch, but no application window was verified before timeout.");
        var observation = await CurrentAsync(ct).ConfigureAwait(false);
        var actual = desktop.Handles().Select(h => desktop.Describe(h, false, ct)).FirstOrDefault(w => w.Handle == found);
        if (actual is null || SecurityEngine.TargetBlock(actual).Length > 0 || actual.IsElevated) return ActionResult.Fail("The launched window did not pass process identity and capability checks.");
        WindowsDesktop.Focus(found);
        return new(true, true, $"Opened {candidate.Name}; window title: {actual.Title}. This verifies launch, not the user's eventual goal.", observation);
    }
    private async Task<ActionResult> OpenDocument(string path, CancellationToken ct)
    {
        if (!SecurityEngine.IsSafeReadPath(path, out var reason, true)) return ActionResult.Fail(reason);
        var before = desktop.Handles().ToHashSet();
        await Task.Run(() => ApplicationDiscovery.OpenDocument(path), ct).ConfigureAwait(false);
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < TimeSpan.FromSeconds(12))
        {
            ct.ThrowIfCancellationRequested(); await Task.Delay(250, ct).ConfigureAwait(false);
            var h = desktop.Handles().FirstOrDefault(h => !before.Contains(h) && Native.Title(h).Contains(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));
            if (h == 0) h = desktop.Handles().FirstOrDefault(h => Native.GetForegroundWindow() == h && Native.Title(h).Contains(Path.GetFileNameWithoutExtension(path), StringComparison.OrdinalIgnoreCase));
            if (h != 0)
            {
                var w = desktop.Describe(h, false, ct);
                if (SecurityEngine.TargetBlock(w).Length > 0 || w.IsElevated) return ActionResult.Fail("The document opened in a protected application.");
                WindowsDesktop.Focus(h);
                return new(true, true, "Opened document/folder and verified a matching window: " + w.Title, await CurrentAsync(ct));
            }
        }
        return new(true, false, "The document open request was accepted, but its window could not be verified.");
    }
    private async Task<ActionResult> FocusWindow(string id, CancellationToken ct)
    {
        var w = await WindowAsync(id, ct).ConfigureAwait(false);
        if (w is null || SecurityEngine.TargetBlock(w).Length > 0) return ActionResult.Fail("The window is unavailable or protected.");
        await Task.Run(() => input.EnsureTarget(w, ct), ct).ConfigureAwait(false);
        return new(true, Native.GetForegroundWindow() == w.Handle, "Focused window: " + w.Title, await CurrentAsync(ct));
    }
    private async Task<ActionResult> CloseWindow(string id, CancellationToken ct)
    {
        var w = await WindowAsync(id, ct).ConfigureAwait(false);
        if (w is null) return ActionResult.Fail("Window no longer exists.");
        // WM_CLOSE asks the app to close gracefully. No Kill, taskkill, or forced termination.
        Native.PostMessageW(w.Handle, 0x0010, 0, 0);
        var sw = Stopwatch.StartNew(); while (sw.Elapsed < TimeSpan.FromSeconds(5))
        { ct.ThrowIfCancellationRequested(); await Task.Delay(150, ct); if (!Native.IsWindow(w.Handle)) return new(true, true, "The application window closed gracefully."); }
        return new(true, false, "Close was requested. A confirmation or save dialog may still be open; inspect before continuing.");
    }
    private async Task<ActionResult> Inspect(CancellationToken ct)
    {
        var w = await CurrentAsync(ct).ConfigureAwait(false);
        if (SecurityEngine.TargetBlock(w).Length > 0 || w.IsPasswordContext) return ActionResult.Fail("This window cannot be observed under the safety policy.");
        return new(true, true, $"Observed {w.Title}: {w.Elements.Count} elements. {string.Join(" ", w.Warnings)}", w);
    }
    private async Task<ActionResult> Capture(AgentAction a, CancellationToken ct)
    {
        var w = await CurrentAsync(ct).ConfigureAwait(false);
        var result = await vision.InspectAsync(w, a.Str("elementId").NullIfEmpty(), settings().SendScreenshotsToVisionModel, ct).ConfigureAwait(false);
        if (result.Observation is not null) _last = result.Observation;
        return result;
    }
    private async Task<ActionResult> WaitApp(AgentAction a, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < a.Int("timeoutSeconds", 15))
        {
            var w = await desktop.FindWindowAsync(a.Str("name"), ct).ConfigureAwait(false);
            if (w is not null && SecurityEngine.TargetBlock(w).Length == 0) return new(true, true, "Application window is present: " + w.Title, w);
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        return ActionResult.Fail("Timed out waiting for the named application.");
    }
    private async Task<ActionResult> WaitElement(AgentAction a, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed.TotalSeconds < a.Int("timeoutSeconds", 15))
        {
            var w = await CurrentAsync(ct).ConfigureAwait(false);
            var e = Match(w, a.Str("name"));
            if (e is not null && e.Enabled && !e.Offscreen) return new(true, true, "Found visible element: " + e.Name, w);
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        return ActionResult.Fail("The requested visible element did not appear before timeout.");
    }
    private async Task<ActionResult> Find(AgentAction a, CancellationToken ct)
    {
        var w = await CurrentAsync(ct).ConfigureAwait(false);
        UiElement? match = Match(w, a.Str("name"));
        if (match is not null && !match.Offscreen) return new(true, true, $"Found {match.Name} ({match.Id}).", w);
        if (!a.Bool("scroll", true) || a.Int("maxScrolls", 8) == 0) return ActionResult.Fail("No matching visible element was found.");
        var containers = w.Elements.Where(e => e.CanScroll && !e.Offscreen && e.Enabled).OrderByDescending(e => e.Bounds.Width * e.Bounds.Height).ToList();
        if (a.Str("elementId").Length > 0) containers = containers.Where(e => e.Id == a.Str("elementId")).ToList();
        if (containers.Count == 0) return ActionResult.Fail("No scrollable UIA region was found. Inspect visually or use the application's search field.");
        var container = containers[0]; var previous = new HashSet<string>();
        for (var i = 0; i < a.Int("maxScrolls", 8); i++)
        {
            ct.ThrowIfCancellationRequested();
            var before = Fingerprint(w); if (!previous.Add(before)) return ActionResult.Fail("Scrolling reached a stable boundary without finding the item.");
            var scroll = new AgentAction("Scroll", new Dictionary<string, JsonElement> { ["elementId"] = JsonSerializer.SerializeToElement(container.Id), ["direction"] = JsonSerializer.SerializeToElement("down"), ["amount"] = JsonSerializer.SerializeToElement(2) });
            // Re-enter the guarded backend so every scroll receives a fresh policy decision.
            var step = await ExecuteAuthorizedAsync(scroll, new SecurityDecision(true, "Bounded search scroll"), ct).ConfigureAwait(false);
            if (!step.Success) return step;
            await Task.Delay(180, ct).ConfigureAwait(false); w = await CurrentAsync(ct).ConfigureAwait(false);
            match = Match(w, a.Str("name"));
            if (match is not null && !match.Offscreen) return new(true, true, $"Found {match.Name} ({match.Id}) after {i + 1} verified scroll steps.", w);
            var updated = w.Elements.FirstOrDefault(e => e.CanScroll && e.Name == container.Name && e.ControlType == container.ControlType);
            if (updated is null) return ActionResult.Fail("The scrollable region changed. Inspect again.");
            container = updated;
        }
        return ActionResult.Fail("The item was not found within the bounded scroll search. No random clicks were attempted.");
    }
    private async Task<ActionResult> Create(AgentAction a, CancellationToken ct)
    {
        await services.WriteNewDocumentAsync(a.Str("path"), a.Str("content"), ct).ConfigureAwait(false);
        var bytes = await File.ReadAllBytesAsync(a.Str("path"), ct).ConfigureAwait(false);
        var verified = Encoding.UTF8.GetString(bytes) == a.Str("content");
        if (Path.GetExtension(a.Str("path")).Equals(".docx", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes), System.IO.Compression.ZipArchiveMode.Read);
            var entry = zip.GetEntry("word/document.xml") ?? throw new IOException("Document content part was missing.");
            using var part = entry.Open(); var xml = System.Xml.Linq.XDocument.Load(part);
            var actual = string.Join("\n", xml.Descendants("{http://schemas.openxmlformats.org/wordprocessingml/2006/main}p").Select(p => string.Concat(p.Descendants("{http://schemas.openxmlformats.org/wordprocessingml/2006/main}t").Select(t => t.Value))));
            var expected = string.Join("\n", a.Str("content").Replace("\r\n", "\n").Split('\n').Select(line => System.Text.RegularExpressions.Regex.Replace(line, @"^(?:#{1,6}\s+|[-*]\s+)", "")));
            verified = actual == expected;
        }
        return new(true, verified, verified ? "Created and read back the new document: " + a.Str("path") : "Document creation could not be verified.");
    }
    private async Task<ActionResult> Interact(AgentAction a, CancellationToken ct)
    {
        var w = await CurrentAsync(ct).ConfigureAwait(false);
        if (a.Arguments.ContainsKey("windowId")) w = await WindowAsync(a.Str("windowId"), ct).ConfigureAwait(false) ?? throw new InvalidOperationException("Window vanished.");
        await Task.Run(() => input.EnsureTarget(w, ct), ct).ConfigureAwait(false);
        w = await CurrentAsync(ct).ConfigureAwait(false);
        // Resolve against the newly focused instance, not the old Z-order target.
        var again = await security.AuthorizeAsync(a, ct).ConfigureAwait(false);
        if (!again.Allowed) return ActionResult.Fail(again.Reason);
        var e = a.Arguments.ContainsKey("elementId") ? w.Find(a.Str("elementId")) : w.Find(w.FocusedElementId ?? "");
        if (a.Arguments.ContainsKey("elementId") && e is null) return ActionResult.Fail("Element IDs changed during focus. Inspect the focused window again.");
        var before = Fingerprint(w);
        switch (a.Name)
        {
            case "ClickElement":
                if (e is null) return ActionResult.Fail("No target element.");
                await Task.Run(() =>
                {
                    var live = desktop.Resolve(w, e.Id); var fingerprint = live.GetRuntimeId();
                    if (live.Current.IsPassword || !live.Current.IsEnabled || live.Current.IsOffscreen) throw new InvalidOperationException("Element is unavailable.");
                    if (live.TryGetCurrentPattern(InvokePattern.Pattern, out var p)) ((InvokePattern)p).Invoke();
                    else if (live.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var si)) ((SelectionItemPattern)si).Select();
                    else if (live.TryGetCurrentPattern(TogglePattern.Pattern, out var tp)) ((TogglePattern)tp).Toggle();
                    else if (live.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out var ep)) { var x = (ExpandCollapsePattern)ep; if (x.Current.ExpandCollapseState == ExpandCollapseState.Expanded) x.Collapse(); else x.Expand(); }
                    else if (e.Source == "OCR")
                    {
                        if (!visualObserver.TryResolve(w, e.Id, out var rect)) throw new InvalidOperationException("The OCR observation expired. Capture and inspect again.");
                        input.Click(w, e with { Bounds = rect }, ct);
                    }
                    else input.Click(w, e, ct);
                }, ct).ConfigureAwait(false); break;
            case "TypeText": case "PasteText":
            {
                var text = a.Str("text");
                if (e is not null)
                {
                    var live = desktop.Resolve(w, e.Id);
                    if (!w.ProcessName.Contains("winword", StringComparison.OrdinalIgnoreCase) && !w.ProcessName.Contains("soffice", StringComparison.OrdinalIgnoreCase) &&
                        live.TryGetCurrentPattern(ValuePattern.Pattern, out var vp) && !((ValuePattern)vp).Current.IsReadOnly)
                    {
                        if (a.Bool("append") || a.Arguments.ContainsKey("append"))
                        {
                            var value = a.Bool("append") ? ((ValuePattern)vp).Current.Value + text : text;
                            if (SensitiveData.ContainsSecret(value)) return ActionResult.Fail("Credential material is not permitted.");
                            ((ValuePattern)vp).SetValue(value);
                            _draft = value; break;
                        }
                        // Default text entry is at the caret, not destructive replacement of a field.
                        live.SetFocus();
                    }
                    live.SetFocus();
                }
                if (a.Name == "TypeText" && a.Bool("append")) input.Shortcut(w, ["ctrl", "end"], ct);
                await Task.Run(() => input.Type(w, text, ct), ct).ConfigureAwait(false); _draft = text; break;
            }
            case "PressKey": await Task.Run(() => input.Shortcut(w, [a.Str("key")], ct), ct).ConfigureAwait(false); break;
            case "UseShortcut": await Task.Run(() => input.Shortcut(w, a.Arguments["keys"].EnumerateArray().Select(k => k.GetString()!).ToArray(), ct), ct).ConfigureAwait(false); break;
            case "Scroll":
                var containers = w.Elements.Where(x => x.CanScroll && !x.Offscreen).OrderByDescending(x => x.Bounds.Width * x.Bounds.Height).ToList();
                var scrollTarget = e ?? containers.FirstOrDefault();
                if (scrollTarget is null) return ActionResult.Fail("No scrollable region was identified.");
                await Task.Run(() => input.Scroll(w, scrollTarget, a.Str("direction"), a.Int("amount", 1), ct), ct).ConfigureAwait(false); break;
            case "ReadText": return Read(w, e);
            case "SelectText":
                if (e is null) return ActionResult.Fail("Specify an observed text element.");
                await Task.Run(() =>
                {
                    var live = desktop.Resolve(w, e.Id);
                    if (!live.TryGetCurrentPattern(TextPattern.Pattern, out var tp)) throw new InvalidOperationException("This element does not expose selectable text.");
                    var text = (TextPattern)tp;
                    if (a.Arguments.ContainsKey("start"))
                    {
                        var all = text.DocumentRange.GetText(-1); var start = a.Int("start"); var length = a.Int("length", all.Length - start);
                        if (start < 0 || length < 0 || start + length > all.Length) throw new InvalidOperationException("Selection is outside the text.");
                        if (SensitiveData.ContainsSecret(all)) throw new InvalidOperationException("Protected text cannot be selected.");
                        var range = text.DocumentRange.Clone();
                        range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, -all.Length);
                        range.MoveEndpointByUnit(TextPatternRangeEndpoint.Start, TextUnit.Character, start);
                        range.MoveEndpointByUnit(TextPatternRangeEndpoint.End, TextUnit.Character, length); range.Select();
                    }
                    else text.DocumentRange.Select();
                }, ct).ConfigureAwait(false); break;
            case "CopyText":
                // Copy the user's selection without reading or persisting clipboard contents.
                if (e is not null) await Task.Run(() => desktop.Resolve(w, e.Id).SetFocus(), ct);
                await Task.Run(() => input.Shortcut(w, ["ctrl", "c"], ct), ct).ConfigureAwait(false);
                return new(true, false, "Copy shortcut sent to the approved application. Clipboard contents were not read, logged, or stored; paste into a permitted editor to verify.");
            case "Navigate":
                input.Shortcut(w, ["ctrl", "l"], ct); await Task.Delay(100, ct); await Task.Run(() => input.Type(w, a.Str("url"), ct), ct); input.Shortcut(w, ["enter"], ct); break;
            default: return ActionResult.Fail("This action has no Windows backend implementation.");
        }
        await Task.Delay(250, ct).ConfigureAwait(false);
        var after = await CurrentAsync(ct).ConfigureAwait(false);
        if (SecurityEngine.TargetBlock(after).Length > 0 || after.IsPasswordContext) return new(false, false, "The action revealed a protected context. Automation stopped; no further input was sent.", after);
        var changed = Fingerprint(after) != before;
        var focusChanged = after.WindowId != w.WindowId || after.Title != w.Title;
        var inserted = a.Name is "TypeText" or "PasteText" && after.Elements.Any(x => x.Value is not null && x.Value.Contains(a.Str("text")[..Math.Min(60, a.Str("text").Length)], StringComparison.Ordinal));
        var verified = inserted || changed || focusChanged;
        var message = a.Name switch
        {
            "ClickElement" => verified ? "Control activation changed the observed UI. The task goal still needs semantic verification." : "The control accepted activation, but no UI change was verified.",
            "TypeText" or "PasteText" => inserted ? "Text insertion was verified in the observed editor." : "Text input was sent, but insertion could not be read back. Inspect or verify visually before claiming success.",
            "Scroll" => changed ? "The observed viewport/content changed after scrolling." : "Scroll was sent, but no content change was observed; a boundary may have been reached.",
            "Navigate" => "Navigation was entered. Inspect the address/title and page content before relying on the result.",
            "SelectText" => "Selection was requested through the text pattern; inspect the editor to verify the selected range.",
            _ => verified ? "The input changed the observed interface." : "The input was accepted, but no UI change was verified."
        };
        return new(true, verified, message, after);
    }
    private static ActionResult Read(WindowSnapshot w, UiElement? e)
    {
        if (w.IsPasswordContext || SecurityEngine.TargetBlock(w).Length > 0) return ActionResult.Fail("Protected content cannot be read.");
        string text;
        if (e is not null) text = e.Value ?? e.Name;
        else text = string.Join("\n", w.Elements.Where(x => !x.Offscreen && x.ControlType is "Text" or "Edit" or "Document" or "ListItem").Select(x => x.Value is { Length: > 0 } ? x.Value : x.Name));
        if (SensitiveData.ContainsSecret(text)) text = SensitiveData.Redact(text);
        if (text.Length > 16000) text = text[..16000] + "\n[Read limit reached]";
        return new(true, text.Length > 0, text.Length > 0 ? "Visible text read from UI Automation (not clipboard):\n" + text : "No readable text was exposed. Try CaptureRelevantScreenRegion.", w);
    }
    internal static UiElement? Match(WindowSnapshot w, string name) => w.Elements.Where(e => e.Enabled && (e.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || e.Name.Contains(name, StringComparison.OrdinalIgnoreCase)))
        .OrderByDescending(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ThenBy(e => e.Offscreen).FirstOrDefault();
    private static string Fingerprint(WindowSnapshot w) => string.Join('|', w.Elements.Where(e => !e.Offscreen).Take(100).Select(e => (e.Name, e.ControlType, e.Selected, e.Value, e.Bounds)));
}
internal static class StringExtensions { public static string? NullIfEmpty(this string s) => string.IsNullOrEmpty(s) ? null : s; }
