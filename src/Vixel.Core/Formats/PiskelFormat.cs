using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Vixel.Core.Formats;

/// <summary>Piskel (.piskel): JSON wrapper around a base64 PNG per frame. Import = first frame.</summary>
public static class PiskelFormat
{
    public static string Export(Canvas canvas, Palette palette, int frameCount = 1)
    {
        var png = PngFormat.Export(canvas, palette, scale: 1);
        var frames = new JsonArray();
        for (var i = 0; i < frameCount; i++)
        {
            frames.Add(new JsonObject
            {
                ["name"] = $"frame{i + 1}",
                ["dataUri"] = $"data:image/png;base64,{Convert.ToBase64String(png)}",
            });
        }

        var doc = new JsonObject
        {
            ["modelVersion"] = 2,
            ["piskel"] = new JsonObject
            {
                ["name"] = "vixel export",
                ["fps"] = 12,
                ["height"] = canvas.Height,
                ["width"] = canvas.Width,
                ["frames"] = frames,
            },
        };
        return doc.ToJsonString();
    }

    public static (Canvas Canvas, Palette Palette) Import(string json)
    {
        JsonObject doc;
        try
        {
            doc = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("piskel root must be a JSON object");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("not valid JSON", e);
        }

        var piskel = doc["piskel"] as JsonObject
            ?? throw new InvalidDataException("missing \"piskel\" object");
        var width = piskel["width"]?.GetValue<int>()
            ?? throw new InvalidDataException("missing piskel width");
        var height = piskel["height"]?.GetValue<int>()
            ?? throw new InvalidDataException("missing piskel height");
        var frames = piskel["frames"] as JsonArray
            ?? throw new InvalidDataException("missing piskel frames");
        if (frames.Count == 0) throw new InvalidDataException("piskel file has no frames");

        var dataUri = (frames[0] as JsonObject)?["dataUri"]?.GetValue<string>()
            ?? throw new InvalidDataException("first piskel frame has no dataUri");
        var comma = dataUri.IndexOf(',');
        if (comma < 0) throw new InvalidDataException("piskel dataUri is not a data URI");
        byte[] png;
        try
        {
            png = Convert.FromBase64String(dataUri[(comma + 1)..]);
        }
        catch (FormatException e)
        {
            throw new InvalidDataException("piskel frame data is not valid base64", e);
        }

        Image<Rgba32> image;
        try
        {
            image = Image.Load<Rgba32>(png);
        }
        catch (Exception e) when (e is UnknownImageFormatException or InvalidImageContentException)
        {
            throw new InvalidDataException("piskel frame data is not a decodable image", e);
        }
        using (image)
        {
            if (image.Width != width || image.Height != height)
                throw new InvalidDataException("frame dimensions do not match header");

            var canvas = new Canvas(width, height);
            var palette = new Palette();
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var p = image[x, y];
                    if (p.A == 0) continue;
                    canvas.SetPixel(x, y, palette.AddColor(new Rgb(p.R, p.G, p.B)));
                }
            }
            return (canvas, palette);
        }
    }
}
