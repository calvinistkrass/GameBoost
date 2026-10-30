using System.Windows;
using GameBoost.Views;

namespace GameBoost.Infrastructure;

/// <summary>Logs errors and shows the user a (throttled) message.</summary>
public static class ErrorReporter
{
    private static DateTime _lastShown = DateTime.MinValue;

    public static void Report(string context, Exception ex, bool showToUser = true)
    {
        Logger.Error(context, ex);
        if (!showToUser) return;

        try
        {
            var app = Application.Current;
            if (app == null) return;
            if (DateTime.Now - _lastShown < TimeSpan.FromSeconds(5)) return;
            _lastShown = DateTime.Now;

            app.Dispatcher.BeginInvoke(() =>
            {
                try
                {
                    Dialogs.Error($"{Loc.T("Err.Generic")}\n\n{ex.Message}");
                }
                catch (Exception inner)
                {
                    Logger.Error("Could not show error dialog", inner);
                }
            });
        }
        catch
        {
            // Never crash from within error handling.
        }
    }
}
