using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Vixel.Core.Formats;

/// <summary>The native .vixel format: palette-indexed JSON, version 1.</summary>
public static class VixelFile
{
    public const int CurrentVersion = 1;

    public static string Save(Canvas canvas, Palette palette, string name)
    {
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
                ["created"] = DateTimeOffset.UtcNow.ToString("o"),
                ["modified"] = DateTimeOffset.UtcNow.ToString("o"),
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

    public static (Canvas Canvas, Palette Palette, string Name) Load(string json)
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
                    canvas.SetPixel(x, y, index);
        }

        return (canvas, palette, name);
    }
}

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
    public static (int W, int H) ReadDimensions(byte[] png)
    {
        using var image = Image.Load(png);
        return (image.Width, image.Height);
    }

    public static (int R, int G, int B, int A) ReadPixel(byte[] png, int x, int y)
    {
        using var image = Image.Load<Rgba32>(png);
        var p = image[x, y];
        return (p.R, p.G, p.B, p.A);
    }
}

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
        var doc = JsonNode.Parse(json)!.AsObject();
        var piskel = doc["piskel"]!.AsObject();
        var width = piskel["width"]!.GetValue<int>();
        var height = piskel["height"]!.GetValue<int>();
        var frames = piskel["frames"]!.AsArray();
        if (frames.Count == 0) throw new InvalidDataException("piskel file has no frames");

        var dataUri = frames[0]!["dataUri"]!.GetValue<string>();
        var png = Convert.FromBase64String(dataUri[(dataUri.IndexOf(',') + 1)..]);

        using var image = Image.Load<Rgba32>(png);
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

/// <summary>Pixquare (.px): zlib-compressed RGBA layer data, single layer for export.</summary>
public static class PxFormat
{
    private const string Magic = "PX";

    public static byte[] Export(Canvas canvas, Palette palette)
    {
        var rgba = new byte[canvas.Width * canvas.Height * 4];
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var offset = (y * canvas.Width + x) * 4;
                if (canvas[x, y] is { } i)
                {
                    var c = palette.Colors[i];
                    rgba[offset] = c.R;
                    rgba[offset + 1] = c.G;
                    rgba[offset + 2] = c.B;
                    rgba[offset + 3] = 255;
                }
            }
        }

        using var output = new MemoryStream();
        using (var writer = new BinaryWriter(output, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic.ToCharArray());
            writer.Write(canvas.Width);
            writer.Write(canvas.Height);
            writer.Write(rgba.Length);
            var compressed = ZlibCompress(rgba);
            writer.Write(compressed.Length);
            writer.Write(compressed);
        }
        return output.ToArray();
    }

    public static (Canvas Canvas, Palette Palette) Import(byte[] data)
    {
        using var input = new MemoryStream(data);
        using var reader = new BinaryReader(input);
        if (new string(reader.ReadChars(2)) != Magic)
            throw new InvalidDataException("not a px file");
        var width = reader.ReadInt32();
        var height = reader.ReadInt32();
        var length = reader.ReadInt32();
        var compressedLength = reader.ReadInt32();
        var rgba = ZlibDecompress(reader.ReadBytes(compressedLength), length);

        var canvas = new Canvas(width, height);
        var palette = new Palette();
        for (var i = 0; i < width * height; i++)
        {
            var offset = i * 4;
            if (rgba[offset + 3] == 0) continue;
            var index = palette.AddColor(new Rgb(rgba[offset], rgba[offset + 1], rgba[offset + 2]));
            canvas.SetPixel(i % width, i / width, index);
        }
        return (canvas, palette);
    }

    internal static byte[] ZlibCompress(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return stream.ToArray();
    }

    internal static byte[] ZlibDecompress(byte[] data, int expectedLength)
    {
        using var stream = new MemoryStream(data);
        using var zlib = new ZLibStream(stream, CompressionMode.Decompress);
        using var result = new MemoryStream();
        zlib.CopyTo(result);
        var bytes = result.ToArray();
        if (bytes.Length != expectedLength)
            throw new InvalidDataException($"decompressed {bytes.Length} bytes, expected {expectedLength}");
        return bytes;
    }
}

/// <summary>
/// Minimal Aseprite (.ase) reader/writer: single layer, single frame, RGBA cels.
/// The writer emits only the chunks real apps require; candidate for upstream PR to AsepriteDotNet.
/// </summary>
public static class AseFormat
{
    private const ushort FileMagic = 0xA5E0;
    private const ushort FrameMagic = 0xF1FA;

