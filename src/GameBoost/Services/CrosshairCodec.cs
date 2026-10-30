using System.Globalization;
using System.Text;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>
/// Import/Export eines Crosshairs als kurzer Code ("GB1-…").
/// Inhalt: Base64Url von "Stil,Farbe,Deckkraft%,Länge,Stärke,Gap,Punkt,Outline,OutlineFarbe,OutlineStärke,OffsetX,OffsetY".
/// </summary>
public static class CrosshairCodec
{
    public const string Prefix = "GB1-";

    public static string Encode(CrosshairConfig c)
    {
        var raw = string.Join(",",
            (int)c.Style,
            c.Color.TrimStart('#'),
            (int)Math.Round(c.Opacity * 100),
            c.Length,
            c.Thickness,
            c.Gap,
            c.DotSize,
            c.Outline ? 1 : 0,
            c.OutlineColor.TrimStart('#'),
            c.OutlineThickness,
            c.OffsetX,
            c.OffsetY);

        var b64 = Convert.ToBase64String(Encoding.ASCII.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return Prefix + b64;
    }

    public static bool TryDecode(string? code, out CrosshairConfig config)
    {
        config = new CrosshairConfig();
        if (string.IsNullOrWhiteSpace(code)) return false;

        var s = new string(code.Where(ch => !char.IsWhiteSpace(ch)).ToArray());
        if (!s.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)) return false;
        s = s[Prefix.Length..].Replace('-', '+').Replace('_', '/');
        s = s.PadRight(s.Length + (4 - s.Length % 4) % 4, '=');

        string raw;
        try
        {
            raw = Encoding.ASCII.GetString(Convert.FromBase64String(s));
        }
        catch (FormatException)
        {
            return false;
        }

        var p = raw.Split(',');
        if (p.Length < 12) return false;

        var ic = CultureInfo.InvariantCulture;
        if (!int.TryParse(p[0], NumberStyles.Integer, ic, out var style) || !Enum.IsDefined(typeof(CrosshairStyle), style)) return false;
        if (!int.TryParse(p[2], NumberStyles.Integer, ic, out var opacity)) return false;
        int[] idx = { 3, 4, 5, 6, 7, 9, 10, 11 };
        var ints = new int[idx.Length];
        for (var i = 0; i < idx.Length; i++)
        {
            if (!int.TryParse(p[idx[i]], NumberStyles.Integer, ic, out ints[i])) return false;
        }

        var color = Infrastructure.ColorUtil.Normalize(p[1]);
        var outlineColor = Infrastructure.ColorUtil.Normalize(p[8]);
        if (color == null || outlineColor == null) return false;

        config = new CrosshairConfig
        {
            Style = (CrosshairStyle)style,
            Color = color,
            Opacity = opacity / 100.0,
            Length = ints[0],
            Thickness = ints[1],
            Gap = ints[2],
            DotSize = ints[3],
            Outline = ints[4] != 0,
            OutlineColor = outlineColor,
            OutlineThickness = ints[5],
            OffsetX = ints[6],
            OffsetY = ints[7]
        };
        return true;
    }
}
