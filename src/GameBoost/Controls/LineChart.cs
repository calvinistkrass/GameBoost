using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace GameBoost.Controls;

/// <summary>Minimal line chart for session timelines (no axes library needed).</summary>
public sealed class LineChart : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(IList<float>), typeof(LineChart), new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
        nameof(Stroke), typeof(Brush), typeof(LineChart), new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GridBrushProperty = DependencyProperty.Register(
        nameof(GridBrush), typeof(Brush), typeof(LineChart), new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty UnitProperty = DependencyProperty.Register(
        nameof(Unit), typeof(string), typeof(LineChart), new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));

    public IList<float>? Values
    {
        get => (IList<float>?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Stroke
    {
        get => (Brush)GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public Brush GridBrush
    {
        get => (Brush)GetValue(GridBrushProperty);
        set => SetValue(GridBrushProperty, value);
    }

    public string Unit
    {
        get => (string)GetValue(UnitProperty);
        set => SetValue(UnitProperty, value);
    }

    protected override void OnRender(DrawingContext dc)
    {
        var w = ActualWidth;
        var h = ActualHeight;
        if (w < 20 || h < 20) return;
        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, w, h));

        var values = Values?.Where(v => !float.IsNaN(v)).ToList();
        const double labelWidth = 34;
        var plot = new Rect(labelWidth, 6, Math.Max(1, w - labelWidth), Math.Max(1, h - 12));
        var gridPen = new Pen(GridBrush, 1) { DashStyle = new DashStyle(new double[] { 2, 4 }, 0) };

        double min = 0, max = 1;
        if (values is { Count: > 0 })
        {
            max = values.Max();
            min = Math.Max(0, values.Min() * 0.85);
            if (max - min < 1) max = min + 1;
            max *= 1.05;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        for (var i = 0; i <= 3; i++)
        {
            var y = plot.Top + plot.Height * i / 3;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = max - (max - min) * i / 3;
            var text = new FormattedText($"{value:0}{Unit}", CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI"), 10, GridBrush, dpi);
            dc.DrawText(text, new Point(0, y - text.Height / 2));
        }

        if (values is not { Count: > 1 }) return;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            for (var i = 0; i < values.Count; i++)
            {
                var p = new Point(plot.Left + plot.Width * i / (values.Count - 1),
                    plot.Bottom - (values[i] - min) / (max - min) * plot.Height);
                if (i == 0) ctx.BeginFigure(p, false, false);
                else ctx.LineTo(p, true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, new Pen(Stroke, 2) { LineJoin = PenLineJoin.Round }, geometry);
    }
}
