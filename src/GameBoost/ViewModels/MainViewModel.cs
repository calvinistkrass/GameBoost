using GameBoost.Infrastructure;

namespace GameBoost.ViewModels;

/// <summary>Hauptfenster: Navigation und Statusanzeige in der Seitenleiste.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppController _controller;
    private object? _currentPage;
    private string _currentPageKey = "GameMode";

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "GameMode"));

        _controller.GameMode.StateChanged += OnStatusChanged;
        _controller.Overlay.VisibilityChanged += OnStatusChanged;
        Loc.LanguageChanged += OnStatusChanged;

        CreatePages();
        Navigate(_currentPageKey);
    }

    public GameModeViewModel GameModePage { get; private set; } = null!;
    public CrosshairViewModel CrosshairPage { get; private set; } = null!;
    public SettingsViewModel SettingsPage { get; private set; } = null!;

    public RelayCommand NavigateCommand { get; }

    public object? CurrentPage
    {
        get => _currentPage;
        private set => SetProperty(ref _currentPage, value);
    }

    public string CurrentPageKey
    {
        get => _currentPageKey;
        private set => SetProperty(ref _currentPageKey, value);
    }

    public bool IsGameModeActive => _controller.GameMode.IsActive;
    public bool IsCrosshairVisible => _controller.Overlay.IsVisible;

    public string GameModeStatusText => _controller.GameMode.IsBusy
        ? "…"
        : _controller.GameMode.IsActive
            ? $"{Loc.T("Status.On")} · {_controller.GameMode.ActiveProfile?.Name}"
            : Loc.T("Status.Off");

    public string CrosshairStatusText => _controller.Overlay.IsVisible ? Loc.T("Status.On") : Loc.T("Status.Off");

    public string VersionText => Loc.F("ST.Version", AppPaths.Version);

    public void Navigate(string key)
    {
        CurrentPage = key.ToLowerInvariant() switch
        {
            "crosshair" => CrosshairPage,
            "settings" => SettingsPage,
            _ => GameModePage
        };
        CurrentPageKey = CurrentPage == CrosshairPage ? "Crosshair" : CurrentPage == SettingsPage ? "Settings" : "GameMode";
        (CurrentPage as INavigationAware)?.OnNavigatedTo();
    }

    /// <summary>Erzeugt alle Seiten neu (nach "Alles zurücksetzen").</summary>
    public void Rebuild()
    {
        DisposePages();
        CreatePages();
        Navigate(CurrentPageKey);
        OnPropertyChanged(string.Empty);
    }

    private void CreatePages()
    {
        GameModePage = new GameModeViewModel(_controller);
        CrosshairPage = new CrosshairViewModel(_controller);
        SettingsPage = new SettingsViewModel(_controller);
    }

    private void DisposePages()
    {
        GameModePage?.Dispose();
        CrosshairPage?.Dispose();
        SettingsPage?.Dispose();
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsGameModeActive));
        OnPropertyChanged(nameof(IsCrosshairVisible));
        OnPropertyChanged(nameof(GameModeStatusText));
        OnPropertyChanged(nameof(CrosshairStatusText));
        OnPropertyChanged(nameof(VersionText));
    }

    public void Dispose()
    {
        _controller.GameMode.StateChanged -= OnStatusChanged;
        _controller.Overlay.VisibilityChanged -= OnStatusChanged;
        Loc.LanguageChanged -= OnStatusChanged;
        DisposePages();
    }
}
