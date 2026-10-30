using System.Windows;
using GameBoost.Infrastructure;
using GameBoost.Views;
using Microsoft.Win32;

namespace GameBoost.Services;

/// <summary>Verwaltet das Crosshair-Overlay-Fenster.</summary>
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
            Logger.Info("Crosshair eingeblendet");
        }
        catch (Exception ex)
        {
            Logger.Error("Crosshair konnte nicht angezeigt werden", ex);
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
                Logger.Info("Crosshair ausgeblendet");
            }
        }
        catch (Exception ex)
        {
            Logger.Error("Crosshair konnte nicht ausgeblendet werden", ex);
        }
        RaiseVisibilityChanged();
    }

    public void Toggle()
    {
        if (IsVisible) Hide();
        else Show();
    }

    /// <summary>Übernimmt Monitor und Crosshair-Objekt aus den Einstellungen (z. B. nach Reset).</summary>
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
            Logger.Error("Overlay-Position konnte nicht aktualisiert werden", ex);
        }
    }

    private void EnsureWindow()
    {
        if (_window != null) return;
        _window = new CrosshairOverlayWindow();
        _ = _window.Handle; // Fensterhandle erzeugen → Durchklick-Stile sind vor dem Anzeigen aktiv
        _window.Closed += (_, _) => _window = null;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        Application.Current?.Dispatcher.BeginInvoke(() =>
        {
            Logger.Info("Bildschirmkonfiguration geändert – Overlay wird neu positioniert");
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
            Logger.Error("Fehler in VisibilityChanged-Handler", ex);
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
            Logger.Warn("Overlay konnte nicht geschlossen werden", ex);
        }
        _window = null;
    }
}
