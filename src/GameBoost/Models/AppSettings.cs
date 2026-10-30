using System.Collections.ObjectModel;

namespace GameBoost.Models;

/// <summary>Alle persistenten Einstellungen (settings.json).</summary>
public sealed class AppSettings
{
    public int SchemaVersion { get; set; } = 1;
    public string Language { get; set; } = "de";
    public bool StartWithWindows { get; set; }
    public bool StartMinimized { get; set; }
    public bool CloseToTray { get; set; } = true;
    public bool AutoProfileSwitch { get; set; } = true;
    public bool TrayHintShown { get; set; }

    public string? ActiveProfileId { get; set; }
    public ObservableCollection<GameModeProfile> Profiles { get; set; } = new();

    public CrosshairSettings Crosshair { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();

    /// <summary>GUID eines von GameBoost angelegten Höchstleistungs-Plans (falls Windows keinen anbietet).</summary>
    public string? CachedHighPerformanceSchemeGuid { get; set; }

    public static AppSettings CreateDefault()
    {
        var settings = new AppSettings();

        var standard = new GameModeProfile { Name = "Standard" };
        var valorant = new GameModeProfile { Name = "Valorant" };
        valorant.TriggerExecutables.Add("valorant-win64-shipping");
        var fortnite = new GameModeProfile { Name = "Fortnite" };
        fortnite.TriggerExecutables.Add("fortniteclient-win64-shipping");

        settings.Profiles.Add(standard);
        settings.Profiles.Add(valorant);
        settings.Profiles.Add(fortnite);
        settings.ActiveProfileId = standard.Id;

        foreach (var preset in CrosshairSamples.DefaultPresets())
            settings.Crosshair.Presets.Add(preset);
        settings.Crosshair.Current = settings.Crosshair.Presets[0].Config.Clone();
        settings.Crosshair.ActivePresetName = settings.Crosshair.Presets[0].Name;

        return settings;
    }

    /// <summary>Repariert unvollständige oder manuell bearbeitete Einstellungen.</summary>
    public void Normalize()
    {
        if (Language is not ("de" or "en")) Language = "de";

        Profiles ??= new();
        foreach (var bad in Profiles.Where(p => p == null).ToList()) Profiles.Remove(bad);
        foreach (var p in Profiles) p.Normalize();
        var duplicateIds = Profiles.GroupBy(p => p.Id).Where(g => g.Count() > 1).SelectMany(g => g.Skip(1));
        foreach (var dup in duplicateIds.ToList()) dup.Id = GameModeProfile.NewId();
        if (Profiles.Count == 0) Profiles.Add(new GameModeProfile { Name = "Standard" });
        if (Profiles.All(p => p.Id != ActiveProfileId)) ActiveProfileId = Profiles[0].Id;

        Crosshair ??= new();
        Crosshair.Current ??= new CrosshairConfig();
        Crosshair.Presets ??= new();
        foreach (var bad in Crosshair.Presets.Where(p => p == null || p.Config == null).ToList())
            Crosshair.Presets.Remove(bad);

        Hotkeys ??= new();
    }
}

/// <summary>Crosshair-bezogene Einstellungen.</summary>
public sealed class CrosshairSettings
{
    public CrosshairConfig Current { get; set; } = new();
    public ObservableCollection<CrosshairPreset> Presets { get; set; } = new();
    public string? ActivePresetName { get; set; }

    /// <summary>Gerätename des Zielmonitors (z. B. \\.\DISPLAY1). null = Hauptmonitor.</summary>
    public string? MonitorDeviceName { get; set; }

    /// <summary>War das Overlay beim letzten Beenden sichtbar?</summary>
    public bool OverlayVisible { get; set; }
}
