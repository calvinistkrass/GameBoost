using GameBoost.Infrastructure;

namespace GameBoost.Models;

public enum CrosshairStyle
{
    Cross,
    Dot,
    Circle,
    CrossWithDot,
    TShape
}

/// <summary>Alle Parameter eines Crosshairs. Alle Maße in physischen Bildschirmpixeln.</summary>
public sealed class CrosshairConfig : ObservableObject
{
    public const int MaxLength = 60;
    public const int MaxThickness = 12;
    public const int MaxGap = 40;
    public const int MaxDotSize = 12;
    public const int MaxOutlineThickness = 4;
    public const int MaxOffset = 200;

    private CrosshairStyle _style = CrosshairStyle.Cross;
    private string _color = "#00FF7F";
    private double _opacity = 1.0;
    private int _length = 6;
    private int _thickness = 2;
    private int _gap = 3;
    private int _dotSize = 2;
    private bool _outline = true;
    private string _outlineColor = "#000000";
    private int _outlineThickness = 1;
    private int _offsetX;
    private int _offsetY;

    public CrosshairStyle Style
    {
        get => _style;
        set => SetProperty(ref _style, Enum.IsDefined(value) ? value : CrosshairStyle.Cross);
    }

    /// <summary>Farbe als #RRGGBB. Ungültige Werte werden ignoriert.</summary>
    public string Color
    {
        get => _color;
        set => SetProperty(ref _color, ColorUtil.Normalize(value) ?? _color);
    }

    /// <summary>Deckkraft 0.05 – 1.0.</summary>
    public double Opacity
    {
        get => _opacity;
        set => SetProperty(ref _opacity, Math.Round(Math.Clamp(double.IsNaN(value) ? 1 : value, 0.05, 1.0), 2));
    }

    /// <summary>Länge der Linien bzw. Radius beim Kreis.</summary>
    public int Length
    {
        get => _length;
        set => SetProperty(ref _length, Math.Clamp(value, 1, MaxLength));
    }

    public int Thickness
    {
        get => _thickness;
        set => SetProperty(ref _thickness, Math.Clamp(value, 1, MaxThickness));
    }

    public int Gap
    {
        get => _gap;
        set => SetProperty(ref _gap, Math.Clamp(value, 0, MaxGap));
    }

    public int DotSize
    {
        get => _dotSize;
        set => SetProperty(ref _dotSize, Math.Clamp(value, 1, MaxDotSize));
    }

    public bool Outline
    {
        get => _outline;
        set => SetProperty(ref _outline, value);
    }

    public string OutlineColor
    {
        get => _outlineColor;
        set => SetProperty(ref _outlineColor, ColorUtil.Normalize(value) ?? _outlineColor);
    }

    public int OutlineThickness
    {
        get => _outlineThickness;
        set => SetProperty(ref _outlineThickness, Math.Clamp(value, 1, MaxOutlineThickness));
    }

    public int OffsetX
    {
        get => _offsetX;
        set => SetProperty(ref _offsetX, Math.Clamp(value, -MaxOffset, MaxOffset));
    }

    public int OffsetY
    {
        get => _offsetY;
        set => SetProperty(ref _offsetY, Math.Clamp(value, -MaxOffset, MaxOffset));
    }

    public CrosshairConfig Clone()
    {
        var copy = new CrosshairConfig();
        copy.CopyFrom(this);
        return copy;
    }

    public void CopyFrom(CrosshairConfig other)
    {
        Style = other.Style;
        Color = other.Color;
        Opacity = other.Opacity;
        Length = other.Length;
        Thickness = other.Thickness;
        Gap = other.Gap;
        DotSize = other.DotSize;
        Outline = other.Outline;
        OutlineColor = other.OutlineColor;
        OutlineThickness = other.OutlineThickness;
        OffsetX = other.OffsetX;
        OffsetY = other.OffsetY;
    }

    public bool ValueEquals(CrosshairConfig other) =>
        Style == other.Style && Color == other.Color && Math.Abs(Opacity - other.Opacity) < 0.001 &&
        Length == other.Length && Thickness == other.Thickness && Gap == other.Gap &&
        DotSize == other.DotSize && Outline == other.Outline && OutlineColor == other.OutlineColor &&
        OutlineThickness == other.OutlineThickness && OffsetX == other.OffsetX && OffsetY == other.OffsetY;
}

/// <summary>Ein gespeichertes, benanntes Crosshair.</summary>
public sealed class CrosshairPreset : ObservableObject
{
    private string _name = "Preset";

    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? _name : value.Trim());
    }

    public CrosshairConfig Config { get; set; } = new();

    public static CrosshairPreset Create(string name, CrosshairConfig config) => new() { Name = name, Config = config.Clone() };
}

/// <summary>Beispiel-Crosshairs (Standard-Presets und Vorschaubilder im Editor).</summary>
public static class CrosshairSamples
{
    public static CrosshairConfig Cross { get; } = new() { Style = CrosshairStyle.Cross, Color = "#E8ECF4", Length = 7, Thickness = 2, Gap = 3, Outline = false };
    public static CrosshairConfig Dot { get; } = new() { Style = CrosshairStyle.Dot, Color = "#E8ECF4", DotSize = 6, Outline = false };
    public static CrosshairConfig Circle { get; } = new() { Style = CrosshairStyle.Circle, Color = "#E8ECF4", Length = 9, Thickness = 2, Outline = false };
    public static CrosshairConfig CrossWithDot { get; } = new() { Style = CrosshairStyle.CrossWithDot, Color = "#E8ECF4", Length = 6, Thickness = 2, Gap = 4, DotSize = 2, Outline = false };
    public static CrosshairConfig TShape { get; } = new() { Style = CrosshairStyle.TShape, Color = "#E8ECF4", Length = 7, Thickness = 2, Gap = 3, Outline = false };

    public static IEnumerable<CrosshairPreset> DefaultPresets()
    {
        yield return CrosshairPreset.Create("Klassisch Grün", new CrosshairConfig { Style = CrosshairStyle.Cross, Color = "#00FF7F", Length = 6, Thickness = 2, Gap = 3, Outline = true });
        yield return CrosshairPreset.Create("Präzision Cyan", new CrosshairConfig { Style = CrosshairStyle.CrossWithDot, Color = "#00FFFF", Length = 4, Thickness = 1, Gap = 3, DotSize = 1, Outline = true });
        yield return CrosshairPreset.Create("Roter Punkt", new CrosshairConfig { Style = CrosshairStyle.Dot, Color = "#FF3B3B", DotSize = 4, Outline = true });
        yield return CrosshairPreset.Create("Kreis Weiß", new CrosshairConfig { Style = CrosshairStyle.Circle, Color = "#FFFFFF", Length = 10, Thickness = 2, Outline = true });
        yield return CrosshairPreset.Create("T-Form Gelb", new CrosshairConfig { Style = CrosshairStyle.TShape, Color = "#FFE14D", Length = 7, Thickness = 2, Gap = 4, Outline = true });
    }
}
