using System.Windows;
using System.Windows.Threading;
using GameBoost.Infrastructure;

namespace GameBoost;

public partial class App : Application
{
    private const string MutexName = @"Local\GameBoost.SingleInstance";
    private const string ShowEventName = @"Local\GameBoost.ShowWindow";

    private Mutex? _mutex;
    private EventWaitHandle? _showEvent;
    private RegisteredWaitHandle? _showWait;
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalExceptionHandlers();
        Logger.Info($"GameBoost {AppPaths.Version} startet (Argumente: {string.Join(' ', e.Args)})");

        // Nur eine Instanz: eine zweite Instanz holt das vorhandene Fenster nach vorne und beendet sich.
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
                Logger.Warn("Laufende Instanz konnte nicht benachrichtigt werden", ex);
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
            _controller.Start(StartupOptions.Parse(e.Args));
        }
        catch (Exception ex)
        {
            Logger.Error("Schwerer Fehler beim Start", ex);
            MessageBox.Show($"GameBoost konnte nicht gestartet werden.\n\n{ex.Message}\n\nLog: {Logger.CurrentLogFile}",
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
            Logger.Error("Fehler beim Sitzungsende", ex);
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
            Logger.Warn("Fehler beim Aufräumen", ex);
        }
        Logger.Info($"GameBoost beendet (Code {e.ApplicationExitCode})");
        base.OnExit(e);
    }

    private void RegisterGlobalExceptionHandlers()
    {
        DispatcherUnhandledException += (_, args) =>
        {
            ErrorReporter.Report("Unbehandelte Ausnahme im UI-Thread", args.Exception);
            args.Handled = true; // App läuft weiter
        };

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            Logger.Error($"Unbehandelte Ausnahme (beendet: {args.IsTerminating})", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            Logger.Error("Unbeobachtete Task-Ausnahme", args.Exception);
            args.SetObserved();
        };
    }
}
