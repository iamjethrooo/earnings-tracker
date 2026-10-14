namespace EarningsTracker.Utilities;

// One gate for update exit only; existing contexts and dialog ownership remain unchanged.
internal sealed class UpdateRestartGuard
{
    internal static UpdateRestartGuard Shared { get; } = new();
    private readonly object sync = new();
    private int active;
    private bool preparing;
    internal event Action? Changed;
    internal bool IsBusy { get { lock (sync) return active != 0 || preparing; } }

    internal IDisposable Enter()
    {
        lock (sync)
        {
            if (preparing) throw new InvalidOperationException("The application is preparing to restart. Retry after restart.");
            active++;
        }
        Changed?.Invoke();
        return new Lease(() => { lock (sync) active--; Changed?.Invoke(); });
    }

    internal IDisposable? TryPrepare()
    {
        lock (sync)
        {
            if (active != 0 || preparing) return null;
            preparing = true;
        }
        Changed?.Invoke();
        return new Lease(() => { lock (sync) preparing = false; Changed?.Invoke(); });
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? callback = release;
        public void Dispose() => Interlocked.Exchange(ref callback, null)?.Invoke();
    }
}
