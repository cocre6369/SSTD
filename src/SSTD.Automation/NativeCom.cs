using System.Runtime.InteropServices;

namespace SSTD.Automation;
[ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder file, int max, nint data, uint flags);
    void GetIDList(out nint id); void SetIDList(nint id);
    void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder text, int max);
    void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder text, int max);
    void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder text, int max);
    void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string text);
    void GetHotkey(out ushort key); void SetHotkey(ushort key);
    void GetShowCmd(out int cmd); void SetShowCmd(int cmd);
    void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] System.Text.StringBuilder text, int max, out int index);
    void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string text, int index);
    void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string text, uint reserved);
    void Resolve(nint hwnd, uint flags); void SetPath([MarshalAs(UnmanagedType.LPWStr)] string text);
}
[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IShellItem
{
    void BindToHandler(nint bind, ref Guid bhid, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object obj);
    void GetParent(out IShellItem parent);
    void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
    void GetAttributes(uint mask, out uint attributes); void Compare(IShellItem other, uint hint, out int order);
}
[ComImport, Guid("BC110B6D-57E8-4148-A9C6-91015AB2F3A5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IEnumShellItems
{
    void Next(uint count, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] out IShellItem[] items, out uint fetched);
    void Skip(uint count); void Reset(); void Clone(out IEnumShellItems enumerator);
}
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")] internal class ApplicationActivationManager { }
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] internal interface IApplicationActivationManager
{
    int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appId, [MarshalAs(UnmanagedType.LPWStr)] string? arguments, uint options, out uint processId);
    int ActivateForFile([MarshalAs(UnmanagedType.LPWStr)] string appId, nint itemArray, [MarshalAs(UnmanagedType.LPWStr)] string? verb, out uint pid);
    int ActivateForProtocol([MarshalAs(UnmanagedType.LPWStr)] string appId, nint itemArray, out uint pid);
}
internal static class ShellItem
{
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)] public static extern void SHCreateItemFromParsingName([MarshalAs(UnmanagedType.LPWStr)] string path, nint bind, ref Guid iid, [MarshalAs(UnmanagedType.Interface)] out IShellItem item);
    public static IShellItem Create(string path) { var guid = typeof(IShellItem).GUID; SHCreateItemFromParsingName(path, 0, ref guid, out var item); return item; }
}
