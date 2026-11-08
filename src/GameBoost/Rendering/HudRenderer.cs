using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameBoost.Models;
using GameBoost.Services;
using GameBoost.Services.Performance;

namespace GameBoost.Rendering;

/// <summary>Draws the performance overlay (HUD) into a premultiplied BGRA image. Must run on the UI thread.</summary>
public static class HudRenderer
{
    private static readonly Typeface LabelFace = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface ValueFace = new(new FontFamily("Bahnschrift, Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);

    private static Color Accent => ThemeService.AccentColor;
    private static readonly Color Text = Color.FromRgb(0xF2, 0xF5, 0xFA);
    private static readonly Color Dim = Color.FromRgb(0x8D, 0x97, 0xAB);
    private static readonly Color Warn = Color.FromRgb(0xFF, 0xB5, 0x47);
    private static readonly Color Hot = Color.FromRgb(0xFF, 0x4D, 0x6A);

    private sealed record Row(string Label, string Value, Color ValueColor, string? Suffix = null, bool Big = false);

    /// <param name="pixelScale">Final scale in physical pixels (HUD size × monitor DPI).</param>
    public static PixelImage Render(PerformanceSnapshot s, HudSettings settings, double pixelScale, string fpsAdminText = "admin")
    {
        var rows = BuildRows(s, settings, fpsAdminText);
        var k = Math.Max(0.5, pixelScale);
        var font = 15 * k;
        var bigFont = 22 * k;
        var padX = 12 * k;
        var padY = 9 * k;
        var gap = 14 * k;
        var lineGap = 3 * k;

        var labelTexts = rows.Select(r => Make(r.Label, LabelFace, font * 0.8, Dim)).ToList();
        var valueTexts = rows.Select(r => Make(r.Value, ValueFace, r.Big ? bigFont : font, r.ValueColor)).ToList();
        var suffixTexts = rows.Select(r => r.Suffix == null ? null : Make(r.Suffix, LabelFace, font * 0.8, Dim)).ToList();

        var labelWidth = labelTexts.Count == 0 ? 0 : labelTexts.Max(t => t.WidthIncludingTrailingWhitespace);
        double contentWidth = 0, contentHeight = 0;
        var rowHeights = new List<double>();
        for (var i = 0; i < rows.Count; i++)
        {
            var w = labelWidth + gap + valueTexts[i].WidthIncludingTrailingWhitespace
                    + (suffixTexts[i] != null ? 5 * k + suffixTexts[i]!.WidthIncludingTrailingWhitespace : 0);
            contentWidth = Math.Max(contentWidth, w);
            var h = Math.Max(valueTexts[i].Height, labelTexts[i].Height);
            rowHeights.Add(h);
            contentHeight += h + (i > 0 ? lineGap : 0);
        }

        var width = (int)Math.Ceiling(contentWidth + 2 * padX + 3 * k);
        var height = (int)Math.Ceiling(contentHeight + 2 * padY);
        width = Math.Max(width, (int)(60 * k));
        height = Math.Max(height, (int)(24 * k));

        var visual = new DrawingVisual();
        TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Grayscale);
        using (var dc = visual.RenderOpen())
        {
            var bgAlpha = (byte)Math.Round(settings.BackgroundOpacity / 100.0 * 230);
            if (bgAlpha > 0)
            {
                var bg = new SolidColorBrush(Color.FromArgb(bgAlpha, 0x12, 0x14, 0x19));
                var border = new Pen(new SolidColorBrush(Color.FromArgb((byte)(bgAlpha / 2), 0x2F, 0x3A, 0x50)), Math.Max(1, k));
                dc.DrawRoundedRectangle(bg, border, new Rect(0.5, 0.5, width - 1, height - 1), 8 * k, 8 * k);
                // accent bar on the left
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(Math.Max(bgAlpha, (byte)120), Accent.R, Accent.G, Accent.B)), null,
                    new Rect(0, padY, 3 * k, Math.Max(1, height - 2 * padY)), 1.5 * k, 1.5 * k);
            }

            var y = padY;
            for (var i = 0; i < rows.Count; i++)
            {
                var h = rowHeights[i];
                var baseX = padX + 3 * k;
                // Labels and values share a baseline per row.
                var valueY = y + (h - valueTexts[i].Height) / 2;
                var labelY = valueY + valueTexts[i].Baseline - labelTexts[i].Baseline;
                DrawOutlined(dc, labelTexts[i], new Point(baseX, labelY), bgAlpha);
                var vx = baseX + labelWidth + gap;
                DrawOutlined(dc, valueTexts[i], new Point(vx, valueY), bgAlpha);
                if (suffixTexts[i] != null)
                {
                    var sy = valueY + valueTexts[i].Baseline - suffixTexts[i]!.Baseline;
                    DrawOutlined(dc, suffixTexts[i]!, new Point(vx + valueTexts[i].WidthIncludingTrailingWhitespace + 5 * k, sy), bgAlpha);
                }
                y += h + lineGap;
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        var pixels = new byte[width * height * 4];
        rtb.CopyPixels(pixels, width * 4, 0);
        return new PixelImage(width, height, pixels);
    }

    public static BitmapSource ToBitmap(PixelImage image)
    {
        var bmp = BitmapSource.Create(image.Width, image.Height, 96, 96, PixelFormats.Pbgra32, null, image.Pixels, image.Stride);
        bmp.Freeze();
        return bmp;
    }

    private static List<Row> BuildRows(PerformanceSnapshot s, HudSettings o, string fpsAdminText)
    {
        var ic = CultureInfo.InvariantCulture;
        var rows = new List<Row>();

        if (o.ShowFps)
        {
            if (s.Fps is { } fps)
                rows.Add(new Row("FPS", fps.ToString("0", ic), Accent, s.FrameTimeMs is { } ms ? ms.ToString("0.0", ic) + " ms" : null, Big: true));
            else
                rows.Add(new Row("FPS", s.FpsNeedsAdmin ? fpsAdminText : "–", s.FpsNeedsAdmin ? Dim : Text, Big: !s.FpsNeedsAdmin));
        }

        if (o.ShowGpu || o.ShowGpuTemperature)
        {
            var parts = new List<string>();
            var color = Text;
            if (o.ShowGpu) parts.Add(s.GpuLoad is { } g ? g.ToString("0", ic) + " %" : "–");
            if (o.ShowGpuTemperature)
            {
                parts.Add(s.GpuTemperature is { } t ? t.ToString("0", ic) + " °C" : "– °C");
                if (s.GpuTemperature >= 85) color = Hot;
                else if (s.GpuTemperature >= 75) color = Warn;
            }
            rows.Add(new Row("GPU", string.Join("  ", parts), color));
        }

        if (o.ShowVram) rows.Add(new Row("VRAM", s.VramUsedGb is { } v ? v.ToString("0.0", ic) + " GB" : "–", Text));

        if (o.ShowCpu)
        {
            var color = s.CpuLoad >= 95 ? Warn : Text;
            rows.Add(new Row("CPU", s.CpuLoad is { } c ? c.ToString("0", ic) + " %" : "–", color));
        }

        if (o.ShowRam && s.RamTotalGb > 0)
        {
            var color = s.RamUsedGb / s.RamTotalGb >= 0.9 ? Warn : Text;
            rows.Add(new Row("RAM", s.RamUsedGb.ToString("0.0", ic) + " / " + s.RamTotalGb.ToString("0", ic) + " GB", color));
        }

        if (o.ShowClock) rows.Add(new Row("TIME", s.Timestamp.ToString("HH:mm", ic), Text));

        if (rows.Count == 0) rows.Add(new Row("GAMEBOOST", "", Text));
        return rows;
    }

    private static FormattedText Make(string text, Typeface face, double size, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, face, size, brush, null,
            TextFormattingMode.Ideal, 1.0);
    }

    /// <summary>Without a background the text gets a dark outline so it stays readable over bright scenes.</summary>
    private static void DrawOutlined(DrawingContext dc, FormattedText text, Point origin, byte bgAlpha)
    {
        if (bgAlpha < 90)
        {
            var geometry = text.BuildGeometry(origin);
            dc.DrawGeometry(null, new Pen(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), Math.Max(2, text.Height / 7)) { LineJoin = PenLineJoin.Round }, geometry);
        }
        dc.DrawText(text, origin);
    }
}
