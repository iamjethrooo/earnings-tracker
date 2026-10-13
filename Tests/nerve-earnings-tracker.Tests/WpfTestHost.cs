using System.Windows;
using System.Windows.Threading;

namespace nerve_earnings_tracker.Tests
{
    // A single STA dispatcher loads real application resources without starting the app or opening windows.
    public sealed class WpfTestHost : IDisposable
    {
        private readonly Thread _thread;
        private readonly Dispatcher _dispatcher;

        public WpfTestHost()
        {
            var ready = new TaskCompletionSource<Dispatcher>();
            _thread = new Thread(() =>
            {
                try
                {
                    var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                    foreach (var file in new[] { "Styles", "Converters" })
                        app.Resources.MergedDictionaries.Add(new ResourceDictionary
                        {
                            Source = new Uri($"/nerve-earnings-tracker;component/Resources/{file}.xaml", UriKind.Relative)
                        });
                    ready.SetResult(Dispatcher.CurrentDispatcher);
                    Dispatcher.Run();
                }
                catch (Exception ex) { ready.TrySetException(ex); }
            }) { IsBackground = true };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();
            _dispatcher = ready.Task.GetAwaiter().GetResult();
        }

        public Task RunAsync(Func<Task> test) => _dispatcher.Invoke(test);

        public void Dispose()
        {
            _dispatcher.InvokeShutdown();
            _thread.Join();
        }
    }
}
