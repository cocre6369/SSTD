using System.Windows.Automation;
using SSTD.Core;
using SSTD.Security;

namespace SSTD.Automation;
public sealed class InputController(WindowsDesktop desktop)
{
    public void EnsureTarget(WindowSnapshot target, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (SecurityEngine.TargetBlock(target).Length > 0 || target.IsPasswordContext || target.IsElevated) throw new InvalidOperationException("The target is protected.");
        if (!WindowsDesktop.Focus(target.Handle)) throw new InvalidOperationException("Could not focus the approved window. Focus it manually or retry; input was not sent to another window.");
        var current = desktop.Describe(target.Handle, false, ct);
        if (current.ProcessId != target.ProcessId || current.ProcessPath != target.ProcessPath || SecurityEngine.TargetBlock(current).Length > 0) throw new InvalidOperationException("The foreground application changed; input was refused.");
        if ((Native.GetAsyncKeyState(0x10) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x12) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0) throw new InvalidOperationException("Release Shift, Alt, and Windows keys before automation resumes.");
    }
    public void Type(WindowSnapshot target, string text, CancellationToken ct)
    {
        if (text.Length > 32000 || SensitiveData.ContainsSecret(text)) throw new InvalidOperationException("Protected or oversized input.");
        // Unicode input events preserve text without touching or reading the clipboard.
        for (var offset = 0; offset < text.Length; offset += 32)
        {
            EnsureTarget(target, ct);
            var chunk = text[offset..Math.Min(offset + 32, text.Length)];
            var events = new List<Native.INPUT>();
            foreach (var c in chunk)
            {
                if (c == '\r') continue;
                if (c == '\n') { events.Add(Native.Key(0x0D)); events.Add(Native.Key(0x0D, true)); }
                else { events.Add(Native.Key(c, unicode: true)); events.Add(Native.Key(c, true, true)); }
            }
            if (events.Count > 0) Native.Send(events.ToArray());
        }
    }
    public void Shortcut(WindowSnapshot target, IReadOnlyList<string> keys, CancellationToken ct)
    {
        EnsureTarget(target, ct);
        var codes = keys.Select(Code).ToArray(); var inputs = new List<Native.INPUT>();
        try
        {
            foreach (var code in codes) inputs.Add(Native.Key(code));
            Native.Send(inputs.ToArray());
        }
        finally
        {
            // Always release in reverse order, even when SendInput fails part-way.
            if (inputs.Count > 0)
            {
                var releases = codes.Reverse().Select(c => Native.Key(c, true)).ToArray();
                Native.SendInput((uint)releases.Length, releases, System.Runtime.InteropServices.Marshal.SizeOf<Native.INPUT>());
            }
        }
    }
    public void Click(WindowSnapshot target, UiElement element, CancellationToken ct)
    {
        EnsureTarget(target, ct);
                System.Windows.Rect r;
        if (element.Source == "OCR") r = new System.Windows.Rect(element.Bounds.X, element.Bounds.Y, element.Bounds.Width, element.Bounds.Height);
        else { var e = desktop.Resolve(target, element.Id); r = e.Current.BoundingRectangle; if (e.Current.IsOffscreen || !e.Current.IsEnabled) throw new InvalidOperationException("The target is not currently visible and enabled."); }
        if (r.IsEmpty) throw new InvalidOperationException("The target is not currently visible and enabled. Scroll or inspect again.");
        if (!Native.GetWindowRect(target.Handle, out var wr) || r.Left < wr.Left || r.Right > wr.Right || r.Top < wr.Top || r.Bottom > wr.Bottom) throw new InvalidOperationException("The target lies outside its approved window.");
        var point = element.Source == "OCR" ? new System.Windows.Point(element.Bounds.CenterX, element.Bounds.CenterY) : desktop.Resolve(target, element.Id).GetClickablePoint();
        var hit = Native.WindowFromPoint(new Native.POINT { X = (int)point.X, Y = (int)point.Y });
        if (Native.Pid(hit) != (uint)target.ProcessId) throw new InvalidOperationException("Another window covers the target. No coordinate click was sent.");
        EnsureTarget(target, ct);
        var move = new Native.INPUT { type = Native.INPUT_MOUSE, U = new() { mi = new() { dx = (int)Math.Round(point.X), dy = (int)Math.Round(point.Y), dwFlags = Native.MOUSEEVENTF_MOVE } } };
        // Normalized absolute coordinates are derived from this live UIA element, never hard-coded.
        var width = System.Windows.Forms.SystemInformation.VirtualScreen;
        move.U.mi.dx = (int)Math.Round((point.X - width.X) * 65535 / Math.Max(1, width.Width - 1));
        move.U.mi.dy = (int)Math.Round((point.Y - width.Y) * 65535 / Math.Max(1, width.Height - 1));
        move.U.mi.dwFlags |= Native.MOUSEEVENTF_ABSOLUTE;
        Native.Send(move);
        Native.Send(new Native.INPUT { type = Native.INPUT_MOUSE, U = new() { mi = new() { dwFlags = Native.MOUSEEVENTF_LEFTDOWN } } }, new Native.INPUT { type = Native.INPUT_MOUSE, U = new() { mi = new() { dwFlags = Native.MOUSEEVENTF_LEFTUP } } });
    }
    public void Scroll(WindowSnapshot target, UiElement element, string direction, int amount, CancellationToken ct)
    {
        EnsureTarget(target, ct);
        var e = desktop.Resolve(target, element.Id);
        if (e.TryGetCurrentPattern(ScrollPattern.Pattern, out var p))
        {
            var scroll = (ScrollPattern)p;
            var horizontal = direction is "left" or "right"; var positive = direction is "down" or "right";
            if (!(horizontal ? scroll.Current.HorizontallyScrollable : scroll.Current.VerticallyScrollable)) throw new InvalidOperationException("This region does not scroll in that direction.");
            for (var i = 0; i < amount; i++)
            {
                ct.ThrowIfCancellationRequested(); EnsureTarget(target, ct);
                scroll.Scroll(horizontal ? positive ? ScrollAmount.SmallIncrement : ScrollAmount.SmallDecrement : ScrollAmount.NoAmount,
                    horizontal ? ScrollAmount.NoAmount : positive ? ScrollAmount.SmallIncrement : ScrollAmount.SmallDecrement);
            }
            return;
        }
        Click(target, target.Find(element.Id)!, ct);
        for (var i = 0; i < amount; i++)
        {
            EnsureTarget(target, ct);
            Native.Send(new Native.INPUT { type = Native.INPUT_MOUSE, U = new() { mi = new() { mouseData = unchecked((uint)(120 * (direction is "down" or "right" ? -1 : 1))), dwFlags = direction is "left" or "right" ? Native.MOUSEEVENTF_HWHEEL : Native.MOUSEEVENTF_WHEEL } } });
        }
    }
    public static ushort Code(string key) => key.ToLowerInvariant() switch
    {
        "ctrl" or "control" => 0x11, "alt" => 0x12, "shift" => 0x10,
        "enter" or "return" => 0x0D, "tab" => 0x09, "escape" or "esc" => 0x1B,
        "up" => 0x26, "down" => 0x28, "left" => 0x25, "right" => 0x27,
        "home" => 0x24, "end" => 0x23, "pageup" => 0x21, "pagedown" => 0x22,
        "backspace" => 0x08, "delete" => 0x2E, "insert" => 0x2D, "space" => 0x20,
        _ when key.Length == 1 && key[0] is >= 'a' and <= 'z' => (ushort)(0x41 + key.ToLowerInvariant()[0] - 'a'),
        _ when key.Length == 1 && key[0] is >= '0' and <= '9' => (ushort)key[0],
        "." => 0xBE, "-" => 0xBD, "+" => 0xBB, "," => 0xBC, "/" => 0xBF, ";" => 0xBA,
        _ when key.Length >= 2 && key[0] is 'f' or 'F' && int.TryParse(key[1..], out var f) && f is >= 1 and <= 12 => (ushort)(0x6F + f),
        _ => throw new ArgumentException("Unregistered key.")
    };
}
