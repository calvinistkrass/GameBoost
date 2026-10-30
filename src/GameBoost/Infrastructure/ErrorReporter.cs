using System.Windows;
using GameBoost.Views;

namespace GameBoost.Infrastructure;

/// <summary>Protokolliert Fehler und zeigt dem Nutzer (gedrosselt) einen Hinweis an.</summary>
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
                    Logger.Error("Fehlerdialog konnte nicht angezeigt werden", inner);
                }
            });
        }
        catch
        {
            // Niemals aus der Fehlerbehandlung heraus abstürzen.
        }
    }
}
