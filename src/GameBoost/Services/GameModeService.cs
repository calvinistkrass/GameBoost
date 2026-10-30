using System.Diagnostics;
using System.Text.Json;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>
/// Aktiviert/deaktiviert den Gaming-Modus: Programme beenden/starten, Energieplan, Benachrichtigungen.
/// Der Zustand wird persistiert, damit nach einem Absturz alles wiederhergestellt werden kann.
/// </summary>
public sealed class GameModeService
{
    private const string BalancedGuid = "381b4222-f694-41f0-9685-ff5bb260df2e";

    private readonly SettingsService _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateFileGate = new();
    private GameModeState? _state;
    private IReadOnlyList<StatusEntry> _entries = Array.Empty<StatusEntry>();

    public GameModeService(SettingsService settings)
    {
        _settings = settings;
    }

    public bool IsActive => _state?.IsActive == true;
    public bool IsBusy { get; private set; }
    public bool ActivatedAutomatically => _state?.Automatic == true;
    public GameModeProfile? ActiveProfile { get; private set; }
    public DateTime? ActivatedAt => _state?.ActivatedAt;
    public IReadOnlyList<StatusEntry> StatusEntries => _entries;

    public event EventHandler? StateChanged;

    /// <summary>Unveränderliche Momentaufnahme eines Profils für die Ausführung im Hintergrund.</summary>
    private sealed record ActivationPlan(
        string ProfileName,
        bool SwitchPowerPlan,
        bool DisableNotifications,
        IReadOnlyList<string> Kill,
        IReadOnlyList<StartProgram> Start);

    public async Task<bool> ActivateAsync(GameModeProfile profile, bool automatic)
    {
        if (IsActive || !await _gate.WaitAsync(0)) return false;

        IsBusy = true;
        RaiseStateChanged();

        var plan = new ActivationPlan(
            profile.Name,
            profile.SwitchPowerPlan,
            profile.DisableNotifications,
            profile.ProcessesToKill.ToList(),
            profile.ProgramsToStart.Select(p => new StartProgram { Path = p.Path, Arguments = p.Arguments }).ToList());

        var state = new GameModeState
        {
            IsActive = true,
            ProfileId = profile.Id,
            ProfileName = profile.Name,
            ActivatedAt = DateTime.Now,
            Automatic = automatic
        };
        var entries = new List<StatusEntry>();
        var cachedGuid = _settings.Settings.CachedHighPerformanceSchemeGuid;
        string? createdGuid = null;

        try
        {
            Logger.Info($"Gaming-Modus wird aktiviert (Profil '{profile.Name}', automatisch: {automatic})");
            createdGuid = await Task.Run(() => ActivateCore(plan, state, entries, cachedGuid));
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler beim Aktivieren des Gaming-Modus", ex);
            entries.Add(new StatusEntry(StatusKind.Error, Loc.T("GMS.Unexpected")));
        }
        finally
        {
            if (createdGuid != null)
            {
                _settings.Settings.CachedHighPerformanceSchemeGuid = createdGuid;
                _settings.RequestSave();
            }

            // Auch bei Teilfehlern als aktiv markieren, damit "Deaktivieren" alles Geänderte zurücksetzt.
            _state = state;
            ActiveProfile = profile;
            _entries = entries;
            IsBusy = false;
            _gate.Release();
            RaiseStateChanged();
        }

        return true;
    }

    public async Task<bool> DeactivateAsync()
    {
        if (!IsActive || !await _gate.WaitAsync(0)) return false;

        IsBusy = true;
        RaiseStateChanged();

        var state = _state!;
        var restart = ActiveProfile?.RestartKilledOnDeactivate ?? true;
        var entries = new List<StatusEntry>();

        try
        {
            Logger.Info("Gaming-Modus wird deaktiviert");
            await Task.Run(() => RestoreCore(state, restart, entries));
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler beim Deaktivieren des Gaming-Modus", ex);
        }
        finally
        {
            _state = null;
            ActiveProfile = null;
            _entries = Array.Empty<StatusEntry>();
            IsBusy = false;
            _gate.Release();
            RaiseStateChanged();
        }

        return true;
    }

    /// <summary>Synchrones Zurücksetzen beim Beenden der App oder beim Abmelden von Windows.</summary>
    public void DeactivateForShutdown(bool restartPrograms)
    {
        if (!IsActive || IsBusy) return;
        try
        {
            var restart = restartPrograms && (ActiveProfile?.RestartKilledOnDeactivate ?? true);
            RestoreCore(_state!, restart, new List<StatusEntry>());
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler beim Zurücksetzen während des Beendens", ex);
        }
        _state = null;
        ActiveProfile = null;
    }

