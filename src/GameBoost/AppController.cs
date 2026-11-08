using System.Windows;
using GameBoost.Controls;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Services.Performance;
using GameBoost.Services.SystemCheck;
using GameBoost.ViewModels;
using GameBoost.Views;

namespace GameBoost;

/// <summary>Command line start options.</summary>
public sealed record StartupOptions(bool Minimized, string? Page, int? WaitForPid)
{
    public static StartupOptions Parse(string[] args)
    {
        var minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var page = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))?["--page=".Length..];
        var wait = args.FirstOrDefault(a => a.StartsWith("--after-pid=", StringComparison.OrdinalIgnoreCase))?["--after-pid=".Length..];
        return new StartupOptions(minimized, page, int.TryParse(wait, out var pid) ? pid : null);
    }
}

/// <summary>
/// Wires all services together (hotkeys, tray, game mode, overlays, performance, PC check) and manages the main window.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly Application _app;
    private MainWindow? _window;
    private bool _overlayShownByGameMode;
    private bool _hudShownByGameMode;
    private bool _exiting;

    public AppController(Application app)
    {
        _app = app;
        SettingsService.Load();
        ThemeService.Apply(Settings); // before any window or style is created
        Loc.Apply(Settings.Language);

        GameMode = new GameModeService(SettingsService);
        Overlay = new OverlayService(SettingsService);
        Performance = new PerformanceMonitorService();
        Hud = new HudService(SettingsService, Performance);
        SystemCheck = new SystemCheckService(SettingsService);
        Osd = new OsdService(SettingsService);
        Sessions = new SessionService(Performance);
        Updates = new UpdateService();
        Discord = new DiscordPresenceService();
        Hotkeys = new HotkeyService();
        Tray = new TrayService();
        Watcher = new ProcessWatcherService(this);
    }

    public SettingsService SettingsService { get; } = new();
    public GameModeService GameMode { get; }
    public OverlayService Overlay { get; }
    public PerformanceMonitorService Performance { get; }
    public HudService Hud { get; }
    public SystemCheckService SystemCheck { get; }
    public OsdService Osd { get; }
    public SessionService Sessions { get; }
    public UpdateService Updates { get; }
    public DiscordPresenceService Discord { get; }
    public HotkeyService Hotkeys { get; }
    public TrayService Tray { get; }
    public ProcessWatcherService Watcher { get; }
    public MainViewModel? MainViewModel { get; private set; }

    public AppSettings Settings => SettingsService.Settings;

    /// <summary>Raised when a crosshair preset was applied (e.g. via hotkey).</summary>
    public event EventHandler<CrosshairPreset>? PresetApplied;

    public void Start(StartupOptions options)
    {
        Logger.CleanupOldLogs();

        var recovered = GameMode.RecoverFromPreviousSession();
        AutostartService.Sync(Settings.StartWithWindows);

        GameMode.StateChanged += (_, _) => UpdateTray();
        GameMode.StateChanged += (_, _) => OnGameModeChanged();
        Performance.Updated += OnPerformanceSnapshot;
        ApplyDiscordSettings();
        Overlay.VisibilityChanged += (_, _) => UpdateTray();
        Hud.VisibilityChanged += (_, _) => UpdateTray();

        Hotkeys.Initialize();
        Hotkeys.HotkeyPressed += (_, action) => OnHotkey(action);
        Hotkeys.Apply(Settings.Hotkeys);
        HotkeyBox.CaptureStateChanged += (_, capturing) => Hotkeys.Suspended = capturing;

        Tray.Initialize();
        Tray.OpenRequested += (_, _) => ShowMainWindow();
        Tray.ToggleGameModeRequested += async (_, _) => await ToggleGameModeAsync();
        Tray.ToggleCrosshairRequested += (_, _) => ToggleCrosshair();
        Tray.NextPresetRequested += (_, _) => NextPreset();
        Tray.ToggleHudRequested += (_, _) => ToggleHud();
        Tray.ExitRequested += (_, _) => Exit();
        Loc.LanguageChanged += (_, _) => UpdateTray();
        UpdateTray();

        MainViewModel = new MainViewModel(this);
        if (!string.IsNullOrEmpty(options.Page)) MainViewModel.Navigate(options.Page);

        if (Settings.Crosshair.OverlayVisible) Overlay.Show();
        if (Settings.Hud.Visible) Hud.Show();
        Watcher.Start();

        if (!(options.Minimized || Settings.StartMinimized)) ShowMainWindow();

        if (recovered) Tray.ShowBalloon(Loc.T("Tray.Recovered.Title"), Loc.T("Tray.Recovered.Text"));
        if (Settings.CheckForUpdates) _ = CheckForUpdatesDelayedAsync();
        Logger.Info("GameBoost started");
    }

    // ------------------------------------------------------------------ Window

    public void ShowMainWindow()
    {
        try
        {
            if (_window == null)
            {
                _window = new MainWindow { DataContext = MainViewModel };
                _window.Closing += OnWindowClosing;
                _window.IsVisibleChanged += (_, _) => MainViewModel?.SetWindowVisible(_window.IsVisible);
            }

            if (!_window.IsVisible) _window.Show();
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.Activate();
            _window.Topmost = true;  // reliably bring to front
            _window.Topmost = false;
            _window.Focus();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not show main window", ex);
        }
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_exiting) return;
        if (!Settings.CloseToTray)
        {
            e.Cancel = true;
            _app.Dispatcher.BeginInvoke(Exit);
            return;
        }

        e.Cancel = true;
        _window?.Hide();
        if (!Settings.TrayHintShown)
        {
            Settings.TrayHintShown = true;
            SettingsService.RequestSave();
            Tray.ShowBalloon(Loc.T("Tray.Hint.Title"), Loc.T("Tray.Hint.Text"));
        }
    }

    // ------------------------------------------------------------------ Actions

    public GameModeProfile SelectedProfile =>
        Settings.Profiles.FirstOrDefault(p => p.Id == Settings.ActiveProfileId) ?? Settings.Profiles[0];

    public async Task ToggleGameModeAsync()
    {
        if (GameMode.IsBusy) return;
        if (GameMode.IsActive) await DeactivateGameModeAsync(manual: true);
        else await ActivateGameModeAsync(SelectedProfile, automatic: false);
    }

    public async Task ActivateGameModeAsync(GameModeProfile profile, bool automatic)
    {
        try
        {
            if (!await GameMode.ActivateAsync(profile, automatic)) return;

            if (profile.FreeRamOnActivate)
            {
                var keep = profile.TriggerExecutables.ToList();
                _ = Task.Run(() => MemoryCleaner.Trim(keep));
            }

            if (profile.ShowCrosshair)
            {
                if (profile.CrosshairPresetName != null)
                {
                    var preset = Settings.Crosshair.Presets.FirstOrDefault(p => p.Name == profile.CrosshairPresetName);
                    if (preset != null) ApplyPreset(preset);
                }
                if (!Overlay.IsVisible)
                {
                    Overlay.Show();
                    _overlayShownByGameMode = true;
                }
            }

            if (Settings.Hud.ShowWithGameMode && !Hud.IsVisible)
            {
                Hud.Show();
                _hudShownByGameMode = true;
            }
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not activate game mode", ex);
        }
    }

    public async Task DeactivateGameModeAsync(bool manual)
    {
        try
        {
            if (manual && GameMode.ActivatedAutomatically && GameMode.ActiveProfile != null)
                Watcher.SuppressUntilExit(GameMode.ActiveProfile.Id);

            if (!await GameMode.DeactivateAsync()) return;

            if (_overlayShownByGameMode)
            {
                Overlay.Hide();
                _overlayShownByGameMode = false;
            }

            if (_hudShownByGameMode)
            {
                Hud.Hide();
                _hudShownByGameMode = false;
            }
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not deactivate game mode", ex);
        }
    }

    public void SetCrosshairVisible(bool visible)
    {
        if (visible) Overlay.Show();
        else Overlay.Hide();
        _overlayShownByGameMode = false;
        Settings.Crosshair.OverlayVisible = Overlay.IsVisible;
        SettingsService.RequestSave();
    }

    public void ToggleCrosshair() => SetCrosshairVisible(!Overlay.IsVisible);

    public void SetHudVisible(bool visible)
    {
        if (visible) Hud.Show();
        else Hud.Hide();
        _hudShownByGameMode = false;
        Settings.Hud.Visible = Hud.IsVisible;
        SettingsService.RequestSave();
    }

    public void ToggleHud() => SetHudVisible(!Hud.IsVisible);

    /// <summary>Restarts GameBoost with administrator rights (needed for the FPS counter).</summary>
    public void RestartAsAdministrator()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            var psi = new System.Diagnostics.ProcessStartInfo(exe, $"--page=performance --after-pid={Environment.ProcessId}")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            System.Diagnostics.Process.Start(psi)?.Dispose();
            Exit();
        }
        catch (System.ComponentModel.Win32Exception)
        {
            Logger.Info("Restart as administrator was cancelled by the user");
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not restart as administrator", ex);
        }
    }

    public void NextPreset()
    {
        var presets = Settings.Crosshair.Presets;
        if (presets.Count == 0) return;
        var index = presets.ToList().FindIndex(p => p.Name == Settings.Crosshair.ActivePresetName);
        ApplyPreset(presets[(index + 1) % presets.Count]);
    }

    public void ApplyPreset(CrosshairPreset preset)
    {
        Settings.Crosshair.Current.CopyFrom(preset.Config);
        Settings.Crosshair.ActivePresetName = preset.Name;
        SettingsService.RequestSave();
        Logger.Info($"Crosshair preset applied: {preset.Name}");
        PresetApplied?.Invoke(this, preset);
    }

    private void OnHotkey(HotkeyAction action)
    {
        Logger.Info($"Hotkey: {action}");
        switch (action)
        {
            case HotkeyAction.ToggleCrosshair:
                ToggleCrosshair();
                Osd.Show(Loc.T(Overlay.IsVisible ? "Osd.CrosshairOn" : "Osd.CrosshairOff"));
                break;
            case HotkeyAction.ToggleGameMode:
                _ = ToggleGameModeWithHintAsync();
                break;
            case HotkeyAction.NextPreset:
                NextPreset();
                Osd.Show(Loc.F("Osd.Preset", Settings.Crosshair.ActivePresetName ?? ""));
                break;
            case HotkeyAction.ToggleHud:
                ToggleHud();
                Osd.Show(Loc.T(Hud.IsVisible ? "Osd.HudOn" : "Osd.HudOff"));
                break;
        }
    }

    private async Task ToggleGameModeWithHintAsync()
    {
        await ToggleGameModeAsync();
        Osd.Show(GameMode.IsActive
            ? Loc.F("Osd.GameModeOn", GameMode.ActiveProfile?.Name ?? "")
            : Loc.T("Osd.GameModeOff"));
    }

    // ------------------------------------------------------------------ Sessions, Discord, warnings, updates

    private bool _sessionActive;
    private DateTime _lastTempWarning = DateTime.MinValue;

    private void OnGameModeChanged()
    {
        if (GameMode.IsBusy) return;
        if (GameMode.IsActive && !_sessionActive)
        {
            _sessionActive = true;
            var name = GameMode.ActiveProfile?.Name ?? "";
            Sessions.Start(name);
            Discord.SetActivity(name, Loc.T("Discord.GameModeOn"), GameMode.ActivatedAt ?? DateTime.Now);
        }
        else if (!GameMode.IsActive && _sessionActive)
        {
            _sessionActive = false;
            _ = Task.Run(Sessions.Stop);
            Discord.Clear();
        }
    }

    private void OnPerformanceSnapshot(object? sender, Services.Performance.PerformanceSnapshot s)
    {
        if (s.GpuTemperature is not { } temp) return;
        var hud = Settings.Hud;
        if (!hud.TempWarning || temp < hud.TempWarningLimit) return;
        if (DateTime.Now - _lastTempWarning < TimeSpan.FromMinutes(3)) return;
        _app.Dispatcher.BeginInvoke(() =>
        {
            if (!(Hud.IsVisible || GameMode.IsActive)) return;
            _lastTempWarning = DateTime.Now;
            Logger.Warn($"GPU temperature warning: {temp:0} °C");
            Osd.Show(Loc.F("Osd.TempWarning", temp.ToString("0")), warning: true);
        });
    }

    public void ApplyDiscordSettings()
    {
        Discord.Configure(Settings.Discord.Enabled, Settings.Discord.ClientId);
        if (GameMode.IsActive)
            Discord.SetActivity(GameMode.ActiveProfile?.Name ?? "", Loc.T("Discord.GameModeOn"), GameMode.ActivatedAt ?? DateTime.Now);
    }

    private async Task CheckForUpdatesDelayedAsync()
    {
        await Task.Delay(TimeSpan.FromSeconds(8));
        var update = await Updates.CheckAsync();
        if (update != null) Tray.ShowBalloon(Loc.T("Update.Title"), Loc.F("Update.Text", update.Version));
    }

    /// <summary>Restarts GameBoost (e.g. to apply a new theme).</summary>
    public void Restart()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return;
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, $"--page=settings --after-pid={Environment.ProcessId}") { UseShellExecute = true })?.Dispose();
            Exit();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not restart GameBoost", ex);
        }
    }

    /// <summary>Downloads and starts the setup of a newer version (installed version) or opens the release page (portable).</summary>
    public async Task InstallUpdateAsync(IProgress<double>? progress = null)
    {
        if (Updates.Available is not { } info) return;
        if (!UpdateService.IsInstalled || info.SetupUrl == null)
        {
            UpdateService.OpenReleasePage(info.ReleaseUrl);
            return;
        }
        try
        {
            if (await Updates.DownloadAndInstallAsync(progress)) Exit();
            else UpdateService.OpenReleasePage(info.ReleaseUrl);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Update failed", ex);
        }
    }

    /// <summary>Resets all settings to factory defaults.</summary>
    public async Task ResetAllAsync()
    {
        if (GameMode.IsActive) await DeactivateGameModeAsync(manual: true);
        Overlay.Hide();
        Hud.Hide();
        _overlayShownByGameMode = false;
        _hudShownByGameMode = false;

        SettingsService.ResetToDefaults();
        Loc.Apply(Settings.Language);
        AutostartService.Sync(Settings.StartWithWindows);
        Hotkeys.Apply(Settings.Hotkeys);
        Overlay.ApplySettings();
        Hud.ApplySettings();
        ApplyDiscordSettings();
        MainViewModel?.Rebuild();
        UpdateTray();
    }

    private void UpdateTray() => Tray.UpdateState(GameMode.IsActive, Overlay.IsVisible, Hud.IsVisible, GameMode.ActiveProfile?.Name);

    // ------------------------------------------------------------------ Exit

    public void Exit()
    {
        if (_exiting) return;
        _exiting = true;
        Logger.Info("GameBoost is shutting down");

        try { Watcher.Stop(); } catch (Exception ex) { Logger.Warn("Stopping watcher failed", ex); }
        try { GameMode.DeactivateForShutdown(restartPrograms: true); } catch (Exception ex) { Logger.Warn("Restore failed", ex); }
        try { SettingsService.Save(); } catch (Exception ex) { Logger.Warn("Saving failed", ex); }
        try { _window?.Close(); } catch (Exception ex) { Logger.Warn("Closing window failed", ex); }

        _app.Shutdown();
    }

    /// <summary>Windows is shutting down or the user signs out.</summary>
    public void OnSessionEnding()
    {
        Logger.Info("Windows session ending – restoring state");
        GameMode.DeactivateForShutdown(restartPrograms: false);
        SettingsService.Save();
    }

    public void Dispose()
    {
        try { Watcher.Dispose(); } catch { /* ignore */ }
        try { Hotkeys.Dispose(); } catch { /* ignore */ }
        try { Overlay.Dispose(); } catch { /* ignore */ }
        try { Hud.Dispose(); } catch { /* ignore */ }
        try { Sessions.Dispose(); } catch { /* ignore */ }
        try { Performance.Dispose(); } catch { /* ignore */ }
        try { Osd.Dispose(); } catch { /* ignore */ }
        try { Discord.Dispose(); } catch { /* ignore */ }
        try { Tray.Dispose(); } catch { /* ignore */ }
        try { MainViewModel?.Dispose(); } catch { /* ignore */ }
    }
}
