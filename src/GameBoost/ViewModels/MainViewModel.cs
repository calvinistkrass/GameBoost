using GameBoost.Infrastructure;

namespace GameBoost.ViewModels;

/// <summary>Main window: navigation, page header and status display in the sidebar.</summary>
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly AppController _controller;
    private object? _currentPage;
    private string _currentPageKey = "Home";
    private bool _windowVisible; // becomes true when the main window is shown (not when starting minimized)

    public MainViewModel(AppController controller)
    {
        _controller = controller;
        NavigateCommand = new RelayCommand(p => Navigate(p as string ?? "Home"));

        _controller.GameMode.StateChanged += OnStatusChanged;
        _controller.Overlay.VisibilityChanged += OnStatusChanged;
        _controller.Hud.VisibilityChanged += OnStatusChanged;
        Loc.LanguageChanged += OnStatusChanged;

        CreatePages();
        Navigate(_currentPageKey);
    }

    public DashboardViewModel HomePage { get; private set; } = null!;
    public GameModeViewModel GameModePage { get; private set; } = null!;
    public CrosshairViewModel CrosshairPage { get; private set; } = null!;
    public PerformanceViewModel PerformancePage { get; private set; } = null!;
    public PcCheckViewModel PcCheckPage { get; private set; } = null!;
    public GamesViewModel GamesPage { get; private set; } = null!;
    public SessionsViewModel SessionsPage { get; private set; } = null!;
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
    public bool IsHudVisible => _controller.Hud.IsVisible;

    public string GameModeStatusText => _controller.GameMode.IsBusy
        ? "…"
        : _controller.GameMode.IsActive
            ? $"{Loc.T("Status.On")} · {_controller.GameMode.ActiveProfile?.Name}"
            : Loc.T("Status.Off");

    public string CrosshairStatusText => _controller.Overlay.IsVisible ? Loc.T("Status.On") : Loc.T("Status.Off");
    public string HudStatusText => _controller.Hud.IsVisible ? Loc.T("Status.On") : Loc.T("Status.Off");

    public string VersionText => Loc.F("ST.Version", AppPaths.Version);

    public void Navigate(string key)
    {
        var target = key.ToLowerInvariant() switch
        {
            "gamemode" => (object)GameModePage,
            "crosshair" => CrosshairPage,
            "performance" or "hud" => PerformancePage,
            "pccheck" or "check" => PcCheckPage,
            "games" => GamesPage,
            "sessions" => SessionsPage,
            "settings" => SettingsPage,
            _ => HomePage
        };

        if (!ReferenceEquals(target, CurrentPage) && _windowVisible) (CurrentPage as INavigationAware)?.OnNavigatedFrom();
        CurrentPage = target;
        CurrentPageKey = target switch
        {
            GameModeViewModel => "GameMode",
            CrosshairViewModel => "Crosshair",
            PerformanceViewModel => "Performance",
            PcCheckViewModel => "PcCheck",
            GamesViewModel => "Games",
            SessionsViewModel => "Sessions",
            SettingsViewModel => "Settings",
            _ => "Home"
        };
        if (_windowVisible) (CurrentPage as INavigationAware)?.OnNavigatedTo();
    }

    /// <summary>Pages pause live data while the window is hidden in the tray.</summary>
    public void SetWindowVisible(bool visible)
    {
        if (_windowVisible == visible) return;
        _windowVisible = visible;
        if (visible) (CurrentPage as INavigationAware)?.OnNavigatedTo();
        else (CurrentPage as INavigationAware)?.OnNavigatedFrom();
    }

    /// <summary>Recreates all pages (after "Reset everything").</summary>
    public void Rebuild()
    {
        (CurrentPage as INavigationAware)?.OnNavigatedFrom();
        DisposePages();
        CreatePages();
        CurrentPage = null;
        Navigate(CurrentPageKey);
        OnPropertyChanged(string.Empty);
    }

    private void CreatePages()
    {
        HomePage = new DashboardViewModel(_controller);
        GameModePage = new GameModeViewModel(_controller);
        CrosshairPage = new CrosshairViewModel(_controller);
        PerformancePage = new PerformanceViewModel(_controller);
        PcCheckPage = new PcCheckViewModel(_controller);
        GamesPage = new GamesViewModel(_controller);
        SessionsPage = new SessionsViewModel(_controller);
        SettingsPage = new SettingsViewModel(_controller);
    }

    private void DisposePages()
    {
        HomePage?.Dispose();
        GameModePage?.Dispose();
        CrosshairPage?.Dispose();
        PerformancePage?.Dispose();
        PcCheckPage?.Dispose();
        GamesPage?.Dispose();
        SessionsPage?.Dispose();
        SettingsPage?.Dispose();
    }

    private void OnStatusChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(IsGameModeActive));
        OnPropertyChanged(nameof(IsCrosshairVisible));
        OnPropertyChanged(nameof(IsHudVisible));
        OnPropertyChanged(nameof(GameModeStatusText));
        OnPropertyChanged(nameof(CrosshairStatusText));
        OnPropertyChanged(nameof(HudStatusText));
        OnPropertyChanged(nameof(VersionText));
    }

    public void Dispose()
    {
        _controller.GameMode.StateChanged -= OnStatusChanged;
        _controller.Overlay.VisibilityChanged -= OnStatusChanged;
        _controller.Hud.VisibilityChanged -= OnStatusChanged;
        Loc.LanguageChanged -= OnStatusChanged;
        DisposePages();
    }
}
