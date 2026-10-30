using System.Runtime.InteropServices;
using System.Text;

namespace SSTD.Automation;
internal static class Native
{
    public const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1, KEYEVENTF_EXTENDEDKEY = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
    public const uint MOUSEEVENTF_MOVE = 1, MOUSEEVENTF_ABSOLUTE = 0x8000, MOUSEEVENTF_LEFTDOWN = 2, MOUSEEVENTF_LEFTUP = 4, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_HWHEEL = 0x1000;
    [StructLayout(LayoutKind.Sequential)] public struct INPUT { public uint type; public INPUTUNION U; }
    [StructLayout(LayoutKind.Explicit)] public struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] public struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public nint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] public struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public nint dwExtraInfo; }
    [DllImport("user32.dll", SetLastError = true)] public static extern uint SendInput(uint nInputs, INPUT[] inputs, int cbInput);
    [DllImport("user32.dll")] public static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindow(nint h);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint h);
    [DllImport("user32.dll")] public static extern bool IsIconic(nint h);
    [DllImport("user32.dll")] public static extern bool ShowWindow(nint h, int cmd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(nint h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextW(nint h, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassNameW(nint h, StringBuilder s, int max);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc proc, nint param);
    public delegate bool EnumProc(nint h, nint param);
    [DllImport("user32.dll")] public static extern nint WindowFromPoint(POINT p);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(nint h, out RECT r);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] public static extern bool PostMessageW(nint h, uint msg, nint w, nint l);
    [DllImport("user32.dll")] public static extern nint GetWindow(nint h, uint cmd);
    [DllImport("kernel32.dll", SetLastError = true)] public static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(nint h);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageNameW(nint h, uint flags, StringBuilder name, ref uint size);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll", SetLastError = true)] public static extern bool GetTokenInformation(nint token, int cls, out uint info, uint length, out uint returnLength);
    [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint from, uint to, bool attach);
    [DllImport("user32.dll")] public static extern nint GetFocus();
    [DllImport("user32.dll")] public static extern bool BringWindowToTop(nint h);
    [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static string Title(nint h) { var s = new StringBuilder(512); GetWindowTextW(h, s, s.Capacity); return s.ToString(); }
    public static string Class(nint h) { var s = new StringBuilder(256); GetClassNameW(h, s, s.Capacity); return s.ToString(); }
    public static uint Pid(nint h) { GetWindowThreadProcessId(h, out var p); return p; }
    public static string ProcessPath(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (h == 0) return "";
        try { var s = new StringBuilder(32768); var n = (uint)s.Capacity; return QueryFullProcessImageNameW(h, 0, s, ref n) ? s.ToString() : ""; }
        finally { CloseHandle(h); }
    }
    public static bool? IsElevated(uint pid)
    {
        var h = OpenProcess(0x1000, false, pid);
        if (h == 0) return null;
        try
        {
            if (!OpenProcessToken(h, 0x0008, out var token)) return null;
            try { return GetTokenInformation(token, 20, out var elevation, 4, out _) ? elevation != 0 : null; }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(h); }
    }
    public static void Send(params INPUT[] inputs)
    {
        if (Marshal.SizeOf<INPUT>() != (nint.Size == 8 ? 40 : 28)) throw new InvalidOperationException("Unexpected input structure layout.");
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
        if (sent != inputs.Length) throw new InvalidOperationException($"Windows accepted {sent}/{inputs.Length} input events. Another application may block synthetic input.");
    }
    public static INPUT Key(ushort vk, bool up = false, bool unicode = false) => new() { type = INPUT_KEYBOARD, U = new() { ki = new() { wVk = unicode ? (ushort)0 : vk, wScan = unicode ? vk : (ushort)0, dwFlags = (up ? KEYEVENTF_KEYUP : 0) | (unicode ? KEYEVENTF_UNICODE : 0) | (!unicode && IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0) } } };
    private static bool IsExtended(ushort k) => k is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E;
}
