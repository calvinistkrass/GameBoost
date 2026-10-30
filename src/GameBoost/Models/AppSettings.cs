using System.Collections.ObjectModel;

namespace GameBoost.Models;

/// <summary>All persisted settings (settings.json).</summary>
public sealed class AppSettings
{
    public const int CurrentSchemaVersion = 3;

    public int SchemaVersion { get; set; } = CurrentSchemaVersion;
    public string Language { get; set; } = "en";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool AutoProfileSwitch { get; set; } = true;
    public bool TrayHintShown { get; set; }

    public string? ActiveProfileId { get; set; }
    public ObservableCollection<GameModeProfile> Profiles { get; set; } = new();

    public CrosshairSettings Crosshair { get; set; } = new();
    public HudSettings Hud { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>Original values of Windows settings changed by the PC check (per check id), used for "Undo".</summary>
    public Dictionary<string, Dictionary<string, string?>> TweakBackups { get; set; } = new();

    /// <summary>GUID of a High performance plan created by GameBoost (if Windows doesn't offer one).</summary>
    public string? CachedHighPerformanceSchemeGuid { get; set; }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();

        var standard = new GameModeProfile { Name = "Default" };
        var fortnite = new GameModeProfile { Name = "Fortnite" };
        fortnite.TriggerExecutables.Add("fortniteclient-win64-shipping");

        settings.Profiles.Add(standard);
        settings.Profiles.Add(fortnite);
        settings.ActiveProfileId = standard.Id;

        foreach (var preset in CrosshairSamples.DefaultPresets())
            settings.Crosshair.Presets.Add(preset);
        settings.Crosshair.Current = settings.Crosshair.Presets[0].Config.Clone();
        settings.Crosshair.ActivePresetName = settings.Crosshair.Presets[0].Name;

        return settings;
    }

    /// <summary>Repairs incomplete or hand-edited settings.</summary>
    public void Normalize()
    {
        Migrate();
        if (Language is not ("de" or "en")) Language = "en";

        Profiles ??= new();
        foreach (var bad in Profiles.Where(p => p == null).ToList()) Profiles.Remove(bad);
        foreach (var p in Profiles) p.Normalize();
        var duplicateIds = Profiles.GroupBy(p => p.Id).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1));
        foreach (var dup in duplicateIds.ToList()) dup.Id = GameModeProfile.NewId();
        if (Profiles.Count == 0) Profiles.Add(new GameModeProfile { Name = "Default" });
        if (Profiles.All(p => p.Id != ActiveProfileId)) ActiveProfileId = Profiles[0].Id;

        Crosshair ??= new();
        Crosshair.Current ??= new CrosshairConfig();
        Crosshair.Presets ??= new();
        foreach (var bad in Crosshair.Presets.Where(p => p == null || p.Config == null).ToList())
            Crosshair.Presets.Remove(bad);

        Hotkeys ??= new();
        Hud ??= new();
        TweakBackups ??= new();
    }

    /// <summary>Schema 1 → 2: English becomes the default language; German default names are renamed.</summary>
    private void Migrate()
    {
        if (SchemaVersion >= CurrentSchemaVersion) return;
        if (SchemaVersion < 2) MigrateToEnglish();
        if (SchemaVersion < 3) RemoveDefaultValorantProfile();
        SchemaVersion = CurrentSchemaVersion;
    }

    /// <summary>Schema 3: the Valorant example profile is no longer shipped. Only removed if the user never changed it.</summary>
    private void RemoveDefaultValorantProfile()
    {
        if (Profiles == null) return;
        var untouched = Profiles.Where(p => p != null && p.Name == "Valorant"
                                              && p.ProcessesToKill.Count == 0 && p.ProgramsToStart.Count == 0
                                              && p.TriggerExecutables.Count == 1
                                              && ProcessNames.Normalize(p.TriggerExecutables[0]) == "valorant-win64-shipping").ToList();
        foreach (var p in untouched) Profiles.Remove(p);
    }

    private void MigrateToEnglish()
    {
        Language = "en";
        var presetNames = new Dictionary<string, string>
        {
            ["Klassisch Grün"] = "Classic Green",
            ["Präzision Cyan"] = "Precision Cyan",
            ["Roter Punkt"] = "Red Dot",
            ["Kreis Weiß"] = "White Circle",
            ["T-Form Gelb"] = "Yellow T"
        };

        foreach (var p in Profiles ?? new())
        {
            if (p == null) continue;
            if (p.Name == "Standard") p.Name = "Default";
            if (p.CrosshairPresetName != null && presetNames.TryGetValue(p.CrosshairPresetName, out var renamedForProfile))
                p.CrosshairPresetName = renamedForProfile;
        }

        if (Crosshair != null)
        {
            foreach (var preset in Crosshair.Presets ?? new())
            {
                if (preset != null && presetNames.TryGetValue(preset.Name, out var renamed)) preset.Name = renamed;
            }
            if (Crosshair.ActivePresetName != null && presetNames.TryGetValue(Crosshair.ActivePresetName, out var active))
                Crosshair.ActivePresetName = active;
        }
    }
}

/// <summary>Crosshair-related settings.</summary>
public sealed class CrosshairSettings
{
    public CrosshairConfig Current { get; set; } = new();
    public ObservableCollection<CrosshairPreset> Presets { get; set; } = new();
    public string? ActivePresetName { get; set; }

    /// <summary>Device name of the target monitor (e.g. \\.\DISPLAY1). null = primary monitor.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>Was the overlay visible when the app last exited?</summary>
    public bool OverlayVisible { get; set; }
}
