using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Rendering;
using GameBoost.Services.Performance;

namespace GameBoost.ViewModels;

/// <summary>"Performance overlay" page: in-game HUD settings with live preview.</summary>
public sealed class PerformanceViewModel : ObservableObject, IDisposable, INavigationAware
{
    private readonly AppController _controller;
    private readonly HudSettings _options;
    private PerformanceSnapshot _snapshot = PerformanceSnapshot.Empty;
    private ImageSource? _preview;
    private bool _acquired;

    public PerformanceViewModel(AppController controller)
    {
        _controller = controller;
        _options = controller.Settings.Hud;
        _options.PropertyChanged += OnOptionsChanged;
        _controller.Hud.VisibilityChanged += OnHudVisibilityChanged;
        _controller.Performance.Updated += OnSnapshot;
        Loc.LanguageChanged += OnLanguageChanged;

        RestartAsAdminCommand = new RelayCommand(_controller.RestartAsAdministrator, () => !IsElevated);
        RenderPreview();
    }

    public HudSettings Options => _options;

    public RelayCommand RestartAsAdminCommand { get; }

    public bool HudVisible
    {
        get => _controller.Hud.IsVisible;
        set => _controller.SetHudVisible(value);
    }

    public bool IsElevated => _controller.Performance.IsElevated;

    public ImageSource? Preview
    {
        get => _preview;
        private set => SetProperty(ref _preview, value);
    }

    public string HotkeyText => _controller.Settings.Hotkeys.ToggleHud?.ToString() ?? Loc.T("Hotkey.None");

    public void OnNavigatedTo()
    {
        if (!_acquired)
        {
            _controller.Performance.Acquire();
            _acquired = true;
        }
        OnPropertyChanged(string.Empty);
        RenderPreview();
    }

    public void OnNavigatedFrom()
    {
        if (!_acquired) return;
        _controller.Performance.Release();
        _acquired = false;
    }

    private void RenderPreview()
    {
        try
        {
            var image = HudRenderer.Render(_snapshot, _options, _options.Scale / 100.0, Loc.T("Hud.FpsAdmin"));
            Preview = HudRenderer.ToBitmap(image);
        }
        catch (Exception ex)
        {
            Logger.Warn("HUD preview could not be rendered", ex);
        }
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        _controller.SettingsService.RequestSave();
        RenderPreview();
    }

    private void OnSnapshot(object? sender, PerformanceSnapshot snapshot)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            _snapshot = snapshot;
            RenderPreview();
        });
    }

    private void OnHudVisibilityChanged(object? sender, EventArgs e) => OnPropertyChanged(nameof(HudVisible));

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(string.Empty);
        RenderPreview();
    }

    public void Dispose()
    {
        OnNavigatedFrom();
        _options.PropertyChanged -= OnOptionsChanged;
        _controller.Hud.VisibilityChanged -= OnHudVisibilityChanged;
        _controller.Performance.Updated -= OnSnapshot;
        Loc.LanguageChanged -= OnLanguageChanged;
    }
}
