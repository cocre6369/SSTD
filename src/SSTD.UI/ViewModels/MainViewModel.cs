using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SSTD.AI;
using SSTD.Core;
using SSTD.Memory;
using SSTD.Planner;

namespace SSTD.UI.ViewModels;
public partial class ChatMessage : ObservableObject
{
    public string Role { get; }
    public bool IsUser => Role == "user";
    public string Label => IsUser ? "YOU" : "SSTD";
    [ObservableProperty] private string _text;
    public ChatMessage(string role, string text) { Role = role; _text = text; }
}
public sealed record PromptItem(string Title, string Description, string Prompt);
public partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly OllamaClient _client;
    private readonly LocalStore _store;
    private readonly AgentRunner _runner;
    private readonly Func<SstdSettings> _getSettings;
    private readonly Action<SstdSettings> _setSettings;
    private readonly ModelCapabilities _capabilities;
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource? _pullCts;
    private CancellationTokenSource? _statusCts = new();
    private readonly SemaphoreSlim _runGate = new(1, 1);
    private bool _initialized;
    private bool _streaming;
    [ObservableProperty] private string _input = "";
    [ObservableProperty] private string _status = "Checking Ollama…";
    [ObservableProperty] private bool _connected;
    [ObservableProperty] private bool _needsSetup = true;
    [ObservableProperty] private string _activeModel = "No model selected";
    [ObservableProperty] private string _modelDetails = "Local inference · not connected";
    [ObservableProperty] private bool _busy;
    [ObservableProperty] private string _currentTask = "Ready when you are";
    [ObservableProperty] private string _automationState = "IDLE";
    [ObservableProperty] private string _taskSummary = "No actions have been performed.";
    [ObservableProperty] private string _page = "Workspace";
    [ObservableProperty] private bool _showProgress = true;
    [ObservableProperty] private string _error = "";
    [ObservableProperty] private string _pullName = "qwen2.5:7b";
    [ObservableProperty] private string _pullStatus = "";
    [ObservableProperty] private double _pullProgress;
    [ObservableProperty] private bool _pulling;
    [ObservableProperty] private string _historySearch = "";
    [ObservableProperty] private TaskRecord? _selectedTask;
    [ObservableProperty] private string _historyFilter = "";
    public ObservableCollection<ChatMessage> Messages { get; } = [];
    public ObservableCollection<TaskEvent> Activity { get; } = [];
    public ObservableCollection<string> Models { get; } = [];
    public ObservableCollection<TaskRecord> History { get; } = [];
    public ObservableCollection<TaskEvent> HistoryEvents { get; } = [];
    public IReadOnlyList<PromptItem> Prompts { get; } =
    [
        new("Write something great", "Draft in an open document", "Open Notepad, create a new document, and write a short introduction to renewable energy. Verify the text appears."),
        new("Explore an application", "Inspect before interacting", "Inspect the current application and describe the visible controls. Do not change anything."),
        new("Find off-screen content", "Search a scrollable interface", "Find an item in the current application's list. Ask me for the exact item name, then search and scroll only as needed."),
        new("Work locally", "Your computer, your model", "What can you help me do on this computer, and what actions will you refuse?")
    ];
    public SstdSettings Settings => _getSettings().Clone();
    public MainViewModel(OllamaClient client, LocalStore store, AgentRunner runner, Func<SstdSettings> getSettings, Action<SstdSettings> setSettings, ModelCapabilities capabilities)
    {
        _client = client; _store = store; _runner = runner; _getSettings = getSettings; _setSettings = setSettings; _capabilities = capabilities;
        runner.EventRaised += OnEvent;
        runner.TokenRaised += OnToken;
        runner.ReplyCompleted += OnReplyCompleted;
        Messages.CollectionChanged += (_, e) => { if (e.Action == NotifyCollectionChangedAction.Add) OnPropertyChanged(nameof(HasMessages)); };
    }
    public bool HasMessages => Messages.Count > 0;
    public async Task InitializeAsync()
    {
        if (_initialized) return; _initialized = true;
        ApplyPrivacy();
        if (_getSettings().ConversationHistory)
        {
            var recent = await _store.RecentAsync(20, CancellationToken.None);
            foreach (var m in recent) Messages.Add(new(m.Role, m.Content));
        }
        await RefreshConnectionAsync(); await LoadHistoryAsync();
        _ = MonitorConnectionAsync(_statusCts!.Token);
    }
    private async Task MonitorConnectionAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        try { while (await timer.WaitForNextTickAsync(ct)) if (!Busy && !Pulling) await RefreshConnectionAsync(); }
        catch (OperationCanceledException) { }
    }
    [RelayCommand]
    public async Task RefreshConnectionAsync()
    {
        try
        {
            using var ct = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var version = await _client.GetVersionAsync(ct.Token);
            var models = await _client.GetModelsAsync(ct.Token);
            Connected = true; NeedsSetup = models.Count == 0;
            Status = "Ollama connected";
            Models.Clear(); foreach (var m in models) Models.Add(m.Name);
            var selected = _getSettings().Model;
            if (!models.Any(m => m.Name == selected))
            {
                selected = models.FirstOrDefault()?.Name ?? "";
                var s = _getSettings().Clone(); s.Model = selected; _setSettings(s);
            }
            ActiveModel = selected.Length > 0 ? selected : "No models installed";
            ModelDetails = $"{models.Count} local model{(models.Count == 1 ? "" : "s")} · Ollama {version}";
            _capabilities.Vision = false;
            if (selected.Length > 0)
            {
                try { var capabilities = await _client.GetCapabilitiesAsync(selected, ct.Token); _capabilities.Vision = capabilities.Contains("vision"); }
                catch (Exception ex) when (ex is not OperationCanceledException) { ModelDetails += " · vision capability unavailable"; }
            }
            OnPropertyChanged(nameof(Settings));
            if (models.Count == 0) Error = "Ollama is running, but no model is installed. Download one below with your permission.";
            else Error = "";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Connected = false; NeedsSetup = true; Status = "Ollama offline"; ActiveModel = "No connection"; ModelDetails = "Local engine required";
            Error = "Ollama is required for SSTD's local AI engine. Connect an existing installation or install it manually.";
        }
    }
    [RelayCommand]
    private async Task SendAsync()
    {
        if (Busy || string.IsNullOrWhiteSpace(Input)) return;
        if (!Connected || string.IsNullOrWhiteSpace(_getSettings().Model)) { Error = "Connect Ollama and select an installed model before sending a request."; return; }
        if (!await _runGate.WaitAsync(0)) return;
        var request = Input.Trim(); Input = ""; Error = "";
        _runCts = new CancellationTokenSource(TimeSpan.FromSeconds(_getSettings().AutomationTimeoutSeconds));
        Busy = true; AutomationState = "WORKING"; CurrentTask = request; Activity.Clear(); TaskSummary = "Planning locally…";
        Messages.Add(new("user", request));
        try
        {
            var result = await _runner.RunAsync(request, _runCts.Token);
            CurrentTask = result.Success ? "Task concluded" : "Task stopped or incomplete";
            AutomationState = result.Success ? "DONE" : "STOPPED";
            TaskSummary = $"{result.Steps} action request{(result.Steps == 1 ? "" : "s")} · {Activity.Count(e => e.Phase == "Verify" && e.Success)} verified";
            if (!result.Success && Error.Length == 0) Error = result.Message;
            if (!Messages.Any(m => m.Role == "assistant" && m.Text == result.Message)) Messages.Add(new("assistant", result.Message));
        }
        catch (Exception ex) { Error = SensitiveData.SafeError(ex); Messages.Add(new("assistant", Error)); AutomationState = "ERROR"; }
        finally { Busy = false; _runCts.Dispose(); _runCts = null; _streaming = false; _runGate.Release(); await LoadHistoryAsync(); }
    }
    [RelayCommand] private void Stop() { _runCts?.Cancel(); _pullCts?.Cancel(); AutomationState = "STOPPING"; TaskSummary = "Cancellation requested; waiting for the current operation to return."; }
    [RelayCommand] private void UsePrompt(PromptItem item) { Input = item.Prompt; Page = "Workspace"; }
    [RelayCommand] private void Navigate(string page) { Page = page; if (page == "History") _ = LoadHistoryAsync(); }
    [RelayCommand] private void NewConversation()
    {
        if (Busy) return;
        Messages.Clear(); Activity.Clear(); Error = ""; CurrentTask = "Ready when you are"; AutomationState = "IDLE"; TaskSummary = "No actions in this conversation.";
        _ = _store.ClearConversationsAsync(CancellationToken.None);
    }
    [RelayCommand]
    private async Task SaveSettingsAsync(SstdSettings? edited)
    {
        var s = edited ?? Settings; var error = s.Validate(); if (error.Length > 0) { Error = error; return; }
        try
        {
            await _store.SaveAsync(s, CancellationToken.None); _setSettings(s); _client.Configure(s.Endpoint); ApplyPrivacy();
            OnPropertyChanged(nameof(Settings)); ShowProgress = s.ShowProgress; ActiveModel = string.IsNullOrEmpty(s.Model) ? "No model selected" : s.Model;
            Error = ""; await RefreshConnectionAsync();
            System.Windows.Application.Current.Dispatcher.Invoke(() => (System.Windows.Application.Current as App)?.ApplyPreferences(s));
        }
        catch (Exception ex) { Error = SensitiveData.SafeError(ex); }
    }
    [RelayCommand]
    private async Task SelectModelAsync(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var s = Settings; s.Model = name; await SaveSettingsAsync(s);
    }
    [RelayCommand]
    private void InstallOllama()
    {
        var result = System.Windows.MessageBox.Show("SSTD does not bundle Ollama and will not install software silently.\n\nOpen the official Ollama download page in your browser? You must review and run the installer yourself.", "Install Ollama manually", MessageBoxButton.OKCancel, MessageBoxImage.Information);
        if (result == MessageBoxResult.OK) OpenUrl("https://ollama.com/download/windows");
    }
    [RelayCommand] private void ConnectExisting() { Page = "Settings"; Error = "Enter your loopback Ollama endpoint, save, then retry the connection."; }
    [RelayCommand]
    private async Task PullModelAsync()
    {
        if (Pulling) return;
        var model = PullName.Trim();
        try { OllamaClient.ValidateModel(model); } catch (ArgumentException ex) { Error = ex.Message; return; }
        if (!Connected) { Error = "Connect to Ollama before downloading a model."; return; }
        var consent = System.Windows.MessageBox.Show($"Download the Ollama model '{model}'?\n\nThis may download several gigabytes, use disk space, and take significant time. SSTD sends the request to your configured local Ollama server; Ollama obtains the model from its registry. No model is downloaded without this approval.", "Approve model download", MessageBoxButton.OKCancel, MessageBoxImage.Warning);
        if (consent != MessageBoxResult.OK) { PullStatus = "Download cancelled. Nothing was requested."; return; }
        _pullCts = new CancellationTokenSource(TimeSpan.FromMinutes(90)); Pulling = true; PullStatus = "Requesting model…"; PullProgress = 0;
        try
        {
            var success = false;
            await foreach (var p in _client.PullModelAsync(model, _pullCts.Token))
            {
                PullStatus = p.Status; PullProgress = p.Total > 0 ? 100.0 * p.Completed / p.Total : 0;
                if (p.Status == "success") success = true;
            }
            if (success) { PullStatus = "Model installed."; await RefreshConnectionAsync(); await SelectModelAsync(model); }
            else PullStatus = "The stream ended before Ollama reported success. Retry or check Ollama.";
        }
        catch (OperationCanceledException) { PullStatus = "Download request cancelled. Ollama may retain completed layers."; }
        catch (Exception ex) { PullStatus = SensitiveData.SafeError(ex); }
        finally { Pulling = false; _pullCts.Dispose(); _pullCts = null; }
    }
    [RelayCommand] private void CancelPull() => _pullCts?.Cancel();
    [RelayCommand]
    public async Task LoadHistoryAsync()
    {
        try { var records = await _store.ListAsync(CancellationToken.None); History.Clear(); foreach (var r in records.Where(r => string.IsNullOrEmpty(HistoryFilter) || r.Request.Contains(HistoryFilter, StringComparison.OrdinalIgnoreCase) || r.Status.Contains(HistoryFilter, StringComparison.OrdinalIgnoreCase))) History.Add(r); }
        catch (Exception ex) { Error = "History unavailable: " + SensitiveData.SafeError(ex); }
    }
    [RelayCommand]
    private async Task InspectTaskAsync(TaskRecord? record)
    {
        if (record is null) return; SelectedTask = record; var detail = await _store.GetAsync(record.Id, CancellationToken.None); HistoryEvents.Clear();
        if (detail is not null) foreach (var e in detail.Events) HistoryEvents.Add(e);
    }
    [RelayCommand]
    private async Task ClearHistoryAsync()
    {
        if (System.Windows.MessageBox.Show("Permanently clear SSTD task history? This does not change documents or other applications.", "Clear task history", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await _store.ClearAsync(CancellationToken.None); History.Clear(); HistoryEvents.Clear(); SelectedTask = null;
    }
    [RelayCommand]
    private async Task ClearAllAsync()
    {
        if (Busy) { Error = "Stop the active task before clearing local data."; return; }
        if (System.Windows.MessageBox.Show("Clear SSTD conversations and task history? Settings are retained. Ollama and its models are not modified.", "Clear local data", MessageBoxButton.OKCancel, MessageBoxImage.Warning) != MessageBoxResult.OK) return;
        await _store.ClearAllAsync(CancellationToken.None); Messages.Clear(); History.Clear(); HistoryEvents.Clear(); Activity.Clear();
    }
    [RelayCommand] private void OpenDataFolder()
    {
        try { Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { _store.DataDirectory }, UseShellExecute = false }); }
        catch (Exception ex) { Error = SensitiveData.SafeError(ex); }
    }
    [RelayCommand] private void OpenOllamaModels() => OpenUrl("https://ollama.com/library");
    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { System.Windows.MessageBox.Show("Could not open the browser: " + SensitiveData.SafeError(ex)); }
    }
    private void ApplyPrivacy() { _store.HistoryEnabled = _getSettings().TaskHistory; _store.ConversationEnabled = _getSettings().ConversationHistory; ShowProgress = _getSettings().ShowProgress; }
    private void OnEvent(TaskEvent ev) => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
    {
        Activity.Add(ev); if (Activity.Count > 300) Activity.RemoveAt(0);
        if (ev.Phase is "Action" or "Verify" or "Rejected") TaskSummary = ev.Message.Split('\n')[0];
    });
    private void OnToken(string token) => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
    {
        if (!_streaming) { Messages.Add(new("assistant", token)); _streaming = true; }
        else Messages[^1].Text += token;
    });
    private void OnReplyCompleted(string reply) => System.Windows.Application.Current?.Dispatcher.Invoke(() =>
    {
        if (_streaming) Messages[^1].Text = reply;
        else if (reply.Length > 0) Messages.Add(new("assistant", reply));
        _streaming = false;
    });
    partial void OnHistoryFilterChanged(string value) => _ = LoadHistoryAsync();
    private bool _disposed;
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return; _disposed = true;
        _statusCts?.Cancel(); _runCts?.Cancel(); _pullCts?.Cancel();
        _runner.EventRaised -= OnEvent; _runner.TokenRaised -= OnToken; _runner.ReplyCompleted -= OnReplyCompleted;
        _statusCts?.Dispose(); _runGate.Dispose(); await Task.CompletedTask;
    }
}
