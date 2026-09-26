using System.Text.Json;
using System.Text.Json.Nodes;

namespace Vixel.Core.Formats;

/// <summary>The native .vixel format: palette-indexed JSON, version 1.</summary>
public static class VixelFile
{
    public const int CurrentVersion = 1;

    public static string Save(Canvas canvas, Palette palette, string name, DateTimeOffset? created = null)
    {
        var now = DateTimeOffset.UtcNow;
        var doc = new JsonObject
        {
            ["version"] = CurrentVersion,
            ["name"] = name,
            ["width"] = canvas.Width,
            ["height"] = canvas.Height,
            ["palette"] = new JsonArray(palette.Colors.Select(c => (JsonNode)c.ToHex()).ToArray()),
            ["rows"] = BuildRows(canvas),
            ["meta"] = new JsonObject
            {
                ["created"] = (created ?? now).ToString("o"),
                ["modified"] = now.ToString("o"),
            },
        };
        return doc.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonArray BuildRows(Canvas canvas)
    {
        var rows = new JsonArray();
        for (var y = 0; y < canvas.Height; y++)
        {
            var row = new JsonArray();
            for (var x = 0; x < canvas.Width; x++)
                row.Add(canvas[x, y] is { } i ? JsonValue.Create(i) : null);
            rows.Add(row);
        }
        return rows;
    }

    public static (Canvas Canvas, Palette Palette, string Name, DateTimeOffset? Created) Load(string json)
    {
        JsonObject doc;
        try
        {
            doc = JsonNode.Parse(json) as JsonObject
                ?? throw new InvalidDataException("root must be a JSON object");
        }
        catch (JsonException e)
        {
            throw new InvalidDataException("not valid JSON", e);
        }

        var version = doc["version"]?.GetValue<int>()
            ?? throw new InvalidDataException("missing version");
        if (version > CurrentVersion)
            throw new InvalidDataException($"file version {version} is newer than supported ({CurrentVersion})");

        var width = doc["width"]?.GetValue<int>() ?? throw new InvalidDataException("missing width");
        var height = doc["height"]?.GetValue<int>() ?? throw new InvalidDataException("missing height");
        var name = doc["name"]?.GetValue<string>() ?? "untitled";
        var created = DateTimeOffset.TryParse(doc["meta"]?["created"]?.GetValue<string>(), out var c) ? c : (DateTimeOffset?)null;

        var palette = new Palette();
        foreach (var hex in doc["palette"]?.AsArray() ?? [])
            palette.AddColor(Rgb.FromHex(hex!.GetValue<string>()));

        var rows = doc["rows"]?.AsArray() ?? throw new InvalidDataException("missing rows");
        if (rows.Count != height)
            throw new InvalidDataException($"rows count {rows.Count} != height {height}");

        var canvas = new Canvas(width, height);
        for (var y = 0; y < height; y++)
        {
            var row = rows[y]!.AsArray();
            if (row.Count != width)
                throw new InvalidDataException($"row {y} count {row.Count} != width {width}");
            for (var x = 0; x < width; x++)
                if (row[x] is JsonValue v && v.TryGetValue<int>(out var index))
                {
                    if (index < 0 || index >= palette.Colors.Count)
                        throw new InvalidDataException($"pixel ({x},{y}) references palette index {index}, palette has {palette.Colors.Count} colors");
                    canvas.SetPixel(x, y, index);
                }
        }

        return (canvas, palette, name, created);
    }
}
