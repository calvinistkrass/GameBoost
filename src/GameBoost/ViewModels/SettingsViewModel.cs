using System.Diagnostics;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Views;

namespace GameBoost.ViewModels;

public sealed record LanguageOption(string Code, string Name);

public sealed record AccentOption(string Hex, string NameKey)
{
    public string Name => Loc.T(NameKey);
}

/// <summary>"Settings" page.</summary>
public sealed class SettingsViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;

    public SettingsViewModel(AppController controller)
    {
        _controller = controller;
        _controller.Hotkeys.StatesChanged += OnHotkeyStatesChanged;
        Loc.LanguageChanged += OnLanguageChanged;

        OpenDataFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.DataDirectory));
        OpenLogFolderCommand = new RelayCommand(() => OpenFolder(AppPaths.LogDirectory));
        ResetCommand = new AsyncRelayCommand(ResetAsync);
        ResetHotkeysCommand = new RelayCommand(ResetHotkeys);
        CheckUpdateCommand = new AsyncRelayCommand(CheckUpdateAsync, () => !IsChecking && !IsInstalling);
        InstallUpdateCommand = new AsyncRelayCommand(InstallUpdateAsync, () => _controller.Updates.Available != null && !IsInstalling);
        SetAccentCommand = new RelayCommand(p => SetAccent(p as string));
        RestartCommand = new RelayCommand(_controller.Restart);
        OpenDiscordPortalCommand = new RelayCommand(() => OpenUrl("https://discord.com/developers/applications"));
        _controller.Updates.AvailableChanged += OnUpdateChanged;
        _controller.Discord.ConnectionChanged += OnDiscordChanged;
        _themeAtStart = (S.Theme, S.AccentColor);
    }

    private readonly (string Theme, string Accent) _themeAtStart;
    private bool _isChecking, _isInstalling;
    private double _installProgress;

    private AppSettings S => _controller.Settings;

    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }
    public RelayCommand ResetHotkeysCommand { get; }

    // ------------------------------------------------------------------ General

    public IReadOnlyList<LanguageOption> Languages { get; } = new[]
    {
        new LanguageOption("en", "English"),
        new LanguageOption("de", "Deutsch")
    };

    public LanguageOption SelectedLanguage
    {
        get => Languages.FirstOrDefault(l => l.Code == S.Language) ?? Languages[0];
        set
        {
            if (value == null || value.Code == S.Language) return;
            S.Language = value.Code;
            Save();
            Loc.Apply(value.Code);
            OnPropertyChanged();
        }
    }

    public bool StartWithWindows
    {
        get => S.StartWithWindows;
        set
        {
            if (!AutostartService.SetEnabled(value))
            {
                Dialogs.Error(Loc.T("ST.AutostartFailed"));
                OnPropertyChanged();
                return;
            }
            S.StartWithWindows = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool StartMinimized
    {
        get => S.StartMinimized;
        set
        {
            S.StartMinimized = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool CloseToTray
    {
        get => S.CloseToTray;
        set
        {
            S.CloseToTray = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool AutoProfileSwitch
    {
        get => S.AutoProfileSwitch;
        set
        {
            S.AutoProfileSwitch = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool ShowOsd
    {
        get => S.ShowOsd;
        set
        {
            S.ShowOsd = value;
            Save();
            OnPropertyChanged();
        }
    }

    // ------------------------------------------------------------------ Updates

    public AsyncRelayCommand CheckUpdateCommand { get; }
    public AsyncRelayCommand InstallUpdateCommand { get; }

    public bool CheckForUpdates
    {
        get => S.CheckForUpdates;
        set
        {
            S.CheckForUpdates = value;
            Save();
            OnPropertyChanged();
        }
    }

    public bool IsChecking
    {
        get => _isChecking;
        private set => SetProperty(ref _isChecking, value);
    }

    public bool IsInstalling
    {
        get => _isInstalling;
        private set => SetProperty(ref _isInstalling, value);
    }

    public double InstallProgress
    {
        get => _installProgress;
        private set => SetProperty(ref _installProgress, value);
    }

    public bool UpdateAvailable => _controller.Updates.Available != null;
    public string InstallUpdateText => UpdateService.IsInstalled ? Loc.T("Update.Install") : Loc.T("Update.Download");

    public string UpdateStatus
    {
        get
        {
            if (IsInstalling) return Loc.F("Update.Downloading", (int)(InstallProgress * 100));
            if (IsChecking) return Loc.T("Update.Checking");
            if (_controller.Updates.Available is { } u) return Loc.F("Update.Available", u.Version);
            if (_controller.Updates.LastCheck is { } t) return Loc.F("Update.UpToDate", t.ToString("t"));
            return Loc.F("ST.Version", AppPaths.Version);
        }
    }

    private async Task CheckUpdateAsync()
    {
        IsChecking = true;
        OnPropertyChanged(nameof(UpdateStatus));
        await _controller.Updates.CheckAsync();
        IsChecking = false;
        OnPropertyChanged(string.Empty);
    }

    private async Task InstallUpdateAsync()
    {
        IsInstalling = true;
        var progress = new Progress<double>(v =>
        {
            InstallProgress = v;
            OnPropertyChanged(nameof(UpdateStatus));
        });
        await _controller.InstallUpdateAsync(progress);
        IsInstalling = false;
        OnPropertyChanged(string.Empty);
    }

    // ------------------------------------------------------------------ Appearance

    public IReadOnlyList<AccentOption> Accents { get; } = ThemeService.Accents.Select(a => new AccentOption(a.Hex, a.NameKey)).ToList();
    public RelayCommand SetAccentCommand { get; }
    public RelayCommand RestartCommand { get; }

    public bool LightTheme
    {
        get => S.Theme == "Light";
        set
        {
            if (value == LightTheme) return;
            S.Theme = value ? "Light" : "Dark";
            Save();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DarkTheme));
            OnPropertyChanged(nameof(RestartNeeded));
        }
    }

    public bool DarkTheme
    {
        get => !LightTheme;
        set => LightTheme = !value;
    }

    public string AccentColor => S.AccentColor;

    public string AccentName => Accents.FirstOrDefault(a => string.Equals(a.Hex, S.AccentColor, StringComparison.OrdinalIgnoreCase))?.Name ?? S.AccentColor;

    public bool RestartNeeded => S.Theme != _themeAtStart.Theme || !string.Equals(S.AccentColor, _themeAtStart.Accent, StringComparison.OrdinalIgnoreCase);

    private void SetAccent(string? hex)
    {
        if (string.IsNullOrEmpty(hex)) return;
        S.AccentColor = hex;
        Save();
        OnPropertyChanged(nameof(AccentColor));
        OnPropertyChanged(nameof(AccentName));
        OnPropertyChanged(nameof(RestartNeeded));
    }

    // ------------------------------------------------------------------ Discord

    public RelayCommand OpenDiscordPortalCommand { get; }

    public bool DiscordEnabled
    {
        get => S.Discord.Enabled;
        set
        {
            S.Discord.Enabled = value;
            Save();
            _controller.ApplyDiscordSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiscordStatus));
        }
    }

    public string DiscordClientId
    {
        get => S.Discord.ClientId;
        set
        {
            var id = new string((value ?? "").Where(char.IsDigit).ToArray());
            if (id == S.Discord.ClientId) return;
            S.Discord.ClientId = id;
            Save();
            _controller.ApplyDiscordSettings();
            OnPropertyChanged();
            OnPropertyChanged(nameof(DiscordStatus));
        }
    }

    public string DiscordStatus =>
        !S.Discord.Enabled ? Loc.T("Discord.Off")
        : string.IsNullOrWhiteSpace(S.Discord.ClientId) ? Loc.T("Discord.NeedId")
        : _controller.Discord.IsConnected ? Loc.T("Discord.Connected")
        : Loc.T("Discord.Waiting");

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not open link", ex);
        }
    }

    private void OnUpdateChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void OnDiscordChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(DiscordStatus));

    // ------------------------------------------------------------------ Hotkeys

    public HotkeyBinding? HotkeyToggleCrosshair
    {
        get => S.Hotkeys.ToggleCrosshair;
        set => SetHotkey(HotkeyAction.ToggleCrosshair, value);
    }

    public HotkeyBinding? HotkeyToggleGameMode
    {
        get => S.Hotkeys.ToggleGameMode;
        set => SetHotkey(HotkeyAction.ToggleGameMode, value);
    }

    public HotkeyBinding? HotkeyNextPreset
    {
        get => S.Hotkeys.NextPreset;
        set => SetHotkey(HotkeyAction.NextPreset, value);
    }

    public HotkeyBinding? HotkeyToggleHud
    {
        get => S.Hotkeys.ToggleHud;
        set => SetHotkey(HotkeyAction.ToggleHud, value);
    }

    public string StatusToggleHud => StatusText(HotkeyAction.ToggleHud);
    public bool ConflictToggleHud => IsConflict(HotkeyAction.ToggleHud);

    public string StatusToggleCrosshair => StatusText(HotkeyAction.ToggleCrosshair);
    public string StatusToggleGameMode => StatusText(HotkeyAction.ToggleGameMode);
    public string StatusNextPreset => StatusText(HotkeyAction.NextPreset);

    public bool ConflictToggleCrosshair => IsConflict(HotkeyAction.ToggleCrosshair);
    public bool ConflictToggleGameMode => IsConflict(HotkeyAction.ToggleGameMode);
    public bool ConflictNextPreset => IsConflict(HotkeyAction.NextPreset);

    // ------------------------------------------------------------------ Info

    public string DataPath => AppPaths.DataDirectory;
    public string VersionText => Loc.F("ST.Version", AppPaths.Version);

    public void OnNavigatedTo()
    {
        OnPropertyChanged(string.Empty);
    }

    private void SetHotkey(HotkeyAction action, HotkeyBinding? binding)
    {
        S.Hotkeys.Set(action, binding);
        Save();
        _controller.Hotkeys.Apply(S.Hotkeys);
        OnPropertyChanged(string.Empty);
    }

    private void ResetHotkeys()
    {
        S.Hotkeys = new HotkeySettings();
        Save();
        _controller.Hotkeys.Apply(S.Hotkeys);
        OnPropertyChanged(string.Empty);
    }

    private string StatusText(HotkeyAction action)
    {
        if (!_controller.Hotkeys.States.TryGetValue(action, out var state)) return "";
        return state switch
        {
            HotkeyRegistrationState.Registered => Loc.T("ST.Hk.Ok"),
            HotkeyRegistrationState.Conflict => Loc.T("ST.Hk.Conflict"),
            _ => Loc.T("ST.Hk.None")
        };
    }

    private bool IsConflict(HotkeyAction action)
        => _controller.Hotkeys.States.TryGetValue(action, out var s) && s == HotkeyRegistrationState.Conflict;

    private async Task ResetAsync()
    {
        if (!Dialogs.Confirm(Loc.T("ST.ResetConfirm"), Loc.T("ST.Reset"))) return;
        await _controller.ResetAllAsync();
        Dialogs.Info(Loc.T("ST.ResetDone"));
    }

    private static void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Could not open folder", ex);
        }
    }

    private void Save() => _controller.SettingsService.RequestSave();

    private void OnHotkeyStatesChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        _controller.Hotkeys.StatesChanged -= OnHotkeyStatesChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
        _controller.Updates.AvailableChanged -= OnUpdateChanged;
        _controller.Discord.ConnectionChanged -= OnDiscordChanged;
    }
}
