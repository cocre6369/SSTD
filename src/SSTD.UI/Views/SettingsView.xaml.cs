using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SSTD.Core;
using SSTD.UI.ViewModels;

namespace SSTD.UI.Views;
public partial class SettingsView : System.Windows.Controls.UserControl
{
    private MainViewModel? _vm;
    public SettingsView() { InitializeComponent(); DataContextChanged += (_, _) => { if (_vm is not null) _vm.PropertyChanged -= VmChanged; _vm = DataContext as MainViewModel; if (_vm is not null) { _vm.PropertyChanged += VmChanged; Load(); } }; IsVisibleChanged += (_, e) => { if ((bool)e.NewValue) Load(); }; }
    private void VmChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) { if (e.PropertyName == nameof(MainViewModel.Settings) && !IsKeyboardFocusWithin) Dispatcher.BeginInvoke(Load); }
    private void Load()
    {
        if (_vm is null) return; var s = _vm.Settings;
        Endpoint.Text = s.Endpoint; Model.SelectedItem = s.Model; Temperature.Text = s.Temperature.ToString(CultureInfo.InvariantCulture); ContextSize.Text = s.ContextSize.ToString(); MaxTokens.Text = s.MaxResponseTokens.ToString();
        DesktopControl.IsChecked = s.DesktopControl; UiAutomation.IsChecked = s.UiAutomation; VisualRecognition.IsChecked = s.VisualRecognition; SendImages.IsChecked = s.SendScreenshotsToVisionModel;
        Timeout.Text = s.AutomationTimeoutSeconds.ToString(); MaxSteps.Text = s.MaxSteps.ToString(); ShowProgress.IsChecked = s.ShowProgress; Theme.SelectedIndex = s.Theme == "Midnight" ? 1 : 0;
        Animations.IsChecked = s.Animations; Notifications.IsChecked = s.Notifications; Sounds.IsChecked = s.SoundEffects; Tray.IsChecked = s.SystemTray; ConversationHistory.IsChecked = s.ConversationHistory; TaskHistory.IsChecked = s.TaskHistory;
    }
    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null) return; var s = _vm.Settings;
        s.Endpoint = Endpoint.Text.Trim(); s.Model = Model.SelectedItem as string ?? s.Model;
        if (!double.TryParse(Temperature.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var temp) || !int.TryParse(ContextSize.Text, out var context) || !int.TryParse(MaxTokens.Text, out var tokens) || !int.TryParse(Timeout.Text, out var timeout) || !int.TryParse(MaxSteps.Text, out var steps)) { ((App)System.Windows.Application.Current).SetError("Enter valid numeric values in AI and Automation settings."); return; }
        s.Temperature = temp; s.ContextSize = context; s.MaxResponseTokens = tokens; s.AutomationTimeoutSeconds = timeout; s.MaxSteps = steps;
        s.DesktopControl = DesktopControl.IsChecked == true; s.UiAutomation = UiAutomation.IsChecked == true; s.VisualRecognition = VisualRecognition.IsChecked == true; s.SendScreenshotsToVisionModel = SendImages.IsChecked == true;
        s.ShowProgress = ShowProgress.IsChecked == true; s.Theme = Theme.SelectedIndex == 1 ? "Midnight" : "Charcoal"; s.Animations = Animations.IsChecked == true; s.Notifications = Notifications.IsChecked == true; s.SoundEffects = Sounds.IsChecked == true; s.SystemTray = Tray.IsChecked == true;
        s.ConversationHistory = ConversationHistory.IsChecked == true; s.TaskHistory = TaskHistory.IsChecked == true;
        await _vm.SaveSettingsCommand.ExecuteAsync(s);
    }
}
