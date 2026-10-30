using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>Loads and saves the settings as JSON in %AppData%\GameBoost\settings.json.</summary>
public sealed class SettingsService
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly object _fileGate = new();
    private DispatcherTimer? _saveTimer;

    public AppSettings Settings { get; private set; } = AppSettings.CreateDefault();

    public event EventHandler? SettingsReplaced;

    public void Load()
    {
        var path = AppPaths.SettingsFile;
        try
        {
            if (!File.Exists(path))
            {
                Logger.Info("No settings found – using defaults.");
                Settings = AppSettings.CreateDefault();
                Save();
                return;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                         ?? throw new JsonException("Empty settings file");
            loaded.Normalize();
            Settings = loaded;
            Logger.Info("Settings loaded.");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not read settings – using defaults.", ex);
            BackupCorruptFile(path);
            Settings = AppSettings.CreateDefault();
            Save();
        }
    }

    /// <summary>Saves immediately (atomically via a temp file).</summary>
    public void Save()
    {
        try
        {
            _saveTimer?.Stop();
            var json = JsonSerializer.Serialize(Settings, JsonOptions);
            lock (_fileGate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var target = AppPaths.SettingsFile;
                var temp = target + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, target, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save settings", ex);
        }
    }

    /// <summary>Saves with a delay (batches many quick changes, e.g. while dragging a slider).</summary>
    public void RequestSave()
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null)
        {
            Save();
            return;
        }

        if (_saveTimer == null)
        {
            _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(500), DispatcherPriority.Background, (_, _) => Save(), dispatcher);
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void ResetToDefaults()
    {
        var path = AppPaths.SettingsFile;
        BackupCorruptFile(path, "before-reset");
        Settings = AppSettings.CreateDefault();
        Save();
        Logger.Info("Settings were reset.");
        SettingsReplaced?.Invoke(this, EventArgs.Empty);
    }

    private static void BackupCorruptFile(string path, string suffix = "corrupt")
    {
        try
        {
            if (!File.Exists(path)) return;
            var backup = Path.Combine(Path.GetDirectoryName(path)!, $"settings.{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(path, backup, overwrite: true);
            Logger.Info($"Backup of old settings: {backup}");
        }
        catch (Exception ex)
        {
            Logger.Warn("Backing up settings file failed", ex);
        }
    }
}
