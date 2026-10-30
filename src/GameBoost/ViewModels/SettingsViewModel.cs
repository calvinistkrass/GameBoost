using System.Diagnostics;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Views;

namespace GameBoost.ViewModels;

public sealed record LanguageOption(string Code, string Name);

/// <summary>Seite "Einstellungen".</summary>
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
    }

    private AppSettings S => _controller.Settings;

    public RelayCommand OpenDataFolderCommand { get; }
    public RelayCommand OpenLogFolderCommand { get; }
    public AsyncRelayCommand ResetCommand { get; }
    public RelayCommand ResetHotkeysCommand { get; }

    // ------------------------------------------------------------------ Allgemein

    public IReadOnlyList<LanguageOption> Languages { get; } = new[]
    {
        new LanguageOption("de", "Deutsch"),
        new LanguageOption("en", "English")
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
            ErrorReporter.Report("Ordner konnte nicht geöffnet werden", ex);
        }
    }

    private void Save() => _controller.SettingsService.RequestSave();

    private void OnHotkeyStatesChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void OnLanguageChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        _controller.Hotkeys.StatesChanged -= OnHotkeyStatesChanged;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}
