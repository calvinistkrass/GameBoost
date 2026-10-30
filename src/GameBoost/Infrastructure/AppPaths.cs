using System.Reflection;

namespace GameBoost.Infrastructure;

/// <summary>Zentrale Pfade und Versionsinfos der Anwendung.</summary>
public static class AppPaths
{
    private static string? _dataDirectory;

    /// <summary>%AppData%\GameBoost (per Umgebungsvariable GAMEBOOST_DATA_DIR überschreibbar, z. B. für Tests).</summary>
    public static string DataDirectory
    {
        get
        {
            if (_dataDirectory != null) return _dataDirectory;
            var overrideDir = Environment.GetEnvironmentVariable("GAMEBOOST_DATA_DIR");
            _dataDirectory = !string.IsNullOrWhiteSpace(overrideDir)
                ? overrideDir
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GameBoost");
            return _dataDirectory;
        }
    }

    public static string SettingsFile => Path.Combine(DataDirectory, "settings.json");
    public static string GameModeStateFile => Path.Combine(DataDirectory, "gamemode-state.json");
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string Version
    {
        get
        {
            var asm = Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (string.IsNullOrEmpty(info)) return asm.GetName().Version?.ToString(3) ?? "1.0.0";
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }
    }

    internal static void OverrideDataDirectory(string path) => _dataDirectory = path;
}
