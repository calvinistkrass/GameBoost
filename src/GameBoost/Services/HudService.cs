using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Rendering;
using GameBoost.Services.Performance;
using GameBoost.Views;

namespace GameBoost.Services;

/// <summary>In-game performance overlay (FPS, GPU, CPU, VRAM, RAM) in a corner of the selected monitor.</summary>
public sealed class HudService : IDisposable
{
    public const string WindowTitle = "GameBoost Performance Overlay";

    private readonly SettingsService _settings;
    private readonly PerformanceMonitorService _monitor;
    private readonly DispatcherTimer _guard;
    private LayeredWindow? _window;
    private HudSettings? _subscribed;
    private bool _wanted;
    private bool _acquired;

    public HudService(SettingsService settings, PerformanceMonitorService monitor)
    {
        _settings = settings;
        _monitor = monitor;
        _monitor.Updated += OnSnapshot;
        _guard = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Guard(), Dispatcher.CurrentDispatcher);
        Subscribe();
    }

    private HudSettings Options => _settings.Settings.Hud;

    /// <summary>true while the user wants the HUD on (it may be hidden temporarily by "only over fullscreen").</summary>
    public bool IsVisible => _wanted;

    public event EventHandler? VisibilityChanged;

    public void Show()
    {
        try
        {
            _wanted = true;
            if (!_acquired)
            {
                _monitor.Acquire();
                _acquired = true;
            }
            _monitor.SetFpsWanted(Options.ShowFps);
            _window ??= new LayeredWindow(WindowTitle);
            Render(_monitor.Latest);
            UpdateWindowVisibility();
            _guard.Start();
            Logger.Info("Performance overlay shown");
        }
        catch (Exception ex)
        {
            Logger.Error("Performance overlay could not be shown", ex);
        }
        Raise();
    }

    public void Hide()
    {
        _wanted = false;
        _guard.Stop();
        _window?.Hide();
        if (_acquired)
        {
            _monitor.SetFpsWanted(false);
            _monitor.Release();
            _acquired = false;
        }
        Logger.Info("Performance overlay hidden");
        Raise();
    }

    public void Toggle()
    {
        if (_wanted) Hide();
        else Show();
    }

    /// <summary>Re-reads the settings object (e.g. after a reset) and redraws.</summary>
    public void ApplySettings()
    {
        Subscribe();
        if (!_wanted) return;
        _monitor.SetFpsWanted(Options.ShowFps);
        Render(_monitor.Latest);
        UpdateWindowVisibility();
    }

    private void Subscribe()
    {
        if (ReferenceEquals(_subscribed, Options)) return;
        if (_subscribed != null) _subscribed.PropertyChanged -= OnOptionsChanged;
        _subscribed = Options;
        _subscribed.PropertyChanged += OnOptionsChanged;
    }

    private void OnOptionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HudSettings.Visible)) return;
        ApplySettings();
    }

    private void OnSnapshot(object? sender, PerformanceSnapshot snapshot)
    {
        if (!_wanted) return;
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            if (_wanted) Render(snapshot);
        });
    }

    private void Render(PerformanceSnapshot snapshot)
    {
        if (_window == null) return;
        try
        {
            var monitor = MonitorService.FindOrPrimary(_settings.Settings.Crosshair.MonitorDeviceName);
            var scale = Options.Scale / 100.0 * monitor.DpiScale;
            var image = HudRenderer.Render(snapshot, Options, scale, Loc.T("Hud.FpsAdmin"));
            var margin = (int)Math.Round(14 * monitor.DpiScale);

            var left = Options.Position is HudPosition.TopLeft or HudPosition.BottomLeft
                ? monitor.Left + margin
                : monitor.Left + monitor.Width - image.Width - margin;
            var top = Options.Position is HudPosition.TopLeft or HudPosition.TopRight
                ? monitor.Top + margin
                : monitor.Top + monitor.Height - image.Height - margin;

            _window.SetImage(image, left, top);
        }
        catch (Exception ex)
        {
            Logger.Error("Performance overlay could not be drawn", ex);
        }
    }

    private void Guard()
    {
        if (!_wanted) return;
        UpdateWindowVisibility();
        _window?.EnsureTopmost();
    }

    private void UpdateWindowVisibility()
    {
        if (_window == null) return;
        var show = true;
        if (Options.OnlyOverFullscreen)
        {
            var monitor = MonitorService.FindOrPrimary(_settings.Settings.Crosshair.MonitorDeviceName);
            show = ForegroundDetector.IsFullscreenAppOn(monitor);
        }
        if (show) _window.Show();
        else _window.Hide();
    }

    private void Raise()
    {
        try
        {
            VisibilityChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            Logger.Error("Error in VisibilityChanged handler", ex);
        }
    }

    public void Dispose()
    {
        _guard.Stop();
        _monitor.Updated -= OnSnapshot;
        if (_subscribed != null) _subscribed.PropertyChanged -= OnOptionsChanged;
        if (_acquired) _monitor.Release();
        _acquired = false;
        _window?.Dispose();
        _window = null;
    }
}
