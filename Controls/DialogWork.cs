using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace nerve_earnings_tracker.Controls;

// Dialogs retain their existing preview/commit workflows. Only the blocking work runs on a worker.
internal sealed class DialogWork(UserControl owner, TextBlock status) : IDisposable
{
    internal bool IsRunning { get; private set; }
    internal bool IsDisposed { get; private set; }

    internal async Task<T> RunAsync<T>(string message, Func<T> operation, bool committing = false)
    {
        if (IsRunning || IsDisposed) throw new InvalidOperationException("This dialog is not available for another operation.");
        IsRunning = true;
        owner.IsEnabled = false;
        status.Text = message;
        status.Visibility = Visibility.Visible;
        var modal = FindModal();
        if (committing && modal != null) modal.IsBusy = true;
        try { return await Task.Run(operation); }
        finally
        {
            IsRunning = false;
            if (!IsDisposed) { owner.IsEnabled = true; status.Visibility = Visibility.Collapsed; }
            if (modal != null && ReferenceEquals(modal.ModalContent, owner)) modal.IsBusy = false;
        }
    }

    private Modal? FindModal()
    {
        DependencyObject? current = owner;
        while (current != null)
        {
            if (current is Modal modal) return modal;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

    public void Dispose() => IsDisposed = true;
}
