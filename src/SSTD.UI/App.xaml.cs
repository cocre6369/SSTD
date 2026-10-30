using System.IO;
using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using SSTD.AI;
using SSTD.Automation;
using SSTD.Core;
using SSTD.Memory;
using SSTD.Planner;
using SSTD.Security;
using SSTD.UI.Services;
using SSTD.UI.ViewModels;
using SSTD.UI.Views;
using SSTD.Vision;

namespace SSTD.UI;
public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;
    private SstdSettings _settings = new();
    private System.Windows.Forms.NotifyIcon? _tray;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private MainWindow? _window;
    public bool SmokeMode { get; private set; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        SmokeMode = e.Args.Contains("--smoke-test", StringComparer.Ordinal);
        _mutex = new Mutex(true, @"Local\SSTD.Desktop.SingleInstance", out _ownsMutex);
        if (!_ownsMutex && !SmokeMode) { System.Windows.MessageBox.Show("SSTD is already running. Check the taskbar or system tray.", "SSTD"); Shutdown(); return; }
        DispatcherUnhandledException += (_, args) =>
        {
            SensitiveData.Redact(args.Exception.Message);
            if (_services?.GetService<MainViewModel>() is { } vm) vm.Error = "Unexpected error: " + SensitiveData.SafeError(args.Exception);
            args.Handled = true;
            if (SmokeMode) Shutdown(1);
        };
        try
        {
            var store = new LocalStore(SmokeMode ? Path.Combine(Path.GetTempPath(), "SSTD-smoke-" + Guid.NewGuid().ToString("N")) : null);
            _settings = await store.LoadAsync(CancellationToken.None);
            var client = new OllamaClient(new Uri(_settings.Endpoint));
            var collection = new ServiceCollection();
            collection.AddSingleton(store); collection.AddSingleton<ISettingsStore>(store); collection.AddSingleton<ITaskStore>(store); collection.AddSingleton<IConversationStore>(store);
            collection.AddSingleton(client); collection.AddSingleton<IOllamaClient>(client);
            collection.AddSingleton<WindowsDesktop>(); collection.AddSingleton<IWindowsSnapshotProvider>(x => x.GetRequiredService<WindowsDesktop>()); collection.AddSingleton<IApplicationResolver>(x => x.GetRequiredService<WindowsDesktop>());
            collection.AddSingleton<InputController>(); collection.AddSingleton<WindowsVisualObserver>(); collection.AddSingleton<IVisualObserver>(x => x.GetRequiredService<WindowsVisualObserver>());
            collection.AddSingleton<ITextComposer, TextComposer>(); collection.AddSingleton<INewDocumentWriter, NewDocumentWriter>();
            collection.AddSingleton<DesktopBackend>(); collection.AddSingleton<IDesktopBackend>(x => x.GetRequiredService<DesktopBackend>()); collection.AddSingleton<IPolicyContext>(x => x.GetRequiredService<DesktopBackend>());
            collection.AddSingleton<IActionServices, ActionServices>(); collection.AddSingleton<IApprovalBroker, ApprovalBroker>();
            collection.AddSingleton<ISecurityEngine, SecurityEngine>(); collection.AddSingleton<IActionExecutor, ActionExecutor>();
            collection.AddSingleton<IAgentPlanner, OllamaPlanner>();
            collection.AddSingleton<Func<SstdSettings>>(_ => () => _settings);
            collection.AddSingleton<Action<SstdSettings>>(_ => value => _settings = value);
            collection.AddSingleton<Func<bool>>(_ => () => _vision);
            collection.AddSingleton<AgentRunner>();
            collection.AddSingleton<MainViewModel>();
            _services = collection.BuildServiceProvider();
            ApplyPreferences(_settings);
            _window = new MainWindow(_services.GetRequiredService<MainViewModel>()); MainWindow = _window;
            CreateTray(); _window.Show();
            if (SmokeMode) await RunSmokeAsync();
        }
        catch (Exception ex)
        {
            if (SmokeMode) { await File.WriteAllTextAsync(Path.Combine(AppContext.BaseDirectory, "smoke-error.txt"), SensitiveData.SafeError(ex)); Shutdown(2); }
            else System.Windows.MessageBox.Show("SSTD could not start:\n" + SensitiveData.SafeError(ex), "SSTD startup error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
    private bool _vision;
    public void ApplyPreferences(SstdSettings settings)
    {
        var theme = (SolidColorBrush)Resources["Background"];
        Resources["Background"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Theme == "Midnight" ? "#070B12" : "#0B0F14")!);
        Resources["Sidebar"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(settings.Theme == "Midnight" ? "#0B101B" : "#0E131B")!);
        if (_tray is not null) _tray.Visible = settings.SystemTray;
    }
    private void CreateTray()
    {
        _tray = new System.Windows.Forms.NotifyIcon { Text = "SSTD — local desktop intelligence", Icon = System.Drawing.SystemIcons.Application, Visible = _settings.SystemTray };
        var iconStream = GetResourceStream(new Uri("pack://application:,,,/Assets/SSTD.ico"))?.Stream;
        if (iconStream is not null) { using (iconStream) _tray.Icon = new System.Drawing.Icon(iconStream); }
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open SSTD", null, (_, _) => Dispatcher.Invoke(() => { _window?.Show(); _window?.Activate(); }));
        menu.Items.Add("Stop automation", null, (_, _) => Dispatcher.Invoke(() => _services?.GetRequiredService<MainViewModel>().StopCommand.Execute(null)));
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(() => _window?.RequestExit()));
        _tray.ContextMenuStrip = menu; _tray.DoubleClick += (_, _) => Dispatcher.Invoke(() => { _window?.Show(); _window?.Activate(); });
    }
    public void NotifyTaskFinished(string status)
    {
        if (SmokeMode) return;
        if (_settings.SoundEffects) System.Media.SystemSounds.Asterisk.Play();
        if (_settings.Notifications && _tray is not null) { _tray.Visible = true; _tray.BalloonTipTitle = "SSTD"; _tray.BalloonTipText = "Task " + status.ToLowerInvariant() + ". Open SSTD to review the result."; _tray.ShowBalloonTip(3000); if (!_settings.SystemTray) _tray.Visible = false; }
    }
    private async Task RunSmokeAsync()
    {
        var output = Path.Combine(AppContext.BaseDirectory, "smoke-result.txt");
        try
        {
            await _services!.GetRequiredService<MainViewModel>().InitializeAsync();
            await Dispatcher.InvokeAsync(() => { _window!.UpdateLayout(); }, System.Windows.Threading.DispatcherPriority.Loaded);
            var security = _services.GetRequiredService<ISecurityEngine>();
            var action = new AgentAction("OpenApplication", new Dictionary<string, System.Text.Json.JsonElement> { ["name"] = System.Text.Json.JsonSerializer.SerializeToElement("cmd.exe") });
            if ((await security.AuthorizeAsync(action, CancellationToken.None)).Allowed) throw new InvalidOperationException("Safety self-check failed.");
            if (_window.ActualWidth < 800 || _services.GetRequiredService<MainViewModel>().SendCommand is null) throw new InvalidOperationException("UI self-check failed.");
            await File.WriteAllTextAsync(output, $"PASS: WPF dashboard constructed; DI resolved; SQLite initialized; Ollama availability handled; independent CMD policy rejection passed.\nVersion: {typeof(App).Assembly.GetName().Version}\nInteractive external-app/Ollama inference tests are separate.\n");
            Shutdown(0);
        }
        catch (Exception ex) { await File.WriteAllTextAsync(output, "FAIL: " + SensitiveData.SafeError(ex)); Shutdown(3); }
    }
    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        if (_services is not null) { _services.GetService<MainViewModel>()?.DisposeAsync().AsTask().GetAwaiter().GetResult(); _services.Dispose(); }
        if (_ownsMutex) _mutex?.ReleaseMutex(); _mutex?.Dispose(); base.OnExit(e);
    }
}
