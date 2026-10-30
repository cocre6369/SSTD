using System.IO;
using System.Runtime.CompilerServices;

namespace SSTD.UI;
internal static class BootDiagnostics
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            File.WriteAllText(Path.Combine(dir, "smoke-boot.txt"), "module-initializer " + DateTime.UtcNow.ToString("O"));
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try { File.WriteAllText(Path.Combine(dir, "smoke-unhandled.txt"), e.ExceptionObject?.ToString() ?? "unknown"); } catch { }
            };
        }
        catch { }
    }
}
