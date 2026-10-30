using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using SSTD.Core;
using SSTD.Security;

namespace SSTD.Automation;

public sealed class WindowsDesktop : IWindowsSnapshotProvider, IApplicationResolver
{
    private readonly Dictionary<string, (AutomationElement Element, int[] RuntimeId, string Name, string Type, Rect Bounds)> _elements = new();
    private readonly Dictionary<string, DateTime> _windows = new();
    private int _sequence;
    public Task<WindowSnapshot> CaptureAsync(CancellationToken ct) => Task.Run(() => Capture(ct), ct);
    public Task<WindowSnapshot?> FindWindowAsync(string name, CancellationToken ct) => Task.Run(() =>
    {
        foreach (var h in Handles())
        {
            ct.ThrowIfCancellationRequested();
            var title = Native.Title(h); var path = Native.ProcessPath(Native.Pid(h));
            if (title.Contains(name, StringComparison.OrdinalIgnoreCase) || Path.GetFileNameWithoutExtension(path).Contains(name, StringComparison.OrdinalIgnoreCase)) return Describe(h, false, ct);
        }
        return null;
    }, ct);
    public IEnumerable<nint> Handles()
    {
        var result = new List<nint>();
        Native.EnumWindows((h, _) => { if (Native.IsWindowVisible(h) && Native.Title(h).Length > 0) result.Add(h); return true; }, 0);
        return result;
    }
    public WindowSnapshot Capture(CancellationToken ct)
    {
        var h = Native.GetForegroundWindow();
        var warnings = new List<string>();
        if (Native.Pid(h) == (uint)Environment.ProcessId)
        {
            h = Native.GetWindow(h, 2); // GW_HWNDNEXT: do not treat SSTD as the user's target.
            while (h != 0 && (!Native.IsWindowVisible(h) || Native.Pid(h) == (uint)Environment.ProcessId || Native.Title(h).Length == 0)) h = Native.GetWindow(h, 2);
            warnings.Add("SSTD was foreground; inspecting the next visible application in Z-order. Targets are focused and rechecked before input.");
        }
        return Describe(h, true, ct, warnings);
    }
    public WindowSnapshot Describe(nint h, bool inspect, CancellationToken ct, List<string>? warnings = null)
    {
        ct.ThrowIfCancellationRequested();
        warnings ??= [];
        if (h == 0 || !Native.IsWindow(h)) return new WindowSnapshot { IsBlocked = true, BlockedReason = "No accessible desktop window.", Warnings = warnings };
        var pid = (int)Native.Pid(h); var path = Native.ProcessPath((uint)pid); var title = Native.Title(h); var cls = Native.Class(h);
        var process = Path.GetFileNameWithoutExtension(path); var elevated = Native.IsElevated((uint)pid);
        try { if (File.Exists(path)) { var info = FileVersionInfo.GetVersionInfo(path); process = string.IsNullOrWhiteSpace(info.OriginalFilename) ? process : info.OriginalFilename; } } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        var blocked = ApplicationPolicy.IsBlocked(path, process, title, cls);
        var id = h.ToInt64().ToString(); _windows[id] = DateTime.UtcNow;
        var snapshot = new WindowSnapshot { WindowId = id, ProcessId = pid, Title = SensitiveData.Redact(title), ProcessName = process, ProcessPath = path, ClassName = cls,
            IsBlocked = blocked || pid == Environment.ProcessId, IsElevated = elevated ?? false, Warnings = warnings, BlockedReason = blocked ? "Application capability is prohibited." : null };
        if (!inspect || snapshot.IsBlocked || elevated != false || SecurityEngine.TargetBlock(snapshot).Length > 0) return snapshot with { IsPasswordContext = SecurityEngine.TargetBlock(snapshot).Length > 0 };
        _elements.Clear();
        var elements = new List<UiElement>(); string? focused = null; var password = false;
        try
        {
            var root = AutomationElement.FromHandle(h);
            var focus = AutomationElement.FocusedElement;
            var queue = new Queue<(AutomationElement Element, int Depth)>(); queue.Enqueue((root, 0));
            var limit = Math.Min(350, 350);
            while (queue.Count > 0 && elements.Count < limit)
            {
                ct.ThrowIfCancellationRequested();
                var (e, depth) = queue.Dequeue();
                if (depth > 22) continue;
                string name, type, autoId;
                try { name = e.Current.Name ?? ""; type = e.Current.ControlType.ProgrammaticName.Replace("ControlType.", ""); autoId = e.Current.AutomationId ?? ""; }
                catch (ElementNotAvailableException) { continue; }
                var isPassword = false;
                try { isPassword = e.Current.IsPassword; } catch (ElementNotAvailableException) { continue; }
                if (isPassword || RegexIsPassword(name + " " + autoId)) password = true;
                var eid = "e" + (++_sequence);
                var r = e.Current.BoundingRectangle;
                var bounds = double.IsInfinity(r.Width) || double.IsInfinity(r.X) ? new Rect(0, 0, 0, 0) : new Rect(r.X, r.Y, Math.Max(0, r.Width), Math.Max(0, r.Height));
                var value = "";
                if (!isPassword && !password && type is "Edit" or "Document")
                {
                    try { if (e.TryGetCurrentPattern(ValuePattern.Pattern, out var p)) value = ((ValuePattern)p).Current.Value ?? ""; }
                    catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException) { }
                }
                bool invoke = e.TryGetCurrentPattern(InvokePattern.Pattern, out _) || e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out _) || e.TryGetCurrentPattern(TogglePattern.Pattern, out _) || e.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out _);
                bool edit = !isPassword && (e.TryGetCurrentPattern(ValuePattern.Pattern, out _) || e.TryGetCurrentPattern(TextPattern.Pattern, out _));
                var element = new UiElement { Id = eid, Name = SensitiveData.Redact(Truncate(name, 240)), AutomationId = SensitiveData.Redact(Truncate(autoId, 120)), ControlType = isPassword ? "Password" : type,
                    Value = SensitiveData.ContainsSecret(value) ? "[REDACTED]" : Truncate(value, 1000), Bounds = bounds, Enabled = e.Current.IsEnabled, Offscreen = e.Current.IsOffscreen,
                    CanScroll = e.TryGetCurrentPattern(ScrollPattern.Pattern, out _), CanInvoke = invoke, CanEdit = edit, Depth = depth,
                    Selected = e.TryGetCurrentPattern(SelectionItemPattern.Pattern, out var sp) && ((SelectionItemPattern)sp).Current.IsSelected };
                elements.Add(element);
                _elements[eid] = (e, e.GetRuntimeId(), name, type, bounds);
                try { if (focus is not null && e.GetRuntimeId().SequenceEqual(focus.GetRuntimeId())) focused = eid; } catch (ElementNotAvailableException) { }
                if (depth < 22)
                {
                    var child = TreeWalker.ControlViewWalker.GetFirstChild(e);
                    while (child is not null && queue.Count < 1000)
                    {
                        queue.Enqueue((child, depth + 1));
                        try { child = TreeWalker.ControlViewWalker.GetNextSibling(child); } catch (ElementNotAvailableException) { break; }
                    }
                }
            }
            if (queue.Count > 0) warnings.Add("UI tree was bounded for responsiveness; use FindElement or a scrollable region to inspect more.");
        }
        catch (Exception e) when (e is ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        { warnings.Add("UI Automation is only partially available: " + SensitiveData.SafeError(e)); }
        // Never expose credential values, even if an application incorrectly exposes them as ordinary text.
        if (password) return snapshot with { IsPasswordContext = true, Elements = [], Warnings = [.. warnings, "Credential field detected. Observation stopped before exposing its content."] };
        return snapshot with { Elements = elements, FocusedElementId = focused, Warnings = warnings };
    }
    internal AutomationElement Resolve(WindowSnapshot w, string id)
    {
        if (!_elements.TryGetValue(id, out var entry)) throw new InvalidOperationException("The element reference is no longer current. Inspect again.");
        var root = AutomationElement.FromHandle(w.Handle);
        if ((int)Native.Pid(w.Handle) != entry.Element.Current.ProcessId) throw new InvalidOperationException("Window ownership changed.");
        var current = entry.Element;
        try { if (!current.GetRuntimeId().SequenceEqual(entry.RuntimeId)) throw new InvalidOperationException("Element identity changed."); }
        catch (ElementNotAvailableException)
        {
            current = root.FindFirst(TreeScope.Descendants, new PropertyCondition(AutomationElement.NameProperty, entry.Name)) ?? throw new InvalidOperationException("The control disappeared. Inspect the new interface.");
            if (current.Current.ControlType.ProgrammaticName.Replace("ControlType.", "") != entry.Type) throw new InvalidOperationException("A different control now has that name.");
        }
        if (current.Current.IsPassword || current.Current.ProcessId != w.ProcessId) throw new InvalidOperationException("Target changed to a protected or different process.");
        return current;
    }
    public async Task<IReadOnlyList<ApplicationCandidate>> FindAsync(string name, CancellationToken ct) => await Task.Run(() => ApplicationDiscovery.Find(name, ct), ct);
    public static bool Focus(nint handle)
    {
        if (!Native.IsWindow(handle)) return false;
        if (Native.GetForegroundWindow() == handle) return true;
        if (Native.IsIconic(handle)) Native.ShowWindow(handle, 9);
        var foreground = Native.GetForegroundWindow(); var ours = Native.GetCurrentThreadId(); var other = Native.GetWindowThreadProcessId(foreground, out _);
        var attached = other != ours && Native.AttachThreadInput(ours, other, true);
        try { Native.BringWindowToTop(handle); Native.SetForegroundWindow(handle); }
        finally { if (attached) Native.AttachThreadInput(ours, other, false); }
        return Native.GetForegroundWindow() == handle;
    }
    private static bool RegexIsPassword(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"password|passwd|passcode|verification code|recovery code|authentication code|security code|api key|private key|\botp\b|\bcvv\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    internal static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";
}
