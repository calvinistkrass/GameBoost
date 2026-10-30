using System.Globalization;
using System.Windows.Media;

namespace GameBoost.Infrastructure;

/// <summary>Helpers for colors in #RRGGBB format.</summary>
public static class ColorUtil
{
    /// <summary>Normalizes "#RGB", "RRGGBB", "#RRGGBB" or "#AARRGGBB" to "#RRGGBB". Returns null for invalid input.</summary>
    public static string? Normalize(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var s = input.Trim().TrimStart('#');

        if (s.Length == 3)
            s = string.Concat(s[0], s[0], s[1], s[1], s[2], s[2]);
        else if (s.Length == 8)
            s = s[2..];

        if (s.Length != 6) return null;
        if (!int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _)) return null;
        return "#" + s.ToUpperInvariant();
    }

    public static Color ToColor(string? hex, Color fallback)
    {
        var n = Normalize(hex);
        if (n == null) return fallback;
        var v = int.Parse(n[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return Color.FromRgb((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public static string ToHex(byte r, byte g, byte b) => $"#{r:X2}{g:X2}{b:X2}";
}