    /// <summary>Stellt nach einem Absturz den ursprünglichen Zustand wieder her. Liefert true, wenn etwas zu tun war.</summary>
    public bool RecoverFromPreviousSession()
    {
        try
        {
            var state = LoadState();
            if (state == null) return false;
            if (!state.IsActive)
            {
                DeleteState();
                return false;
            }

            Logger.Warn($"Gaming-Modus war beim letzten Beenden noch aktiv (Profil '{state.ProfileName}') – stelle wieder her.");
            RestoreCore(state, restartPrograms: false, new List<StatusEntry>());
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error("Wiederherstellung nach Absturz fehlgeschlagen", ex);
            DeleteState();
            return false;
        }
    }

    // ------------------------------------------------------------------ Kernlogik (Hintergrundthread)

    private string? ActivateCore(ActivationPlan plan, GameModeState state, List<StatusEntry> entries, string? cachedGuid)
    {
        string? createdGuid = null;
        SaveState(state);

        if (plan.SwitchPowerPlan)
        {
            var previous = PowerPlanManager.GetActiveScheme();
            var target = PowerPlanManager.EnsureHighPerformanceScheme(cachedGuid, out createdGuid);

            if (target != null)
            {
                if (string.Equals(previous, target, StringComparison.OrdinalIgnoreCase))
                {
                    entries.Add(new StatusEntry(StatusKind.Info, Loc.T("GMS.PowerAlready")));
                }
                else if (PowerPlanManager.SetActive(target))
                {
                    state.PowerSchemeChanged = true;
                    state.PreviousPowerScheme = previous ?? BalancedGuid;
                    entries.Add(new StatusEntry(StatusKind.Ok, Loc.T("GMS.PowerSet")));
                    Logger.Info($"Energieplan {previous} → {target}");
                }
                else
                {
                    entries.Add(new StatusEntry(StatusKind.Warning, Loc.T("GMS.PowerFailed")));
                }
            }
            else if (PowerPlanManager.TrySetBestPerformanceOverlay(out var previousOverlay))
            {
                state.PowerOverlayChanged = true;
                state.PreviousPowerOverlay = previousOverlay;
                entries.Add(new StatusEntry(StatusKind.Ok, Loc.T("GMS.PowerOverlay")));
                Logger.Info($"Energiemodus-Overlay {previousOverlay} → Beste Leistung");
            }
            else
            {
                entries.Add(new StatusEntry(StatusKind.Warning, Loc.T("GMS.PowerFailed")));
            }
            SaveState(state);
        }

        if (plan.DisableNotifications)
        {
            try
            {
                state.PreviousNotificationValues = NotificationManager.Disable();
                state.NotificationsChanged = true;
                entries.Add(new StatusEntry(StatusKind.Ok, Loc.T("GMS.NotifOff")));
            }
            catch (Exception ex)
            {
                Logger.Warn("Benachrichtigungen konnten nicht deaktiviert werden", ex);
                entries.Add(new StatusEntry(StatusKind.Warning, Loc.T("GMS.NotifFailed")));
            }
            SaveState(state);
        }

        if (plan.Kill.Count > 0)
        {
            KillProcesses(plan.Kill, state, entries);
            SaveState(state);
        }

        StartPrograms(plan.Start, entries);
        return createdGuid;
    }

    private static void KillProcesses(IReadOnlyList<string> names, GameModeState state, List<StatusEntry> entries)
    {
        foreach (var name in names)
        {
            Process[] processes;
            try
            {
                processes = Process.GetProcessesByName(name);
            }
            catch (Exception ex)
            {
                Logger.Warn($"Prozesse '{name}' konnten nicht gelesen werden", ex);
                continue;
            }

            try
            {
                if (processes.Length == 0)
                {
                    entries.Add(new StatusEntry(StatusKind.Info, Loc.F("GMS.KillNotRunning", name)));
                    continue;
                }

                var allowed = new List<Process>();
                string? path = null;
                string display = processes[0].ProcessName;
                foreach (var p in processes)
                {
                    var check = ProcessGuard.Check(p);
                    if (check.Allowed)
                    {
                        allowed.Add(p);
                        path ??= check.Path;
                    }
                    else
                    {
                        Logger.Info($"Prozess {p.ProcessName} ({p.Id}) übersprungen: {check.Reason}");
                    }
                }

                if (allowed.Count == 0)
                {
                    entries.Add(new StatusEntry(StatusKind.Warning, Loc.F("GMS.KillProtected", display)));
                    continue;
                }

                // Erst höflich schließen, dann hart beenden.
                foreach (var p in allowed)
                {
                    try { p.CloseMainWindow(); } catch { /* kein Fenster */ }
                }

                var deadline = DateTime.UtcNow.AddMilliseconds(1500);
                foreach (var p in allowed)
                {
                    try
                    {
                        var remaining = (int)Math.Max(0, (deadline - DateTime.UtcNow).TotalMilliseconds);
                        if (!p.HasExited) p.WaitForExit(remaining);
                    }
                    catch { /* ignorieren */ }
                }

                var failed = 0;
                foreach (var p in allowed)
                {
                    try
                    {
                        if (p.HasExited) continue;
                        p.Kill(entireProcessTree: false);
                        p.WaitForExit(3000);
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Logger.Warn($"{p.ProcessName} ({p.Id}) konnte nicht beendet werden: {ex.Message}");
                    }
                }

                if (failed < allowed.Count)
                {
                    if (path != null && !state.KilledPrograms.Contains(path, StringComparer.OrdinalIgnoreCase))
                        state.KilledPrograms.Add(path);
                    entries.Add(new StatusEntry(StatusKind.Ok, Loc.F("GMS.Killed", display)));
                    Logger.Info($"{display} beendet ({allowed.Count - failed}/{allowed.Count} Instanzen)");
                }
                else
                {
                    entries.Add(new StatusEntry(StatusKind.Warning, Loc.F("GMS.KillFailed", display)));
                }
            }
            finally
            {
                foreach (var p in processes) p.Dispose();
            }
        }
    }

