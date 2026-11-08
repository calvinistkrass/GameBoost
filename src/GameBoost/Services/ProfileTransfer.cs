using System.Collections.ObjectModel;
using System.Text.Json;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>Exports and imports game mode profiles as .gbprofile files (JSON) to share them.</summary>
public static class ProfileTransfer
{
    public const string Extension = ".gbprofile";

    private sealed class ProfileFile
    {
        public string Format { get; set; } = "GameBoostProfile";
        public int Version { get; set; } = 1;
        public string Name { get; set; } = "";
        public List<string> ProcessesToKill { get; set; } = new();
        public List<StartProgram> ProgramsToStart { get; set; } = new();
        public List<string> TriggerExecutables { get; set; } = new();
        public bool SwitchPowerPlan { get; set; }
        public bool DisableNotifications { get; set; }
        public bool RestartKilledOnDeactivate { get; set; }
        public bool ShowCrosshair { get; set; }
        public string? CrosshairPresetName { get; set; }
        public bool AutoDeactivateOnExit { get; set; }
        public bool FreeRamOnActivate { get; set; }
    }

    public static void Export(GameModeProfile p, string path)
    {
        var file = new ProfileFile
        {
            Name = p.Name,
            ProcessesToKill = p.ProcessesToKill.ToList(),
            ProgramsToStart = p.ProgramsToStart.ToList(),
            TriggerExecutables = p.TriggerExecutables.ToList(),
            SwitchPowerPlan = p.SwitchPowerPlan,
            DisableNotifications = p.DisableNotifications,
            RestartKilledOnDeactivate = p.RestartKilledOnDeactivate,
            ShowCrosshair = p.ShowCrosshair,
            CrosshairPresetName = p.CrosshairPresetName,
            AutoDeactivateOnExit = p.AutoDeactivateOnExit,
            FreeRamOnActivate = p.FreeRamOnActivate
        };
        File.WriteAllText(path, JsonSerializer.Serialize(file, SettingsService.JsonOptions));
        Logger.Info($"Profile exported: {p.Name} → {path}");
    }

    /// <summary>Reads a profile file. Returns null if the file is not a valid GameBoost profile.</summary>
    public static GameModeProfile? Import(string path, IEnumerable<string> existingNames)
    {
        try
        {
            var file = JsonSerializer.Deserialize<ProfileFile>(File.ReadAllText(path), SettingsService.JsonOptions);
            if (file == null || file.Format != "GameBoostProfile" || string.IsNullOrWhiteSpace(file.Name)) return null;

            var names = new HashSet<string>(existingNames, StringComparer.CurrentCultureIgnoreCase);
            var name = file.Name.Trim();
            var candidate = name;
            for (var i = 2; names.Contains(candidate); i++) candidate = $"{name} ({i})";

            var profile = new GameModeProfile
            {
                Name = candidate,
                ProcessesToKill = file.ProcessesToKill ?? new(),
                ProgramsToStart = new ObservableCollection<StartProgram>(file.ProgramsToStart ?? new()),
                TriggerExecutables = new ObservableCollection<string>(file.TriggerExecutables ?? new()),
                SwitchPowerPlan = file.SwitchPowerPlan,
                DisableNotifications = file.DisableNotifications,
                RestartKilledOnDeactivate = file.RestartKilledOnDeactivate,
                ShowCrosshair = file.ShowCrosshair,
                CrosshairPresetName = file.CrosshairPresetName,
                AutoDeactivateOnExit = file.AutoDeactivateOnExit,
                FreeRamOnActivate = file.FreeRamOnActivate
            };
            profile.Normalize();
            // Never import protected processes as "close" targets, even if someone edited the file.
            profile.ProcessesToKill.RemoveAll(ProcessGuard.IsProtectedName);
            Logger.Info($"Profile imported: {profile.Name} ← {path}");
            return profile;
        }
        catch (Exception ex)
        {
            Logger.Warn($"Profile could not be imported: {path}", ex);
            return null;
        }
    }
}
