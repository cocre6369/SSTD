using System.Windows;
using SSTD.Core;
using SSTD.UI.Views;

namespace SSTD.UI.Services;
public sealed class ApprovalBroker : IApprovalBroker
{
    public Task<bool> ApproveAsync(ApprovalRequest request, CancellationToken ct)
    {
        var app = System.Windows.Application.Current;
        if (app is null) return Task.FromResult(false);
        return app.Dispatcher.InvokeAsync(async () =>
        {
            var dialog = new ApprovalWindow(request) { Owner = app.MainWindow };
            using var cancellation = ct.Register(() => dialog.Dispatcher.Invoke(() => { dialog.Cancelled = true; dialog.Close(); }));
            dialog.ShowDialog();
            return !ct.IsCancellationRequested && dialog.DialogResult == true;
        }).Task.Unwrap();
    }
}
