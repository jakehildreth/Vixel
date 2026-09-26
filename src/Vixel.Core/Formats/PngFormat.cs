using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Vixel.Core.Formats;

/// <summary>PNG export via ImageSharp: 1:1 or nearest-neighbor integer upscale. Null → alpha 0.</summary>
public static class PngFormat
{
    public static byte[] Export(Canvas canvas, Palette palette, int scale)
    {
        if (scale < 1) throw new ArgumentOutOfRangeException(nameof(scale));

        using var image = new Image<Rgba32>(canvas.Width * scale, canvas.Height * scale);
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                Rgba32 color = canvas[x, y] is { } i
                    ? new Rgba32(palette.Colors[i].R, palette.Colors[i].G, palette.Colors[i].B, 255)
                    : new Rgba32(0, 0, 0, 0);

                for (var sy = 0; sy < scale; sy++)
                    for (var sx = 0; sx < scale; sx++)
                        image[x * scale + sx, y * scale + sy] = color;
            }
        }

        using var stream = new MemoryStream();
        image.SaveAsPng(stream);
        return stream.ToArray();
    }

    // Test helpers — verify output without depending on our own writer's internals.
    internal static (int W, int H) ReadDimensions(byte[] png)
    {
        using var image = Image.Load(png);
        return (image.Width, image.Height);
    }

    internal static (int R, int G, int B, int A) ReadPixel(byte[] png, int x, int y)
    {
        using var image = Image.Load<Rgba32>(png);
        var p = image[x, y];
        return (p.R, p.G, p.B, p.A);
    }
}
