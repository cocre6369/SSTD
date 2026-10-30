using System.Windows;
namespace SSTD.TestApp;
public partial class MainWindow : Window
{
    public MainWindow() { InitializeComponent(); if(Environment.GetCommandLineArgs().Contains("--no-password")) ProtectedField.Visibility=Visibility.Collapsed; Library.ItemsSource = Enumerable.Range(1,300).Select(i => i == 273 ? "Minecraft launcher" : "Library item " + i).ToList(); }
    private void Apply_Click(object sender, RoutedEventArgs e) { ResultText.Text = "Applied successfully"; }
}
