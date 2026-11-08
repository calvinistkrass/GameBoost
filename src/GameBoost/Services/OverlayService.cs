using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using GameBoost.Infrastructure;
using GameBoost.Views;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Manages the crosshair overlay window.</summary>
public sealed class OverlayService : IDisposable
{
    private const int VK_RBUTTON = 0x02;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _aimTimer;
    private CrosshairOverlayWindow? _window;

    public OverlayService(SettingsService settings)
    {
        _settings = settings;
        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        // "Hide while aiming": hide the crosshair while the right mouse button is held.
        _aimTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(15), DispatcherPriority.Render, (_, _) => CheckAiming(), Dispatcher.CurrentDispatcher);
        _aimTimer.Stop();
    }

    private void CheckAiming()
    {
        if (_window is not { IsVisible: true }) return;
        var aiming = _settings.Settings.Crosshair.HideWhileAiming && (GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0;
        var opacity = aiming ? 0.0 : 1.0;
        if (Math.Abs(_window.Opacity - opacity) > 0.01) _window.Opacity = opacity;
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
            _aimTimer.Start();
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
            _aimTimer.Stop();
            if (_window?.IsVisible == true)
            {
                _window.Opacity = 1;
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
        _aimTimer.Stop();
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
