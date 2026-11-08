using System.Collections.ObjectModel;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Views;
using Microsoft.Win32;

namespace GameBoost.ViewModels;

/// <summary>One row on the "Games" page.</summary>
public sealed class GameItemViewModel : ObservableObject
{
    public GameItemViewModel(GameEntry game, GameModeProfile? profile)
    {
        Game = game;
        Profile = profile;
    }

    public GameEntry Game { get; }
    public GameModeProfile? Profile { get; }
    public string Name => Game.Name;
    public string Source => Game.Source;
    public string? ExePath => Game.ExePath;
    public bool IsManual => Game.Source == "Manual";
    public bool HasProfile => Profile != null;
    public string ProfileText => Profile != null ? Loc.F("Games.LinkedTo", Profile.Name) : Loc.T("Games.NoProfile");
}

/// <summary>"Games" page: installed games from Steam, Epic, Riot and manual entries; play or create a game mode profile.</summary>
public sealed class GamesViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private bool _isScanning;
    private bool _scanned;
    private string _filter = "";
    private List<GameEntry> _all = new();

    public GamesViewModel(AppController controller)
    {
        _controller = controller;
        RescanCommand = new AsyncRelayCommand(ScanAsync, () => !IsScanning);
        PlayCommand = new AsyncRelayCommand(p => PlayAsync(p as GameItemViewModel));
        CreateProfileCommand = new RelayCommand(p => CreateProfile(p as GameItemViewModel), p => p is GameItemViewModel { HasProfile: false, ExePath: not null });
        AddManualCommand = new RelayCommand(AddManual);
        RemoveManualCommand = new RelayCommand(p => RemoveManual(p as GameItemViewModel), p => p is GameItemViewModel { IsManual: true });
        Loc.LanguageChanged += OnLanguageChanged;
    }

    public AsyncRelayCommand RescanCommand { get; }
    public AsyncRelayCommand PlayCommand { get; }
    public RelayCommand CreateProfileCommand { get; }
    public RelayCommand AddManualCommand { get; }
    public RelayCommand RemoveManualCommand { get; }

    public ObservableCollection<GameItemViewModel> Games { get; } = new();

    public bool IsScanning
    {
        get => _isScanning;
        private set => SetProperty(ref _isScanning, value);
    }

    public string Filter
    {
        get => _filter;
        set
        {
            if (SetProperty(ref _filter, value ?? "")) Rebuild();
        }
    }

    public bool IsEmpty => _scanned && !IsScanning && Games.Count == 0;
    public string CountText => Loc.F("Games.Count", _all.Count);

    public void OnNavigatedTo()
    {
        if (!_scanned && !IsScanning) _ = ScanAsync();
        else Rebuild();
    }

    private async Task ScanAsync()
    {
        IsScanning = true;
        try
        {
            var manual = _controller.Settings.ManualGames.ToList();
            _all = await Task.Run(() => GameLibrary.Scan(manual));
            Logger.Info($"Game library: {_all.Count} games found");
        }
        catch (Exception ex)
        {
            ErrorReporter.Report("Game library scan failed", ex);
        }
        finally
        {
            _scanned = true;
            IsScanning = false;
            Rebuild();
        }
    }

    private void Rebuild()
    {
        Games.Clear();
        foreach (var g in _all.Where(g => _filter.Length == 0 || g.Name.Contains(_filter, StringComparison.CurrentCultureIgnoreCase)))
            Games.Add(new GameItemViewModel(g, FindProfile(g)));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    private GameModeProfile? FindProfile(GameEntry g) =>
        g.ProcessName is { } name
            ? _controller.Settings.Profiles.FirstOrDefault(p => p.TriggerExecutables.Contains(name, StringComparer.OrdinalIgnoreCase))
            : null;

    private async Task PlayAsync(GameItemViewModel? item)
    {
        if (item == null) return;
        try
        {
            if (item.Profile != null && !_controller.GameMode.IsActive)
                await _controller.ActivateGameModeAsync(item.Profile, automatic: false);
            GameLibrary.Launch(item.Game);
        }
        catch (Exception ex)
        {
            ErrorReporter.Report($"Could not start {item.Name}", ex);
        }
    }

    private void CreateProfile(GameItemViewModel? item)
    {
        if (item?.Game.ProcessName is not { } process) return;
        if (ProcessGuard.IsProtectedName(process))
        {
            Dialogs.Info(Loc.F("GM.Auto.Protected", process));
            return;
        }
        var template = _controller.SelectedProfile;
        var profile = template.CloneAsNew(item.Name);
        profile.TriggerExecutables.Clear();
        profile.TriggerExecutables.Add(process);
        _controller.Settings.Profiles.Add(profile);
        _controller.SettingsService.RequestSave();
        Rebuild();
        Dialogs.Info(Loc.F("Games.ProfileCreated", item.Name));
    }

    private void AddManual()
    {
        var dlg = new OpenFileDialog
        {
            Title = Loc.T("Dlg.SelectGame"),
            Filter = Loc.T("Dlg.ExeOnlyFilter"),
            CheckFileExists = true
        };
        if (dlg.ShowDialog() != true) return;
        var name = Dialogs.Input(Loc.T("Games.AddTitle"), Loc.T("Games.AddPrompt"), Path.GetFileNameWithoutExtension(dlg.FileName));
        if (name == null) return;
        _controller.Settings.ManualGames.Add(new ManualGame { Name = name, ExePath = dlg.FileName });
        _controller.SettingsService.RequestSave();
        _ = ScanAsync();
    }

    private void RemoveManual(GameItemViewModel? item)
    {
        if (item == null) return;
        _controller.Settings.ManualGames.RemoveAll(m => string.Equals(m.ExePath, item.ExePath, StringComparison.OrdinalIgnoreCase));
        _controller.SettingsService.RequestSave();
        _all.Remove(item.Game);
        Rebuild();
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Rebuild();

    public void Dispose() => Loc.LanguageChanged -= OnLanguageChanged;
}
