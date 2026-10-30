using System.Globalization;
using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Services.Performance;
using GameBoost.Services.SystemCheck;

namespace GameBoost.ViewModels;

/// <summary>Home page: quick toggles, live system stats, PC score and hardware summary.</summary>
public sealed class DashboardViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private PerformanceSnapshot _snapshot = PerformanceSnapshot.Empty;
    private bool _acquired;
    private bool _checking;

    public DashboardViewModel(AppController controller)
    {
        _controller = controller;
        ToggleGameModeCommand = new AsyncRelayCommand(_controller.ToggleGameModeAsync, () => !_controller.GameMode.IsBusy);
        ToggleCrosshairCommand = new RelayCommand(_controller.ToggleCrosshair);
        ToggleHudCommand = new RelayCommand(_controller.ToggleHud);
        NavigateCommand = new RelayCommand(p => _controller.MainViewModel?.Navigate(p as string ?? "Home"));

        _controller.GameMode.StateChanged += OnStateChanged;
        _controller.Overlay.VisibilityChanged += OnStateChanged;
        _controller.Hud.VisibilityChanged += OnStateChanged;
        _controller.Performance.Updated += OnSnapshot;
        _controller.SystemCheck.Completed += OnCheckCompleted;
        Loc.LanguageChanged += OnStateChanged;
    }

    public AsyncRelayCommand ToggleGameModeCommand { get; }
    public RelayCommand ToggleCrosshairCommand { get; }
    public RelayCommand ToggleHudCommand { get; }
    public RelayCommand NavigateCommand { get; }

    // ------------------------------------------------------------------ Header

    public string Greeting
    {
        get
        {
            var h = DateTime.Now.Hour;
            return Loc.T(h < 5 ? "Home.Greeting.Night" : h < 12 ? "Home.Greeting.Morning" : h < 18 ? "Home.Greeting.Afternoon" : "Home.Greeting.Evening");
        }
    }

    // ------------------------------------------------------------------ Quick toggles

    public bool GameModeActive => _controller.GameMode.IsActive;
    public string GameModeState => _controller.GameMode.IsBusy ? "…" : Loc.T(GameModeActive ? "Status.On" : "Status.Off");
    public string GameModeDetail => GameModeActive
        ? Loc.F("Home.ProfileActive", _controller.GameMode.ActiveProfile?.Name ?? "")
        : Loc.F("Home.ProfileReady", _controller.SelectedProfile.Name);

    public bool CrosshairVisible => _controller.Overlay.IsVisible;
    public string CrosshairState => Loc.T(CrosshairVisible ? "Status.On" : "Status.Off");
    public string CrosshairDetail => _controller.Settings.Crosshair.ActivePresetName ?? Loc.T("Home.CustomCrosshair");

    public bool HudVisible => _controller.Hud.IsVisible;
    public string HudState => Loc.T(HudVisible ? "Status.On" : "Status.Off");
    public string HudDetail => Loc.T(_controller.Performance.IsElevated ? "Home.HudWithFps" : "Home.HudNoFps");

    // ------------------------------------------------------------------ Live stats

    public string CpuText => Percent(_snapshot.CpuLoad);
    public double CpuValue => _snapshot.CpuLoad ?? 0;
    public string GpuText => Percent(_snapshot.GpuLoad);
    public double GpuValue => _snapshot.GpuLoad ?? 0;
    public string GpuTempText => _snapshot.GpuTemperature is { } t ? t.ToString("0", CultureInfo.CurrentCulture) + " °C" : "–";
    public double GpuTempValue => Math.Clamp(_snapshot.GpuTemperature ?? 0, 0, 100);
    public string RamText => _snapshot.RamTotalGb > 0
        ? $"{_snapshot.RamUsedGb.ToString("0.0", CultureInfo.CurrentCulture)} / {_snapshot.RamTotalGb:0} GB"
        : "–";
    public double RamValue => _snapshot.RamTotalGb > 0 ? _snapshot.RamUsedGb / _snapshot.RamTotalGb * 100 : 0;
    public string VramText => _snapshot.VramUsedGb is { } v ? v.ToString("0.0", CultureInfo.CurrentCulture) + " GB" : "–";

    // ------------------------------------------------------------------ PC check summary

    public bool HasCheck => _controller.SystemCheck.LastResult != null;
    public bool IsChecking => _checking;

    public int Score => _controller.SystemCheck.LastResult is { } r ? SystemCheckService.Score(r.Checks) : 0;
    public string ScoreText => HasCheck ? Score.ToString(CultureInfo.CurrentCulture) : "–";
    public string ScoreLabel => !HasCheck ? Loc.T("Home.Analyzing")
        : Score >= 90 ? Loc.T("PC.Score.Great") : Score >= 70 ? Loc.T("PC.Score.Good") : Loc.T("PC.Score.Attention");
    public string OpenIssuesText => _controller.SystemCheck.LastResult is { } r
        ? Loc.F("Home.OpenIssues", r.Checks.Count(c => c.Status is CheckStatus.Recommended or CheckStatus.Warning))
        : "";

    public string CpuName => _controller.SystemCheck.LastResult?.Hardware.Cpu ?? "…";
    public string GpuName => _controller.SystemCheck.LastResult is { } r
        ? r.Hardware.Gpu + (r.Hardware.VramGb is { } v ? $" · {v:0} GB" : "")
        : "…";
    public string MemoryName => _controller.SystemCheck.LastResult is { } r ? $"{r.Hardware.RamGb:0} GB RAM" : "…";
    public string DisplayName => _controller.SystemCheck.LastResult is { Hardware.ResolutionWidth: > 0 } r
        ? $"{r.Hardware.ResolutionWidth} × {r.Hardware.ResolutionHeight} · {r.Hardware.CurrentHz} Hz"
        : "…";
    public string TierText => _controller.SystemCheck.LastResult is { } r ? Loc.T($"Tier.{r.Hardware.Tier}") : "";

    // ------------------------------------------------------------------ Hotkeys

    public string HkGameMode => Hk(_controller.Settings.Hotkeys.ToggleGameMode);
    public string HkCrosshair => Hk(_controller.Settings.Hotkeys.ToggleCrosshair);
    public string HkHud => Hk(_controller.Settings.Hotkeys.ToggleHud);
    public string HkNextPreset => Hk(_controller.Settings.Hotkeys.NextPreset);

    private static string Hk(Models.HotkeyBinding? b) => b?.IsValid == true ? b.ToString() : "–";

    // ------------------------------------------------------------------ Navigation

    public void OnNavigatedTo()
    {
        if (!_acquired)
        {
            _controller.Performance.Acquire();
            _acquired = true;
        }
        OnPropertyChanged(string.Empty);
        if (!HasCheck && !_checking) _ = RunCheckAsync();
    }

    public void OnNavigatedFrom()
    {
        if (!_acquired) return;
        _controller.Performance.Release();
        _acquired = false;
    }

    private async Task RunCheckAsync()
    {
        _checking = true;
        OnPropertyChanged(nameof(IsChecking));
        try
        {
            await _controller.SystemCheck.RunAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("PC check failed", ex);
        }
        finally
        {
            _checking = false;
            OnPropertyChanged(nameof(IsChecking));
        }
    }

    private static string Percent(double? value) => value is { } v ? v.ToString("0", CultureInfo.CurrentCulture) + " %" : "–";

    private void OnSnapshot(object? sender, PerformanceSnapshot snapshot)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _snapshot = snapshot;
            OnPropertyChanged(nameof(CpuText));
            OnPropertyChanged(nameof(CpuValue));
            OnPropertyChanged(nameof(GpuText));
            OnPropertyChanged(nameof(GpuValue));
            OnPropertyChanged(nameof(GpuTempText));
            OnPropertyChanged(nameof(GpuTempValue));
            OnPropertyChanged(nameof(RamText));
            OnPropertyChanged(nameof(RamValue));
            OnPropertyChanged(nameof(VramText));
        });
    }

    private void OnStateChanged(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    private void OnCheckCompleted(object? sender, EventArgs e) => OnPropertyChanged(string.Empty);

    public void Dispose()
    {
        OnNavigatedFrom();
        _controller.GameMode.StateChanged -= OnStateChanged;
        _controller.Overlay.VisibilityChanged -= OnStateChanged;
        _controller.Hud.VisibilityChanged -= OnStateChanged;
        _controller.Performance.Updated -= OnSnapshot;
        _controller.SystemCheck.Completed -= OnCheckCompleted;
        Loc.LanguageChanged -= OnStateChanged;
    }
}
