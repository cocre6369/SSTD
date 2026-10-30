using System.Windows;
using SSTD.Core;

namespace SSTD.UI.Services;
public static class DialogService
{
    public static void ShowSettingsSaved() => System.Windows.MessageBox.Show("Settings saved.", "SSTD", MessageBoxButton.OK, MessageBoxImage.Information);
}
