using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using GameBoost.Infrastructure;
using GameBoost.Rendering;
using GameBoost.Views;

namespace GameBoost.Services;

/// <summary>
/// Short on-screen hints ("Crosshair ON", "GPU 88 °C") at the top of the screen. Click-through and always on top,
/// so they also work in-game while Windows notifications are muted.
/// </summary>
public sealed class OsdService : IDisposable
{
    public const string WindowTitle = "GameBoost Hint";

    private readonly SettingsService _settings;
    private readonly DispatcherTimer _hideTimer;
    private LayeredWindow? _window;

    public OsdService(SettingsService settings)
    {
        _settings = settings;
        _hideTimer = new DispatcherTimer(TimeSpan.FromSeconds(1.8), DispatcherPriority.Normal, (_, _) => Hide(), Dispatcher.CurrentDispatcher);
        _hideTimer.Stop();
    }

    public void Show(string text, bool warning = false)
    {
        if (!_settings.Settings.ShowOsd) return;
        try
        {
            var monitor = MonitorService.FindOrPrimary(_settings.Settings.Crosshair.MonitorDeviceName);
            var image = Render(text, warning, monitor.DpiScale);
            _window ??= new LayeredWindow(WindowTitle);
            var left = monitor.Left + (monitor.Width - image.Width) / 2;
            var top = monitor.Top + (int)(monitor.Height * 0.06);
            _window.SetImage(image, left, top);
            _window.Show();
            _hideTimer.Stop();
            _hideTimer.Interval = TimeSpan.FromSeconds(warning ? 4 : 1.8);
            _hideTimer.Start();
        }
        catch (Exception ex)
        {
            Logger.Warn("On-screen hint could not be shown", ex);
        }
    }

    private void Hide()
    {
        _hideTimer.Stop();
        _window?.Hide();
    }

    /// <summary>Draws a rounded pill with the text. Must run on the UI thread.</summary>
    internal static PixelImage Render(string text, bool warning, double scale)
    {
        var k = Math.Max(0.75, scale);
        var accent = warning ? Color.FromRgb(0xFF, 0xB5, 0x47) : ThemeService.AccentColor;
        var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal),
            16 * k, new SolidColorBrush(Color.FromRgb(0xF0, 0xF3, 0xF9)), null, TextFormattingMode.Ideal, 1.0);

        var padX = 18 * k;
        var padY = 10 * k;
        var dot = 8 * k;
        var width = (int)Math.Ceiling(padX * 2 + dot + 10 * k + formatted.WidthIncludingTrailingWhitespace);
        var height = (int)Math.Ceiling(padY * 2 + formatted.Height);

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 0x17, 0x19, 0x1F)),
                new Pen(new SolidColorBrush(Color.FromArgb(160, accent.R, accent.G, accent.B)), Math.Max(1, k)),
                new Rect(0.5, 0.5, width - 1, height - 1), height / 2.0, height / 2.0);
            dc.DrawEllipse(new SolidColorBrush(accent), null, new Point(padX + dot / 2, height / 2.0), dot / 2, dot / 2);
            dc.DrawText(formatted, new Point(padX + dot + 10 * k, padY));
        }

        var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var pixels = new byte[width * height * 4];
        rtb.CopyPixels(pixels, width * 4, 0);
        return new PixelImage(width, height, pixels);
    }

    public void Dispose()
    {
        _hideTimer.Stop();
        _window?.Dispose();
        _window = null;
    }
}
