using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using GameBoost.Infrastructure;

namespace GameBoost.Models;

/// <summary>A game mode profile (e.g. "Fortnite").</summary>
public sealed class GameModeProfile : ObservableObject
{
    private string _name = "Profile";
    private bool _switchPowerPlan = true;
    private bool _disableNotifications = true;
    private bool _restartKilledOnDeactivate = true;
    private bool _showCrosshair;
    private string? _crosshairPresetName;
    private bool _autoDeactivateOnExit = true;

    public string Id { get; set; } = NewId();

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? _name : value.Trim());
    }

    /// <summary>Process names (lowercase, without .exe) closed on activation.</summary>
    public List<string> ProcessesToKill { get; set; } = new();

    /// <summary>Programs started on activation.</summary>
    public ObservableCollection<StartProgram> ProgramsToStart { get; set; } = new();

    /// <summary>Process names (lowercase, without .exe) whose start activates this profile automatically.</summary>
    public ObservableCollection<string> TriggerExecutables { get; set; } = new();

    public bool SwitchPowerPlan
    {
        get => _switchPowerPlan;
        set => SetProperty(ref _switchPowerPlan, value);
    }

    public bool DisableNotifications
    {
        get => _disableNotifications;
        set => SetProperty(ref _disableNotifications, value);
    }

    public bool RestartKilledOnDeactivate
    {
        get => _restartKilledOnDeactivate;
        set => SetProperty(ref _restartKilledOnDeactivate, value);
    }

    public bool ShowCrosshair
    {
        get => _showCrosshair;
        set => SetProperty(ref _showCrosshair, value);
    }

    /// <summary>Optional preset loaded on activation (null = current crosshair).</summary>
    public string? CrosshairPresetName
    {
        get => _crosshairPresetName;
        set => SetProperty(ref _crosshairPresetName, string.IsNullOrWhiteSpace(value) ? null : value);
    }

    public bool AutoDeactivateOnExit
    {
        get => _autoDeactivateOnExit;
        set => SetProperty(ref _autoDeactivateOnExit, value);
    }

    public static string NewId() => Guid.NewGuid().ToString("N");

    public GameModeProfile CloneAsNew(string name) => new()
    {
        Name = name,
        ProcessesToKill = new List<string>(ProcessesToKill),
        ProgramsToStart = new ObservableCollection<StartProgram>(ProgramsToStart.Select(p => new StartProgram { Path = p.Path, Arguments = p.Arguments })),
        TriggerExecutables = new ObservableCollection<string>(),
        SwitchPowerPlan = SwitchPowerPlan,
        DisableNotifications = DisableNotifications,
        RestartKilledOnDeactivate = RestartKilledOnDeactivate,
        ShowCrosshair = ShowCrosshair,
        CrosshairPresetName = CrosshairPresetName,
        AutoDeactivateOnExit = AutoDeactivateOnExit
    };

    internal void Normalize()
    {
        if (string.IsNullOrWhiteSpace(Id)) Id = NewId();
        ProcessesToKill = (ProcessesToKill ?? new())
            .Select(ProcessNames.Normalize)
            .Where(n => n.Length > 0)
            .Distinct()
            .ToList();
        ProgramsToStart ??= new();
        foreach (var bad in ProgramsToStart.Where(p => p == null || string.IsNullOrWhiteSpace(p.Path)).ToList())
            ProgramsToStart.Remove(bad);
        TriggerExecutables ??= new();
        var triggers = TriggerExecutables.Select(ProcessNames.Normalize).Where(n => n.Length > 0).Distinct().ToList();
        TriggerExecutables.Clear();
        foreach (var t in triggers) TriggerExecutables.Add(t);
    }
}

/// <summary>A program started when game mode is activated.</summary>
public sealed class StartProgram
{
    public string Path { get; set; } = "";
    public string Arguments { get; set; } = "";

    [JsonIgnore]
    public string DisplayName => System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>Process name normalization ("C:\x\Discord.EXE" → "discord").</summary>
public static class ProcessNames
{
    public static string Normalize(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var n = name.Trim().Trim('"');
        try
        {
            n = System.IO.Path.GetFileName(n);
        }
        catch (ArgumentException)
        {
            // Invalid path characters – use the name as is.
        }
        if (n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) n = n[..^4];
        return n.Trim().ToLowerInvariant();
    }
}
