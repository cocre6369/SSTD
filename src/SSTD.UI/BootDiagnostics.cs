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
            var dir = Environment.GetEnvironmentVariable("SSTD_SMOKE_DIR") ?? Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            if (Environment.GetCommandLineArgs().Contains("--smoke-test")) File.WriteAllText(Path.Combine(dir, "smoke-boot.txt"), "module-initializer " + DateTime.UtcNow.ToString("O"));
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try { File.WriteAllText(Path.Combine(dir, "smoke-unhandled.txt"), e.ExceptionObject?.ToString() ?? "unknown"); } catch { }
            };
        }
        catch { }
    }
}
