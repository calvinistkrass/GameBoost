namespace GameBoost.Rendering;

/// <summary>32-bit image in premultiplied BGRA, top-down. Used for layered overlay windows and previews.</summary>
public sealed class PixelImage
{
    public PixelImage(int width, int height)
    {
        Width = Math.Max(1, width);
        Height = Math.Max(1, height);
        Pixels = new byte[Width * Height * 4];
    }

    public PixelImage(int width, int height, byte[] pixels)
    {
        if (pixels.Length != width * height * 4) throw new ArgumentException("Pixel buffer size does not match.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>BGRA, premultiplied alpha, row stride = Width * 4.</summary>
    public byte[] Pixels { get; }

    public int Stride => Width * 4;

    public (byte B, byte G, byte R, byte A) GetPixel(int x, int y)
    {
        var i = (y * Width + x) * 4;
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public int CountVisiblePixels(byte minAlpha = 1)
    {
        var count = 0;
        for (var i = 3; i < Pixels.Length; i += 4)
        {
            if (Pixels[i] >= minAlpha) count++;
        }
        return count;
    }
}
