using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Controls;

/// <summary>
/// Draws a pixel-exact crosshair. One unit equals one physical screen pixel
/// (multiplied by <see cref="Zoom"/>), independent of Windows display scaling.
/// </summary>
public sealed class CrosshairRenderer : FrameworkElement
{
    public static readonly DependencyProperty ConfigProperty = DependencyProperty.Register(
        nameof(Config), typeof(CrosshairConfig), typeof(CrosshairRenderer),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnConfigChanged));

    public static readonly DependencyProperty ZoomProperty = DependencyProperty.Register(
        nameof(Zoom), typeof(double), typeof(CrosshairRenderer),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ApplyOffsetProperty = DependencyProperty.Register(
        nameof(ApplyOffset), typeof(bool), typeof(CrosshairRenderer),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public CrosshairConfig? Config
    {
        get => (CrosshairConfig?)GetValue(ConfigProperty);
        set => SetValue(ConfigProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public bool ApplyOffset
    {
        get => (bool)GetValue(ApplyOffsetProperty);
        set => SetValue(ApplyOffsetProperty, value);
    }

    private static void OnConfigChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var renderer = (CrosshairRenderer)d;
        if (e.OldValue is CrosshairConfig oldConfig) oldConfig.PropertyChanged -= renderer.OnConfigPropertyChanged;
        if (e.NewValue is CrosshairConfig newConfig) newConfig.PropertyChanged += renderer.OnConfigPropertyChanged;
    }

    private void OnConfigPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (Dispatcher.CheckAccess()) InvalidateVisual();
        else Dispatcher.BeginInvoke(InvalidateVisual);
    }

    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        try
        {
            var cfg = Config;
            if (cfg == null || ActualWidth <= 0 || ActualHeight <= 0) return;
            Render(dc, cfg);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not draw crosshair", ex);
        }
    }

    private void Render(DrawingContext dc, CrosshairConfig cfg)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var zoom = Math.Max(1, Zoom);

        // Center in physical pixels (integer) → back to WPF units.
        var centerPxX = Math.Floor(ActualWidth * dpi.DpiScaleX / 2);
        var centerPxY = Math.Floor(ActualHeight * dpi.DpiScaleY / 2);
        dc.PushTransform(new TranslateTransform(centerPxX / dpi.DpiScaleX, centerPxY / dpi.DpiScaleY));
        dc.PushTransform(new ScaleTransform(zoom / dpi.DpiScaleX, zoom / dpi.DpiScaleY));
        dc.PushOpacity(cfg.Opacity);

        var fill = Freeze(new SolidColorBrush(ColorUtil.ToColor(cfg.Color, Colors.Lime)));
        var outline = Freeze(new SolidColorBrush(ColorUtil.ToColor(cfg.OutlineColor, Colors.Black)));

        var ox = ApplyOffset ? cfg.OffsetX : 0;
        var oy = ApplyOffset ? cfg.OffsetY : 0;
        var shapes = BuildShapes(cfg, ox, oy);

        if (cfg.Outline)
        {
            var o = cfg.OutlineThickness;
            foreach (var s in shapes)
            {
                switch (s)
                {
                    case RectShape r:
                        dc.DrawRectangle(outline, null, new Rect(r.X - o, r.Y - o, r.W + 2 * o, r.H + 2 * o));
                        break;
                    case EllipseShape e when e.StrokeThickness <= 0:
                        dc.DrawEllipse(outline, null, new Point(e.Cx, e.Cy), e.R + o, e.R + o);
                        break;
                    case EllipseShape e:
                        dc.DrawEllipse(null, Freeze(new Pen(outline, e.StrokeThickness + 2 * o)), new Point(e.Cx, e.Cy), e.R, e.R);
                        break;
                }
            }
        }

        foreach (var s in shapes)
        {
            switch (s)
            {
                case RectShape r:
                    dc.DrawRectangle(fill, null, new Rect(r.X, r.Y, r.W, r.H));
                    break;
                case EllipseShape e when e.StrokeThickness <= 0:
                    dc.DrawEllipse(fill, null, new Point(e.Cx, e.Cy), e.R, e.R);
                    break;
                case EllipseShape e:
                    dc.DrawEllipse(null, Freeze(new Pen(fill, e.StrokeThickness)), new Point(e.Cx, e.Cy), e.R, e.R);
                    break;
            }
        }

        dc.Pop();
        dc.Pop();
        dc.Pop();
    }

    private abstract record Shape;
    private sealed record RectShape(double X, double Y, double W, double H) : Shape;
    private sealed record EllipseShape(double Cx, double Cy, double R, double StrokeThickness) : Shape;

    /// <summary>
    /// Builds the shapes relative to the center (0,0) in pixels. With odd thickness the center lies
    /// on pixel [0,1), with even thickness between pixels -1 and 0 – this keeps the crosshair symmetric.
    /// </summary>
    private static List<Shape> BuildShapes(CrosshairConfig cfg, int ox, int oy)
    {
        var list = new List<Shape>();
        int t = cfg.Thickness, g = cfg.Gap, l = cfg.Length;
        int odd = t % 2;
        int half = t / 2;

        bool cross = cfg.Style is CrosshairStyle.Cross or CrosshairStyle.CrossWithDot or CrosshairStyle.TShape;
        if (cross)
        {
            list.Add(new RectShape(ox - g - l, oy - half, l, t));                 // left
            list.Add(new RectShape(ox + g + odd, oy - half, l, t));               // right
            list.Add(new RectShape(ox - half, oy + g + odd, t, l));               // bottom
            if (cfg.Style != CrosshairStyle.TShape)
                list.Add(new RectShape(ox - half, oy - g - l, t, l));             // top
        }

        if (cfg.Style == CrosshairStyle.Circle)
        {
            var c = odd * 0.5;
            list.Add(new EllipseShape(ox + c, oy + c, l, t));
        }

        if (cfg.Style is CrosshairStyle.Dot or CrosshairStyle.CrossWithDot)
        {
            var d = cfg.DotSize;
            if (d <= 3)
            {
                list.Add(new RectShape(ox - d / 2, oy - d / 2, d, d));
            }
            else
            {
                var c = (d % 2) * 0.5;
                list.Add(new EllipseShape(ox + c, oy + c, d / 2.0, 0));
            }
        }

        return list;
    }

    private static T Freeze<T>(T freezable) where T : Freezable
    {
        freezable.Freeze();
        return freezable;
    }
}
