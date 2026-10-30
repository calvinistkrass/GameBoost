using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Threading;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>Lädt und speichert die Einstellungen als JSON in %AppData%\GameBoost\settings.json.</summary>
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
                Logger.Info("Keine Einstellungen gefunden – Standardwerte werden verwendet.");
                Settings = AppSettings.CreateDefault();
                Save();
                return;
            }

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions)
                         ?? throw new JsonException("Leere Einstellungsdatei");
            loaded.Normalize();
            Settings = loaded;
            Logger.Info("Einstellungen geladen.");
        }
        catch (Exception ex)
        {
            Logger.Error("Einstellungen konnten nicht gelesen werden – Standardwerte werden verwendet.", ex);
            BackupCorruptFile(path);
            Settings = AppSettings.CreateDefault();
            Save();
        }
    }

    /// <summary>Speichert sofort (atomar über eine temporäre Datei).</summary>
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
            Logger.Error("Einstellungen konnten nicht gespeichert werden", ex);
        }
    }

    /// <summary>Speichert verzögert (bündelt viele schnelle Änderungen, z. B. beim Ziehen eines Sliders).</summary>
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
        Logger.Info("Einstellungen wurden zurückgesetzt.");
        SettingsReplaced?.Invoke(this, EventArgs.Empty);
    }

    private static void BackupCorruptFile(string path, string suffix = "corrupt")
    {
        try
        {
            if (!File.Exists(path)) return;
            var backup = Path.Combine(Path.GetDirectoryName(path)!, $"settings.{suffix}-{DateTime.Now:yyyyMMdd-HHmmss}.json");
            File.Copy(path, backup, overwrite: true);
            Logger.Info($"Sicherung der alten Einstellungen: {backup}");
        }
        catch (Exception ex)
        {
            Logger.Warn("Sicherung der Einstellungsdatei fehlgeschlagen", ex);
        }
    }
}
