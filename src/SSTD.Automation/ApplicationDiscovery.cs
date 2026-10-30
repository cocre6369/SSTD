using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using SSTD.Core;
using SSTD.Security;

namespace SSTD.Automation;
public static class ApplicationDiscovery
{
    public static IReadOnlyList<ApplicationCandidate> Find(string query, CancellationToken ct)
    {
        if (Path.IsPathFullyQualified(query))
        {
            if (!File.Exists(query) || Path.GetExtension(query).ToLowerInvariant() != ".exe" || ApplicationPolicy.IsBlocked(query, Metadata(query).OriginalFilename, Metadata(query).FileDescription)) return [];
            return [Describe(query)];
        }
        var candidates = new List<ApplicationCandidate>();
        string[] roots = [Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs)];
        foreach (var root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        foreach (var file in Directory.EnumerateFiles(root, "*.lnk", SearchOption.AllDirectories))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var link = (IShellLinkW)new CShellLink();
                try
                {
                    ((System.Runtime.InteropServices.ComTypes.IPersistFile)link).Load(file, 0);
                    var path = new StringBuilder(32768); link.GetPath(path, path.Capacity, 0, 2);
                    if (path.Length == 0 || !File.Exists(path.ToString()) || Path.GetExtension(path.ToString()).ToLowerInvariant() != ".exe") continue;
                    var c = Describe(path.ToString()) with { Name = Path.GetFileNameWithoutExtension(file) };
                    if (!ApplicationPolicy.IsBlocked(c.Path, c.OriginalName, c.Description) && !ApplicationPolicy.IsBlockedName(c.Name + ".exe")) candidates.Add(c);
                }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException or InvalidCastException) { }
        }
        foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
        using (var apps = hive.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths"))
        {
            if (apps is null) continue;
            foreach (var key in apps.GetSubKeyNames())
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    using var sub = apps.OpenSubKey(key); var path = sub?.GetValue("") as string;
                    if (string.IsNullOrWhiteSpace(path)) continue;
                    path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
                    if (File.Exists(path) && Path.GetExtension(path).ToLowerInvariant() == ".exe")
                    {
                        var c = Describe(path) with { Name = Path.GetFileNameWithoutExtension(key) };
                        if (!ApplicationPolicy.IsBlocked(c.Path, c.OriginalName, c.Description)) candidates.Add(c);
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
        }
        // Packaged apps are discovered dynamically from AppsFolder, not a supported-app list.
        try
        {
            var folder = ShellItem.Create("shell:AppsFolder"); var bhid = new Guid("94F60519-2850-4924-AA5A-D15E84868039"); var iid = typeof(IEnumShellItems).GUID;
            folder.BindToHandler(0, ref bhid, ref iid, out var obj); var enumerator = (IEnumShellItems)obj;
            try
            {
                while (true)
                {
                    ct.ThrowIfCancellationRequested(); enumerator.Next(1, out var items, out var count); if (count == 0) break;
                    var item = items[0];
                    try
                    {
                        item.GetDisplayName(0, out var name); item.GetDisplayName(0x80018001, out var appId);
                        if (!string.IsNullOrEmpty(appId) && appId.Contains('!') && !ApplicationPolicy.IsBlockedPackage(appId, name)) candidates.Add(new(name, "", appId, "Windows apps"));
                    }
                    finally { Marshal.FinalReleaseComObject(item); }
                }
            }
            finally { Marshal.FinalReleaseComObject(enumerator); Marshal.FinalReleaseComObject(folder); }
        }
        catch (Exception e) when (e is COMException or InvalidCastException or UnauthorizedAccessException) { }
        var normalized = Normalize(query);
        bool Matches(string s) => Normalize(s) == normalized || Normalize(s).Contains(normalized, StringComparison.Ordinal);
        var matches = candidates.Where(c => Matches(c.Name)).DistinctBy(c => c.AppUserModelId ?? c.Path.ToLowerInvariant()).ToList();
        var exact = matches.Where(c => Normalize(c.Name) == normalized).DistinctBy(c => c.AppUserModelId ?? c.Path.ToLowerInvariant()).ToList();
        return exact.Count > 0 ? exact.Take(8).ToList() : matches.Take(8).ToList();
    }
    public static ApplicationCandidate Describe(string path)
    {
        var m = Metadata(path); return new(Path.GetFileNameWithoutExtension(path), Path.GetFullPath(path)) { OriginalName = m.OriginalFilename, Description = m.FileDescription };
    }
    private static (string OriginalFilename, string FileDescription) Metadata(string path)
    {
        try { var i = FileVersionInfo.GetVersionInfo(path); return (i.OriginalFilename ?? "", i.FileDescription ?? ""); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return ("", ""); }
    }
    public static int Launch(ApplicationCandidate c)
    {
        if (ApplicationPolicy.IsBlocked(c.Path, c.OriginalName, c.Description)) throw new InvalidOperationException("Application blocked.");
        if (c.AppUserModelId is not null)
        {
            if (ApplicationPolicy.IsBlockedPackage(c.AppUserModelId, c.Name)) throw new InvalidOperationException("Packaged system application blocked.");
            var manager = (IApplicationActivationManager)new ApplicationActivationManager();
            try
            {
                var hr = manager.ActivateApplication(c.AppUserModelId, null, 0, out var pid);
                Marshal.ThrowExceptionForHR(hr); return (int)pid;
            }
            finally { Marshal.FinalReleaseComObject(manager); }
        }
        if (ApplicationPolicy.IsScriptOrInstaller(c.Path) || !File.Exists(c.Path)) throw new InvalidOperationException("Only existing ordinary executables can be launched.");
        using (var stream = File.OpenRead(c.Path))
        using (var pe = new System.Reflection.PortableExecutable.PEReader(stream))
        {
            if (pe.PEHeaders.CoffHeader.Machine != System.Reflection.PortableExecutable.Machine.Amd64 ||
                pe.PEHeaders.PEHeader?.Subsystem is not (System.Reflection.PortableExecutable.Subsystem.WindowsGui or System.Reflection.PortableExecutable.Subsystem.WindowsCui))
                throw new InvalidOperationException("The executable format is not supported.");
            if (pe.PEHeaders.PEHeader.Subsystem == System.Reflection.PortableExecutable.Subsystem.WindowsCui)
                throw new InvalidOperationException("Console-subsystem executables are blocked, including renamed shells.");
        }
        // No shell, no command line, no arguments, no runas verb. Shortcut arguments are deliberately discarded.
        var p = Process.Start(new ProcessStartInfo { FileName = c.Path, UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(c.Path) ?? Environment.CurrentDirectory });
        return p?.Id ?? throw new InvalidOperationException("Windows did not start the application.");
    }
    public static void OpenDocument(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path)) throw new FileNotFoundException("Document or folder not found.");
        if (Directory.Exists(path))
        {
            var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
            using var p = Process.Start(new ProcessStartInfo(explorer) { ArgumentList = { path }, UseShellExecute = false });
        }
        else
        {
            // Validate the actual file association executable before ShellExecute; a .txt association to a shell is not a bypass.
            var handler = AssociationExecutable(Path.GetExtension(path));
            if (handler.Length == 0 || ApplicationPolicy.IsBlocked(handler, ApplicationDiscovery.Describe(handler).OriginalName, ApplicationDiscovery.Describe(handler).Description) || ApplicationPolicy.IsScriptOrInstaller(handler))
                throw new InvalidOperationException("The document's associated application is blocked or unavailable. Open it manually in an ordinary application.");
            using var p = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
        }
    }
    private static string AssociationExecutable(string extension)
    {
        var buffer = new StringBuilder(32768); var size = (uint)buffer.Capacity;
        var hr = AssocQueryString(0x00000400, 2, extension, "open", buffer, ref size);
        return hr == 0 ? Environment.ExpandEnvironmentVariables(buffer.ToString()) : "";
    }
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] private static extern int AssocQueryString(uint flags, uint str, string assoc, string? extra, StringBuilder output, ref uint size);
    private static string Normalize(string s) => new(s.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
}
[ComImport, Guid("00021401-0000-0000-C000-000000000046")] internal class CShellLink { }
