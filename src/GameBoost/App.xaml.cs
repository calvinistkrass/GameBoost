using System.Windows;
using System.Windows.Threading;
using GameBoost.Infrastructure;

namespace GameBoost;

public partial class App : Application
{
    // A separate data folder (GAMEBOOST_DATA_DIR, used for tests) gets its own instance so it can run next to the real app.
    private static readonly string InstanceSuffix = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMEBOOST_DATA_DIR"))
        ? "" : "." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Environment.GetEnvironmentVariable("GAMEBOOST_DATA_DIR")!.ToLowerInvariant())))[..12];
    private static readonly string MutexName = @"Local\GameBoost.SingleInstance" + InstanceSuffix;
    private static readonly string ShowEventName = @"Local\GameBoost.ShowWindow" + InstanceSuffix;

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();
        Logger.Info($"GameBoost {AppPaths.Version} starting (args: {string.Join(' ', e.Args)})");

        // When restarted as administrator, wait until the previous instance has exited.
        var options = StartupOptions.Parse(e.Args);
        if (options.WaitForPid is { } previousPid)
        {
            try
            {
                using var previous = System.Diagnostics.Process.GetProcessById(previousPid);
                previous.WaitForExit(15000);
            }
            catch (ArgumentException)
            {
                // already exited
            }
        }

        // Single instance: a second instance brings the existing window to front and exits.
        _mutex = new Mutex(true, MutexName, out var createdNew);
        if (!createdNew)
        {
            try
            {
                using var existing = EventWaitHandle.OpenExisting(ShowEventName);
                existing.Set();
            }
            catch (Exception ex)
            {
                Logger.Warn("Could not notify running instance", ex);
            }
            _mutex.Dispose();
            _mutex = null;
            Shutdown();
            return;
        }

        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showEvent,
            (_, _) => Dispatcher.BeginInvoke(() => _controller?.ShowMainWindow()),
            null, Timeout.Infinite, executeOnlyOnce: false);

        try
        {
            _controller = new AppController(this);
            _controller.Start(options);
        }
        catch (Exception ex)
        {
            Logger.Error("Fatal error during startup", ex);
            MessageBox.Show($"GameBoost could not be started.\n\n{ex.Message}\n\nLog: {Logger.CurrentLogFile}",
                "GameBoost", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnSessionEnding(SessionEndingCancelEventArgs e)
    {
        try
        {
            _controller?.OnSessionEnding();
        }
        catch (Exception ex)
        {
            Logger.Error("Error at session end", ex);
        }
        base.OnSessionEnding(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _controller?.Dispose();
            _showWait?.Unregister(null);
            _showEvent?.Dispose();
            if (_mutex != null)
            {
                _mutex.ReleaseMutex();
                _mutex.Dispose();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Error during cleanup", ex);
        }
        Logger.Info($"GameBoost exited (code {e.ApplicationExitCode})");
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorReporter.Report("Unhandled exception on UI thread", args.Exception);
            args.Handled = true; // keep the app running
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error($"Unhandled exception (terminating: {args.IsTerminating})", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unobserved task exception", args.Exception);
            args.SetObserved();
        };
    }
}
