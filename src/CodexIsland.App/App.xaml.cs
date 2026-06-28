using System.Diagnostics;
using System.Threading;

namespace CodexIsland.App;

public partial class App : System.Windows.Application
{
    private const string MutexName = @"Local\CodexOpenIsland.SingleInstance";
    private const string ShowEventName = @"Local\CodexOpenIsland.ShowExisting";
    private Mutex? _singleInstanceMutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showEventRegistration;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        var createdNew = false;
        _singleInstanceMutex = new Mutex(true, MutexName, out createdNew);
        if (!createdNew)
        {
            TrySignalExistingInstance();
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showEventRegistration = ThreadPool.RegisterWaitForSingleObject(
            _showEvent,
            static (state, _) => ((App)state!).Dispatcher.BeginInvoke(new Action(((App)state!).BringMainWindowToFront)),
            this,
            -1,
            false);

        base.OnStartup(e);
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _showEventRegistration?.Unregister(null);
        _showEvent?.Dispose();

        if (_singleInstanceMutex is not null)
        {
            try
            {
                _singleInstanceMutex.ReleaseMutex();
            }
            catch
            {
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }

    private static void TrySignalExistingInstance()
    {
        try
        {
            using var showEvent = EventWaitHandle.OpenExisting(ShowEventName);
            showEvent.Set();
        }
        catch
        {
            try
            {
                var current = Process.GetCurrentProcess();
                var existing = Process.GetProcessesByName(current.ProcessName)
                    .FirstOrDefault(process => process.Id != current.Id);
                if (existing is not null)
                {
                    var handle = existing.MainWindowHandle;
                    if (handle != nint.Zero)
                    {
                        NativeMethods.ShowWindow(handle, 9);
                        NativeMethods.SetForegroundWindow(handle);
                    }
                }
            }
            catch
            {
            }
        }
    }

    private void BringMainWindowToFront()
    {
        if (MainWindow is MainWindow window)
        {
            window.PresentFromExternalActivation();
        }
    }

    private static class NativeMethods
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool SetForegroundWindow(nint hWnd);

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        internal static extern bool ShowWindow(nint hWnd, int nCmdShow);
    }
}
