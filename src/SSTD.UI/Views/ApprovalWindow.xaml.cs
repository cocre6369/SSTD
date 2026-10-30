using System.Windows;
using SSTD.Core;
namespace SSTD.UI.Views;
public partial class ApprovalWindow : Window
{
    public bool Cancelled { get; set; }
    public ApprovalWindow(ApprovalRequest request)
    {
        InitializeComponent(); ActionText.Text = request.Action; TargetText.Text = request.Target; Details.Text = request.Details;
        PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { Cancelled = true; Close(); } };
    }
    private void Cancel_Click(object sender, RoutedEventArgs e) { Cancelled = true; DialogResult = false; Close(); }
    private void Approve_Click(object sender, RoutedEventArgs e) { DialogResult = true; Close(); }
}
