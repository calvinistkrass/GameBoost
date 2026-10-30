using GameBoost.Infrastructure;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Autostart via HKCU\...\Run (no admin rights needed).</summary>
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
            Logger.Warn("Could not read autostart state", ex);
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
                Logger.Info($"Autostart enabled: {path}");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                Logger.Info("Autostart disabled");
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not change autostart", ex);
            return false;
        }
    }

    /// <summary>Syncs the registry entry with the setting (e.g. updates the path after the .exe was moved).</summary>
    public static void Sync(bool desired)
    {
        if (desired) SetEnabled(true);
        else if (IsEnabled()) SetEnabled(false);
    }
}
