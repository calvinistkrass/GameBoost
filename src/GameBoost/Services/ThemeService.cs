using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Media;
using GameBoost.Infrastructure;
using GameBoost.Models;

namespace GameBoost.Services;

/// <summary>
/// Applies the chosen theme (dark/light) and accent color. Must run before any window is created.
/// XAML resolves StaticResource references inside a dictionary while loading it, so changing colors afterwards
/// would not reach the styles. Instead the theme is re-read from its XAML source with the new colors filled in
/// and replaces the compiled dictionary.
/// </summary>
public static class ThemeService
{
    public const string DefaultAccent = "#2ED497";

    public static readonly (string Hex, string NameKey)[] Accents =
    {
        (DefaultAccent, "Accent.Green"),
        ("#4F8EF7", "Accent.Blue"),
        ("#9B6BFA", "Accent.Purple"),
        ("#EC5FA6", "Accent.Pink"),
        ("#F2545B", "Accent.Red"),
        ("#F59E3B", "Accent.Orange"),
        ("#22C3E6", "Accent.Cyan")
    };

    private static readonly Dictionary<string, string> LightPalette = new()
    {
        ["C.Bg0"] = "#FFF3F4F6",
        ["C.Bg1"] = "#FFFFFFFF",
        ["C.Bg2"] = "#FFFFFFFF",
        ["C.Bg3"] = "#FFF0F2F5",
        ["C.Bg4"] = "#FFE3E6EB",
        ["C.Border"] = "#FFE1E4E9",
        ["C.BorderStrong"] = "#FFCBD0D7",
        ["C.Text"] = "#FF15171C",
        ["C.TextDim"] = "#FF555B66",
        ["C.TextFaint"] = "#FF878D97"
    };

    public static Color AccentColor { get; private set; } = Color.FromRgb(0x2E, 0xD4, 0x97);
    public static bool IsLight { get; private set; }

    public static void Apply(AppSettings settings)
    {
        try
        {
            IsLight = settings.Theme == "Light";
            AccentColor = ColorUtil.ToColor(settings.AccentColor, AccentColor);
            var isDefault = !IsLight && string.Equals(settings.AccentColor, DefaultAccent, StringComparison.OrdinalIgnoreCase);
            if (isDefault) return; // the compiled theme already matches

            var app = Application.Current;
            if (app == null || app.Resources.MergedDictionaries.Count == 0) return;

            var source = LoadSource();
            if (source == null) return;
            var dictionary = (ResourceDictionary)XamlReader.Parse(Transform(source, IsLight, AccentColor));
            app.Resources.MergedDictionaries[0] = dictionary;
            Logger.Info($"Theme: {settings.Theme}, accent {settings.AccentColor}");
        }
        catch (Exception ex)
        {
            Logger.Warn("Theme could not be applied – using the default theme", ex);
            IsLight = false;
            AccentColor = Color.FromRgb(0x2E, 0xD4, 0x97);
        }
    }

    /// <summary>Fills the theme XAML with the light palette and the accent color (testable without WPF).</summary>
    internal static string Transform(string xaml, bool light, Color accent)
    {
        // XamlReader needs the assembly for local namespaces.
        xaml = Regex.Replace(xaml, "clr-namespace:(GameBoost[\\w.]*)\"", "clr-namespace:$1;assembly=GameBoost\"");

        var colors = light ? new Dictionary<string, string>(LightPalette) : new Dictionary<string, string>();
        colors["C.Accent"] = Hex(255, accent);
        colors["C.Accent2"] = Hex(255, accent);
        var luminance = (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) / 255;
        colors["C.OnAccent"] = luminance > 0.6 ? "#FF06130D" : "#FFFFFFFF";
        foreach (var (key, value) in colors)
            xaml = Regex.Replace(xaml, $"(<Color x:Key=\"{Regex.Escape(key)}\">)#[0-9A-Fa-f]{{8}}(</Color>)", $"${{1}}{value}${{2}}");

        // Colors derived from the accent / dark background that are written inline.
        xaml = xaml.Replace("\"#1A2ED497\"", $"\"{Hex(light ? (byte)0x24 : (byte)0x1A, accent)}\"")
                   .Replace("\"#5500F0A8\"", $"\"{Hex(0x55, accent)}\"");
        if (light)
        {
            xaml = xaml.Replace("\"#0CFFFFFF\"", "\"#00FFFFFF\"")                  // card highlight
                       .Replace("\"#0DFFFFFF\"", "\"#0A000000\"")                  // row hover
                       .Replace("\"#FF15291F\"", $"\"{Hex(0x30, accent)}\"");     // active game mode button
        }
        return xaml;
    }

    private static string Hex(byte alpha, Color c) =>
        string.Create(CultureInfo.InvariantCulture, $"#{alpha:X2}{c.R:X2}{c.G:X2}{c.B:X2}");

    private static string? LoadSource()
    {
        using var stream = typeof(ThemeService).Assembly.GetManifestResourceStream("GameBoost.Theme.xaml");
        if (stream == null)
        {
            Logger.Warn("Theme source not embedded – using the default theme");
            return null;
        }
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
