using System.Windows;
using GameBoost.Controls;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.ViewModels;
using GameBoost.Views;

namespace GameBoost;

/// <summary>Command line start options.</summary>
public sealed record StartupOptions(bool Minimized, string? Page)
{
    public static StartupOptions Parse(string[] args)
    {
        var minimized = args.Any(a => a.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        var page = args.FirstOrDefault(a => a.StartsWith("--page=", StringComparison.OrdinalIgnoreCase))?["--page=".Length..];
        return new StartupOptions(minimized, page);
    }
}

/// <summary>
/// Wires all services together (hotkeys, tray, game mode, overlay) and manages the main window.
/// </summary>
public sealed class AppController : IDisposable
{
    private readonly Application _app;
    private MainWindow? _window;
    private bool _overlayShownByGameMode;
    private bool _exiting;

    public AppController(Application app)
    {
        _app = app;
        SettingsService.Load();
        Loc.Apply(Settings.Language);

        GameMode = new GameModeService(SettingsService);
        Overlay = new OverlayService(SettingsService);
        Hotkeys = new HotkeyService();
        Tray = new TrayService();
        Watcher = new ProcessWatcherService(this);
    }

    public SettingsService SettingsService { get; } = new();
    public GameModeService GameMode { get; }
    public OverlayService Overlay { get; }
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
        Overlay.VisibilityChanged += (_, _) => UpdateTray();

        Hotkeys.Initialize();
        Hotkeys.HotkeyPressed += (_, action) => OnHotkey(action);
        Hotkeys.Apply(Settings.Hotkeys);
        HotkeyBox.CaptureStateChanged += (_, capturing) => Hotkeys.Suspended = capturing;

        Tray.Initialize();
        Tray.OpenRequested += (_, _) => ShowMainWindow();
        Tray.ToggleGameModeRequested += async (_, _) => await ToggleGameModeAsync();
        Tray.ToggleCrosshairRequested += (_, _) => ToggleCrosshair();
        Tray.NextPresetRequested += (_, _) => NextPreset();
        Tray.ExitRequested += (_, _) => Exit();
        Loc.LanguageChanged += (_, _) => UpdateTray();
        UpdateTray();

        MainViewModel = new MainViewModel(this);
        if (!string.IsNullOrEmpty(options.Page)) MainViewModel.Navigate(options.Page);

        if (Settings.Crosshair.OverlayVisible) Overlay.Show();
        Watcher.Start();

        if (!(options.Minimized || Settings.StartMinimized)) ShowMainWindow();

        if (recovered) Tray.ShowBalloon(Loc.T("Tray.Recovered.Title"), Loc.T("Tray.Recovered.Text"));
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
                break;
            case HotkeyAction.ToggleGameMode:
                _ = ToggleGameModeAsync();
                break;
            case HotkeyAction.NextPreset:
                NextPreset();
                break;
        }
    }

    /// <summary>Resets all settings to factory defaults.</summary>
    public async Task ResetAllAsync()
    {
        if (GameMode.IsActive) await DeactivateGameModeAsync(manual: true);
        Overlay.Hide();
        _overlayShownByGameMode = false;

        SettingsService.ResetToDefaults();
        Loc.Apply(Settings.Language);
        AutostartService.Sync(Settings.StartWithWindows);
        Hotkeys.Apply(Settings.Hotkeys);
        Overlay.ApplySettings();
        MainViewModel?.Rebuild();
        UpdateTray();
    }

    private void UpdateTray() => Tray.UpdateState(GameMode.IsActive, Overlay.IsVisible, GameMode.ActiveProfile?.Name);

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
        try { Tray.Dispose(); } catch { /* ignore */ }
        try { MainViewModel?.Dispose(); } catch { /* ignore */ }
    }
}
