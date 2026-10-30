using GameBoost.Infrastructure;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Turns Windows notifications (toasts) off and back on for the current user.</summary>
public static class NotificationManager
{
    private static readonly (string KeyPath, string ValueName)[] Values =
    {
        (@"Software\Microsoft\Windows\CurrentVersion\PushNotifications", "ToastEnabled"),
        (@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings", "NOC_GLOBAL_SETTING_TOASTS_ENABLED")
    };

    private static string Id((string KeyPath, string ValueName) v) => v.KeyPath + "|" + v.ValueName;

    /// <summary>Disables toasts and returns the previous values (null = value did not exist).</summary>
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
                previous[Id(v)] = old; // only remember values that were actually changed
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
            Logger.Warn($"Could not write registry value {keyPath}\\{name}", ex);
            throw;
        }
    }
}