    private static void StartPrograms(IReadOnlyList<StartProgram> programs, List<StatusEntry> entries)
    {
        foreach (var program in programs)
        {
            var display = program.DisplayName;
            try
            {
                if (!File.Exists(program.Path))
                {
                    entries.Add(new StatusEntry(StatusKind.Warning, Loc.F("GMS.StartMissing", display)));
                    continue;
                }

                if (program.Path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
                    ProcessService.IsRunning(ProcessNames.Normalize(program.Path)))
                {
                    entries.Add(new StatusEntry(StatusKind.Info, Loc.F("GMS.StartAlreadyRunning", display)));
                    continue;
                }

                StartDetached(program.Path, program.Arguments);
                entries.Add(new StatusEntry(StatusKind.Ok, Loc.F("GMS.Started", display)));
                Logger.Info($"Programm gestartet: {program.Path}");
            }
            catch (Exception ex)
            {
                Logger.Warn($"Programm konnte nicht gestartet werden: {program.Path}", ex);
                entries.Add(new StatusEntry(StatusKind.Warning, Loc.F("GMS.StartFailed", display)));
            }
        }
    }

    private void RestoreCore(GameModeState state, bool restartPrograms, List<StatusEntry> entries)
    {
        if (state.PowerSchemeChanged)
        {
            var target = state.PreviousPowerScheme ?? BalancedGuid;
            if (!PowerPlanManager.SetActive(target)) PowerPlanManager.SetActive(BalancedGuid);
            Logger.Info($"Energieplan wiederhergestellt: {target}");
        }

        if (state.PowerOverlayChanged)
        {
            PowerPlanManager.RestoreOverlay(state.PreviousPowerOverlay);
            Logger.Info("Energiemodus-Overlay wiederhergestellt");
        }

        if (state.NotificationsChanged)
        {
            try
            {
                NotificationManager.Restore(state.PreviousNotificationValues);
                Logger.Info("Benachrichtigungen wiederhergestellt");
            }
            catch (Exception ex)
            {
                Logger.Warn("Benachrichtigungen konnten nicht wiederhergestellt werden", ex);
            }
        }

        if (restartPrograms)
        {
            foreach (var path in state.KilledPrograms)
            {
                try
                {
                    if (!File.Exists(path) || ProcessService.IsRunning(ProcessNames.Normalize(path))) continue;
                    StartDetached(path, "");
                    entries.Add(new StatusEntry(StatusKind.Ok, Loc.F("GMS.Started", Path.GetFileNameWithoutExtension(path))));
                    Logger.Info($"Beendetes Programm neu gestartet: {path}");
                }
                catch (Exception ex)
                {
                    Logger.Warn($"Neustart fehlgeschlagen: {path}", ex);
                }
            }
        }

        DeleteState();
    }

    private static void StartDetached(string path, string arguments)
    {
        var psi = new ProcessStartInfo(path)
        {
            Arguments = arguments ?? "",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? ""
        };
        Process.Start(psi)?.Dispose();
    }

    // ------------------------------------------------------------------ Persistenz

    private void SaveState(GameModeState state)
    {
        try
        {
            lock (_stateFileGate)
            {
                Directory.CreateDirectory(AppPaths.DataDirectory);
                var temp = AppPaths.GameModeStateFile + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(state, SettingsService.JsonOptions));
                File.Move(temp, AppPaths.GameModeStateFile, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Gaming-Modus-Zustand konnte nicht gespeichert werden", ex);
        }
    }

    private GameModeState? LoadState()
    {
        lock (_stateFileGate)
        {
            if (!File.Exists(AppPaths.GameModeStateFile)) return null;
            return JsonSerializer.Deserialize<GameModeState>(File.ReadAllText(AppPaths.GameModeStateFile), SettingsService.JsonOptions);
        }
    }

    private void DeleteState()
    {
        try
        {
            lock (_stateFileGate)
            {
                if (File.Exists(AppPaths.GameModeStateFile)) File.Delete(AppPaths.GameModeStateFile);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn("Gaming-Modus-Zustand konnte nicht gelöscht werden", ex);
        }
    }

    private void RaiseStateChanged()
    {
        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Fehler in StateChanged-Handler", ex);
        }
    }
}