    public static byte[] Export(Canvas canvas, Palette palette)
    {
        using var output = new MemoryStream();
        using var writer = new BinaryWriter(output);

        var celPixels = new byte[canvas.Width * canvas.Height * 4];
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var offset = (y * canvas.Width + x) * 4;
                if (canvas[x, y] is { } i)
                {
                    var c = palette.Colors[i];
                    celPixels[offset] = c.R;
                    celPixels[offset + 1] = c.G;
                    celPixels[offset + 2] = c.B;
                    celPixels[offset + 3] = 255;
                }
            }
        }

        var compressedCel = PxFormat.ZlibCompress(celPixels);

        // Chunk bodies
        var layerChunk = BuildLayerChunk();
        var celChunk = BuildCelChunk(compressedCel, canvas.Width, canvas.Height);
        var paletteChunk = BuildPaletteChunk(palette);

        var frameBytes = 16 + ChunkTotal(layerChunk) + ChunkTotal(celChunk) + ChunkTotal(paletteChunk);
        var fileBytes = 128 + frameBytes;

        // Header (128 bytes)
        writer.Write(fileBytes);           // DWORD file size
        writer.Write(FileMagic);           // WORD magic
        writer.Write((ushort)1);           // WORD frames
        writer.Write((ushort)canvas.Width);
        writer.Write((ushort)canvas.Height);
        writer.Write((ushort)32);          // WORD color depth: RGBA
        writer.Write((uint)0);             // DWORD flags
        writer.Write((ushort)100);         // WORD speed
        writer.Write((uint)0);             // DWORD (0, should be 0)
        writer.Write((uint)0);             // DWORD (0)
        writer.Write((byte)0);             // transparent palette entry
        writer.Write(new byte[3]);         // ignore
        writer.Write((ushort)palette.Colors.Count); // WORD color count
        writer.Write((byte)1);             // pixel width
        writer.Write((byte)1);             // pixel height
        writer.Write((short)0);            // grid x
        writer.Write((short)0);            // grid y
        writer.Write((ushort)0);           // grid width
        writer.Write((ushort)0);           // grid height
        writer.Write(new byte[84]);        // reserved

        // Frame header (16 bytes)
        writer.Write(frameBytes);          // DWORD bytes in frame
        writer.Write(FrameMagic);
        writer.Write((ushort)3);           // old chunk count
        writer.Write((ushort)100);         // frame duration ms
        writer.Write(new byte[2]);         // reserved
        writer.Write((uint)3);             // new chunk count

        WriteChunk(writer, 0x2004, layerChunk);
        WriteChunk(writer, 0x2005, celChunk);
        WriteChunk(writer, 0x2019, paletteChunk);

        writer.Flush();
        return output.ToArray();

        static int ChunkTotal(byte[] body) => 6 + body.Length;

        void WriteChunk(BinaryWriter w, ushort type, byte[] body)
        {
            w.Write(6 + body.Length);      // DWORD chunk size
            w.Write(type);                 // WORD chunk type
            w.Write(body);
        }

        static byte[] BuildLayerChunk()
        {
            using var s = new MemoryStream();
            using var w = new BinaryWriter(s);
            w.Write((ushort)0);            // flags (visible)
            w.Write((ushort)0);            // type: normal
            w.Write((ushort)0);            // child level
            w.Write((ushort)0);            // default width (ignored)
            w.Write((ushort)0);            // default height
            w.Write((ushort)0);            // blend mode: normal
            w.Write((byte)255);            // opacity
            w.Write(new byte[3]);          // reserved
            WriteAseString(w, "Layer 1");
            w.Flush();
            return s.ToArray();
        }

        static byte[] BuildCelChunk(byte[] compressedPixels, int width, int height)
        {
            using var s = new MemoryStream();
            using var w = new BinaryWriter(s);
            w.Write((ushort)0);            // layer index
            w.Write((short)0);             // x
            w.Write((short)0);             // y
            w.Write((byte)255);            // opacity
            w.Write((ushort)2);            // cel type: compressed image
            w.Write((short)0);             // z-index
            w.Write(new byte[5]);          // reserved
            w.Write((ushort)width);
            w.Write((ushort)height);
            w.Write(compressedPixels);
            w.Flush();
            return s.ToArray();
        }

        static byte[] BuildPaletteChunk(Palette p)
        {
            using var s = new MemoryStream();
            using var w = new BinaryWriter(s);
            var count = Math.Max(p.Colors.Count, 0);
            w.Write(count);                // DWORD palette size
            w.Write(0);                    // DWORD first index
            w.Write(count - 1 < 0 ? 0 : count - 1); // DWORD last index
            w.Write(new byte[8]);          // reserved
            foreach (var c in p.Colors)
            {
                w.Write((ushort)0);        // flags (no name)
                w.Write(c.R);
                w.Write(c.G);
                w.Write(c.B);
                w.Write((byte)255);        // alpha
            }
            w.Flush();
            return s.ToArray();
        }

        static void WriteAseString(BinaryWriter w, string value)
        {
            w.Write((ushort)value.Length);
            w.Write(System.Text.Encoding.UTF8.GetBytes(value));
        }
    }

    public static (Canvas Canvas, Palette Palette) Import(byte[] data)
    {
        if (data.Length < 128) throw new InvalidDataException("too short for ase header");
        using var input = new MemoryStream(data);
        using var reader = new BinaryReader(input);

        reader.ReadUInt32(); // file size
        if (reader.ReadUInt16() != FileMagic) throw new InvalidDataException("bad ase magic");
        var frames = reader.ReadUInt16();
        var width = reader.ReadUInt16();
        var height = reader.ReadUInt16();
        var depth = reader.ReadUInt16();
        if (depth != 32) throw new InvalidDataException($"only 32bpp RGBA supported (got {depth})");
        reader.BaseStream.Seek(128, SeekOrigin.Begin);

        var canvas = new Canvas(width, height);
        var palette = new Palette();
        Rgb[]? paletteEntries = null;

        for (var f = 0; f < frames; f++)
        {
            var frameStart = reader.BaseStream.Position;
            var frameBytes = reader.ReadUInt32();
            if (reader.ReadUInt16() != FrameMagic) throw new InvalidDataException("bad frame magic");
            var oldChunks = reader.ReadUInt16();
            reader.ReadUInt16(); // duration
            reader.ReadBytes(2);
            var newChunks = reader.ReadUInt32();
            var chunkCount = newChunks != 0 ? (int)newChunks : oldChunks;

            for (var c = 0; c < chunkCount; c++)
            {
                var chunkStart = reader.BaseStream.Position;
                var chunkSize = reader.ReadUInt32();
                var chunkType = reader.ReadUInt16();

                if (f == 0 && chunkType == 0x2005) // cel — first frame only (flatten)
                {
                    ReadCel(reader, canvas, palette);
                }
                else if (f == 0 && chunkType == 0x2019) // palette
                {
                    paletteEntries = ReadPaletteChunk(reader);
                }

                reader.BaseStream.Seek(chunkStart + chunkSize, SeekOrigin.Begin);
            }

            reader.BaseStream.Seek(frameStart + frameBytes, SeekOrigin.Begin);
        }

        return (canvas, palette);
    }

    private static void ReadCel(BinaryReader reader, Canvas canvas, Palette palette)
    {
        reader.ReadUInt16(); // layer index
        var celX = reader.ReadInt16();
        var celY = reader.ReadInt16();
        reader.ReadByte();   // opacity
        var celType = reader.ReadUInt16();
        reader.ReadInt16();  // z-index
        reader.ReadBytes(5); // reserved

        if (celType != 2) return; // only compressed image cels in v1
        var w = reader.ReadUInt16();
        var h = reader.ReadUInt16();
        var rgba = PxFormat.ZlibDecompress(
            reader.ReadBytes((int)(reader.BaseStream.Length - reader.BaseStream.Position)),
            w * h * 4);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var offset = (y * w + x) * 4;
                if (rgba[offset + 3] == 0) continue;
                var tx = celX + x;
                var ty = celY + y;
                if (tx < 0 || tx >= canvas.Width || ty < 0 || ty >= canvas.Height) continue;
                var index = palette.AddColor(new Rgb(rgba[offset], rgba[offset + 1], rgba[offset + 2]));
                canvas.SetPixel(tx, ty, index);
            }
        }
    }

    private static Rgb[] ReadPaletteChunk(BinaryReader reader)
    {
        var size = reader.ReadInt32();
        var first = reader.ReadInt32();
        reader.ReadInt32(); // last
        reader.ReadBytes(8);
        var entries = new Rgb[size];
        for (var i = first; i < size; i++)
        {
            var flags = reader.ReadUInt16();
            var r = reader.ReadByte();
            var g = reader.ReadByte();
            var b = reader.ReadByte();
            reader.ReadByte(); // alpha
            if ((flags & 1) != 0) // has name
            {
                var len = reader.ReadUInt16();
                reader.ReadBytes(len);
            }
            entries[i] = new Rgb(r, g, b);
        }
        return entries;
    }
}
