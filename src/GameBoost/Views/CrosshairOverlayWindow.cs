using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GameBoost.Controls;
using GameBoost.Infrastructure;
using GameBoost.Models;
using GameBoost.Services;

namespace GameBoost.Views;

/// <summary>
/// Randloses, transparentes, durchklickbares Fenster, das immer im Vordergrund liegt und
/// nicht in Taskleiste oder Alt+Tab erscheint. Wird pixelgenau mittig auf einem Monitor platziert.
/// </summary>
public sealed class CrosshairOverlayWindow : Window
{
    /// <summary>Kantenlänge des Overlays in physischen Pixeln (gerade Zahl → exakte Mitte).</summary>
    public const int SizePx = 640;

    private readonly CrosshairRenderer _renderer;
    private readonly DispatcherTimer _topmostTimer;
    private MonitorInfo? _monitor;

    public CrosshairOverlayWindow()
    {
        Title = "GameBoost Crosshair";
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        IsHitTestVisible = false;
        UseLayoutRounding = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -32000;
        Top = -32000;
        Width = SizePx;
        Height = SizePx;

        _renderer = new CrosshairRenderer { IsHitTestVisible = false };
        Content = _renderer;

        SourceInitialized += (_, _) => ApplyClickThroughStyles();
        DpiChanged += (_, _) => Dispatcher.BeginInvoke(DispatcherPriority.Background, Reposition);

        // Manche Spiele/Fenster drängen sich nach vorne – Topmost regelmäßig erneuern.
        _topmostTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => EnsureTopmost(), Dispatcher);
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) _topmostTimer.Start();
            else _topmostTimer.Stop();
        };
        Closed += (_, _) => _topmostTimer.Stop();
    }

    public CrosshairConfig? Config
    {
        get => _renderer.Config;
        set => _renderer.Config = value;
    }

    public IntPtr Handle => new WindowInteropHelper(this).EnsureHandle();

    /// <summary>Platziert das Overlay exakt mittig auf dem angegebenen Monitor.</summary>
    public void PlaceOn(MonitorInfo monitor)
    {
        _monitor = monitor;
        Reposition();
    }

    private void Reposition()
    {
        try
        {
            if (_monitor == null) return;
            var hwnd = Handle;

            // WPF-Größe passend zur DPI des Zielmonitors setzen, damit Layout und physische Größe übereinstimmen.
            var scale = _monitor.DpiScale > 0 ? _monitor.DpiScale : 1;
            Width = SizePx / scale;
            Height = SizePx / scale;

            var x = _monitor.Left + _monitor.Width / 2 - SizePx / 2;
            var y = _monitor.Top + _monitor.Height / 2 - SizePx / 2;
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, x, y, SizePx, SizePx,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
        catch (Exception ex)
        {
            Logger.Error("Overlay konnte nicht positioniert werden", ex);
        }
    }

    private void EnsureTopmost()
    {
        try
        {
            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOOWNERZORDER);
        }
        catch (Exception ex)
        {
            Logger.Warn("Topmost konnte nicht erneuert werden", ex);
        }
    }

    private void ApplyClickThroughStyles()
    {
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex |= NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
            ex &= ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, new IntPtr(ex));
        }
        catch (Exception ex)
        {
            Logger.Error("Overlay-Fensterstile konnten nicht gesetzt werden", ex);
        }
    }
}
