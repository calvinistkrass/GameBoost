using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Views;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Manages the crosshair overlay window.</summary>
public sealed class OverlayService : IDisposable
{
    private readonly SettingsService _settings;
    private CrosshairOverlayWindow? _window;

    public OverlayService(SettingsService settings)
    {
        _settings = settings;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public bool IsVisible => _window?.IsVisible == true;

    public event EventHandler? VisibilityChanged;

    public void Show()
    {
        try
        {
            EnsureWindow();
            _window!.Config = _settings.Settings.Crosshair.Current;
            Reposition();
            if (!_window.IsVisible) _window.Show();
            Reposition();
            Logger.Info("Crosshair shown");
        }
        catch (Exception ex)
        {
            Logger.Error("Could not show crosshair", ex);
        }
        RaiseVisibilityChanged();
    }

    public void Hide()
    {
        try
        {
            if (_window?.IsVisible == true)
            {
                _window.Hide();
                Logger.Info("Crosshair hidden");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Could not hide crosshair", ex);
        }
        RaiseVisibilityChanged();
    }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    /// <summary>Takes monitor and crosshair object from the settings (e.g. after a reset).</summary>
    public void ApplySettings()
    {
        if (_window == null) return;
        _window.Config = _settings.Settings.Crosshair.Current;
        Reposition();
    }

    public void Reposition()
    {
        if (_window == null) return;
        try
        {
            var monitor = MonitorService.FindOrPrimary(_settings.Settings.Crosshair.MonitorDeviceName);
            _window.PlaceOn(monitor);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not update overlay position", ex);
        }
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        _window = new CrosshairOverlayWindow();
        _ = _window.Handle; // create the handle → click-through styles are active before showing
        _window.Closed += (_, _) => _window = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Logger.Info("Display configuration changed – repositioning overlay");
            Reposition();
        });
    }

    private void RaiseVisibilityChanged()
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
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        try
        {
            _window?.Close();
        }
        catch (Exception ex)
        {
            Logger.Warn("Could not close overlay", ex);
        }
        _window = null;
    }
}
