using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace GameBoost.Infrastructure;

/// <summary>Reads program icons from .exe files and caches them (thread-safe, frozen).</summary>
public static class IconCache
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object Gate = new();

    public static ImageSource? Get(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        lock (Gate)
        {
            if (Cache.TryGetValue(path, out var cached)) return cached;
        }

        ImageSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    result = source;
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Could not load icon for '{path}': {ex.Message}");
        }

        lock (Gate)
        {
            Cache[path] = result;
        }
        return result;
    }
}
