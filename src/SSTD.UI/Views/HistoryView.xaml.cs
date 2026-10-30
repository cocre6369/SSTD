using System.Windows.Controls;
using SSTD.Core;
using SSTD.UI.ViewModels;
namespace SSTD.UI.Views;
public partial class HistoryView : System.Windows.Controls.UserControl
{
    public HistoryView() { InitializeComponent(); }
    private void Task_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (DataContext is MainViewModel vm && e.AddedItems.Count > 0) vm.InspectTaskCommand.Execute(e.AddedItems[0] as TaskRecord); }
}
