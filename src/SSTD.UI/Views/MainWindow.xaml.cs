using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SSTD.UI.ViewModels;
using SSTD.Core;
namespace SSTD.UI.Views;
public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private bool _exitRequested;
    public MainWindow(MainViewModel vm)
    {
        InitializeComponent(); DataContext = vm; _vm = vm; vm.PropertyChanged += ViewModelChanged;
        MessagesChanged(false); vm.Messages.CollectionChanged += (_, _) => MessagesChanged(true);
        Loaded += async (_, _) => { try { await vm.InitializeAsync(); ViewModelChanged(vm, new(nameof(MainViewModel.Page))); } catch (Exception ex) { vm.Error = SSTD.Core.SensitiveData.SafeError(ex); } };
        Closing += MainWindow_Closing;
    }
    private void ViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Page) && _vm is not null)
        {
            WorkspacePage.Visibility = _vm.Page == "Workspace" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = _vm.Page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            HistoryPage.Visibility = _vm.Page == "History" ? Visibility.Visible : Visibility.Collapsed;
            if (_vm.Settings.Animations)
            {
                var visible = _vm.Page == "Settings" ? (System.Windows.UIElement)SettingsPage : _vm.Page == "History" ? HistoryPage : WorkspacePage;
                visible.BeginAnimation(OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0.65, 1, TimeSpan.FromMilliseconds(160)));
            }
        }
        if (e.PropertyName == nameof(MainViewModel.HasMessages)) MessagesChanged(false);
        if (e.PropertyName == nameof(MainViewModel.Busy) && _vm is not null && !_vm.Busy) ((App)System.Windows.Application.Current).NotifyTaskFinished(_vm.AutomationState);
    }
    private void MessagesChanged(bool scroll)
    {
        if (_vm is null) return; Welcome.Visibility = _vm.HasMessages ? Visibility.Collapsed : Visibility.Visible; ChatScroll.Visibility = _vm.HasMessages ? Visibility.Visible : Visibility.Collapsed;
        if (scroll) Dispatcher.BeginInvoke(() => ChatScroll.ScrollToEnd(), System.Windows.Threading.DispatcherPriority.Background);
    }
    private void Model_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (e.AddedItems.Count > 0 && _vm is not null && !_vm.Busy) _ = _vm.SelectModelCommand.ExecuteAsync(e.AddedItems[0] as string); }
    private void PromptInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control) { e.Handled = true; if (_vm is not null) _vm.SendCommand.Execute(null); }
    }
    public void RequestExit() { _exitRequested = true; Close(); }
    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (!_exitRequested && _vm?.Settings.SystemTray == true) { e.Cancel = true; Hide(); return; }
        _vm?.StopCommand.Execute(null);
    }
}
