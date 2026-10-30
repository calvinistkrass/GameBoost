using GameBoost.Infrastructure;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Schaltet Windows-Benachrichtigungen (Toasts) für den aktuellen Benutzer ab und wieder an.</summary>
public static class NotificationManager
{
    private static readonly (string KeyPath, string ValueName)[] Values =
    {
        (@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled"),
        (@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED")
    };

    private static string Id((string KeyPath, string ValueName) v) => v.KeyPath + "|" + v.ValueName;

    /// <summary>Deaktiviert Toasts und liefert die vorherigen Werte (null = Wert existierte nicht).</summary>
    public static Dictionary<string, int?> Disable()
    {
        var previous = new Dictionary<string, int?>();
        Exception? firstError = null;
        foreach (var v in Values)
        {
            try
            {
                var old = Read(v.KeyPath, v.ValueName);
                Write(v.KeyPath, v.ValueName, 0);
                previous[Id(v)] = old; // nur tatsächlich geänderte Werte merken
            }
            catch (Exception ex)
            {
                firstError ??= ex;
            }
        }
        if (previous.Count == 0 && firstError != null) throw firstError;
        return previous;
    }

    public static void Restore(Dictionary<string, int?> previous)
    {
        foreach (var v in Values)
        {
            if (!previous.TryGetValue(Id(v), out var value)) continue;
            Write(v.KeyPath, v.ValueName, value);
        }
    }

    private static int? Read(string keyPath, string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
        return key?.GetValue(name) is int i ? i : null;
    }

    private static void Write(string keyPath, string name, int? value)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath, writable: true);
            if (value.HasValue)
                key.SetValue(name, value.Value, RegistryValueKind.DWord);
            else
                key.DeleteValue(name, throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            Logger.Warn($"Registry-Wert {keyPath}\\{name} konnte nicht geschrieben werden", ex);
            throw;
        }
    }
}
