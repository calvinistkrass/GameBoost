using GameBoost.Infrastructure;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Autostart über HKCU\...\Run (keine Adminrechte nötig).</summary>
public static class AutostartService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "GameBoost";

    private static string? ExecutablePath => Environment.ProcessPath;

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: false);
            return key?.GetValue(ValueName) is string;
        }
        catch (Exception ex)
        {
            Logger.Warn("Autostart-Status konnte nicht gelesen werden", ex);
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
            if (enabled)
            {
                var path = ExecutablePath;
                if (string.IsNullOrEmpty(path)) return false;
                key.SetValue(ValueName, $"\"{path}\" --minimized");
                Logger.Info($"Autostart aktiviert: {path}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("Autostart deaktiviert");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Autostart konnte nicht geändert werden", ex);
            return false;
        }
    }

    /// <summary>Gleicht den Registry-Eintrag mit der Einstellung ab (aktualisiert z. B. den Pfad nach Verschieben der .exe).</summary>
    public static void Sync(bool desired)
    {
        if (desired) SetEnabled(true);
        else if (IsEnabled()) SetEnabled(false);
    }
}
