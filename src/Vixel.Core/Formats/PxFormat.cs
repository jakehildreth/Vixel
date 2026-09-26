using System.IO.Compression;

namespace Vixel.Core.Formats;

/// <summary>
/// Pixquare (.px): layered artwork graph, zlib-compressed premultiplied ARGB frame contents.
/// Import flattens all visible layers of the first frame, topmost last.
/// Export writes a minimal single-layer RGB artwork Pixquare can open.
/// Size fields include their fixed headers (32 bytes; 16 for Entry). Docs: docs.pixquare.art/pixquare-file/binary-specs
/// (header field widths there are stale; real files zero-pad headers to the fixed sizes).
/// </summary>
public static class PxFormat
{
    private const int ArtworkHeaderSize = 64;
    private const int ModelHeaderSize = 32;
    private const int EntryHeaderSize = 16;

    public static byte[] Export(Canvas canvas, Palette palette)
    {
        var pixels = new byte[canvas.Width * canvas.Height * 4];
        for (var y = 0; y < canvas.Height; y++)
        {
            for (var x = 0; x < canvas.Width; x++)
            {
                var offset = (y * canvas.Width + x) * 4;
                if (canvas[x, y] is { } i)
                {
                    var c = palette.Colors[i];
                    pixels[offset] = c.R;
                    pixels[offset + 1] = c.G;
                    pixels[offset + 2] = c.B;
                    pixels[offset + 3] = 255;
                }
            }
        }

        var artworkId = Guid.NewGuid().ToString().ToUpperInvariant();
        var layerId = Guid.NewGuid().ToString().ToUpperInvariant();
        var frameId = Guid.NewGuid().ToString().ToUpperInvariant();
        var contentId = Guid.NewGuid().ToString().ToUpperInvariant();
        var compressed = ZlibCompress(pixels);

        // FrameContent: size(8) idLen(1) ulen(4) clen(4) compat(1) [pad 14] id data
        var frameContent = new byte[ModelHeaderSize + contentId.Length + compressed.Length];
        using (var s = new MemoryStream(frameContent))
        using (var w = new BinaryWriter(s))
        {
            w.Write((ulong)(ModelHeaderSize + contentId.Length + compressed.Length));
            w.Write((byte)contentId.Length);
            w.Write(pixels.Length);
            w.Write(compressed.Length);
            w.Write((byte)1); // compat
            w.BaseStream.Position = ModelHeaderSize;
            WriteRaw(w, contentId);
            w.Write(compressed);
        }

        // Frame: size(4) idLen(1) contentIdLen(1) [pad 26] id dur selected contentId opacity(f16=2: inherit) zindex customdata[]
        var frame = new byte[ModelHeaderSize + frameId.Length + 4 + 1 + contentId.Length + 2 + 2 + 8];
        using (var s = new MemoryStream(frame))
        using (var w = new BinaryWriter(s))
        {
            w.Write(frame.Length - ModelHeaderSize); // content length only
            w.Write((byte)frameId.Length);
            w.Write((byte)contentId.Length);
            w.BaseStream.Position = ModelHeaderSize;
            WriteRaw(w, frameId);
            w.Write(100);          // duration ms
            w.Write(false);        // selected
            WriteRaw(w, contentId);
            w.Write((ushort)0x4002); // float16 2.0 = inherit layer opacity
            w.Write((short)0);       // z-index
            w.Write((ulong)0);       // custom data
        }

        // Layer: size(4) idLen(1) nameLen(1) compat(1) [pad 25] id name [frames] opacity(f16) visible locked selected alphaLocked blendMode linked [crop] [clip] color [fx]
        const string layerName = "Layer 1";
        var layerSize = ModelHeaderSize + layerId.Length + layerName.Length + 8 + frame.Length + 2 + 5 + 2 + 1 + 8 + 8 + 4 + 8;
        var layer = new byte[layerSize];
        using (var s = new MemoryStream(layer))
        using (var w = new BinaryWriter(s))
        {
            w.Write(layerSize - ModelHeaderSize); // content length only (id+name+frames+tail)
            w.Write((byte)layerId.Length);
            w.Write((byte)layerName.Length);
            w.Write((byte)0b00001111); // compat
            w.BaseStream.Position = ModelHeaderSize;
            WriteRaw(w, layerId);
            WriteRaw(w, layerName);
            w.Write((ulong)1);         // frame count
            w.Write(frame);
            w.Write((ushort)0x3C00);   // float16 1.0 opacity
            w.Write(true);             // visible
            w.Write(false);            // locked
            w.Write(false);            // selected
            w.Write(false);            // alpha-locked
            w.Write((ushort)0);        // blend mode normal
            w.Write(false);            // linked
            w.Write((ulong)0);         // cropping masks
            w.Write((ulong)0);         // clipping masks
            w.Write(new byte[4]);      // color
            w.Write((ulong)0);         // fx
        }

        // Entry: size(4)=content length only, type(1), [pad 11], idLen, id
        var entry = new byte[EntryHeaderSize + 1 + layerId.Length];
        using (var s = new MemoryStream(entry))
        using (var w = new BinaryWriter(s))
        {
            w.Write(1 + layerId.Length); // content length, matching Pixquare semantics
            w.Write((byte)0);            // regular layer
            w.BaseStream.Position = EntryHeaderSize;
            w.Write((byte)layerId.Length);
            WriteRaw(w, layerId);
        }

        var paletteBytes = palette.Colors.Count * 4;
        var contentBytes = artworkId.Length + 8
            + 8 + entry.Length
            + 8                            // groups
            + 8 + layer.Length
            + 8 + frameContent.Length
            + 8 + paletteBytes
            + 8 + 8 + 8 + 8 + 8 + 8 + 8    // refs, refpngs, sym, tags, tilesets, tilemap layers, tilemap contents
            + 1;                           // color depth
        var total = ArtworkHeaderSize + contentBytes;

        using var output = new MemoryStream(total);
        using var writer = new BinaryWriter(output);
        writer.Write((ulong)total);
        writer.Write((byte)artworkId.Length);
        writer.Write((ushort)2);       // compat
        writer.BaseStream.Position = ArtworkHeaderSize;
        WriteRaw(writer, artworkId);
        writer.Write(canvas.Width);
        writer.Write(canvas.Height);
        writer.Write((ulong)1);        // entries
        writer.Write(entry);
        writer.Write((ulong)0);        // groups
        writer.Write((ulong)1);        // layers
        writer.Write(layer);
        writer.Write((ulong)1);        // frame contents
        writer.Write(frameContent);
        writer.Write((ulong)palette.Colors.Count);
        foreach (var c in palette.Colors)
        {
            writer.Write(c.R);
            writer.Write(c.G);
            writer.Write(c.B);
            writer.Write((byte)255);
        }
        for (var i = 0; i < 7; i++) writer.Write((ulong)0); // refs..tilemap contents
        writer.Write((byte)0);         // color depth: RGB
        writer.Flush();
        return output.ToArray();

        static void WriteRaw(BinaryWriter w, string value) =>
            w.Write(System.Text.Encoding.ASCII.GetBytes(value));
    }

    public static (Canvas Canvas, Palette Palette) Import(byte[] data)
    {
        if (data.Length < ArtworkHeaderSize)
            throw new InvalidDataException("too short for px header");
        var pos = 0;
        var fileSize = ReadU64(data, ref pos);
        if (fileSize > (ulong)data.Length + 64)
            throw new InvalidDataException("not a px file");
        var artworkIdLength = ReadByte(data, ref pos);
        pos = ArtworkHeaderSize; // remainder of header unused
        pos += artworkIdLength;  // artwork id

        var width = (int)ReadU32(data, ref pos);
        var height = (int)ReadU32(data, ref pos);
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096)
            throw new InvalidDataException($"bad px canvas size {width}x{height}");

        var entryIds = new List<(string Id, byte Type)>();
        var entryCount = ReadU64(data, ref pos);
        for (ulong i = 0; i < entryCount; i++)
        {
            var start = pos;
            var size = (int)ReadU32(data, ref pos); // content length only (header excluded)
            var type = ReadByte(data, ref pos);
            // id length+id sit at the padded 16-byte header end in real files
            RequireBytes(data, start + EntryHeaderSize, 1);
            var idLength = data[start + EntryHeaderSize];
            var idPos = start + EntryHeaderSize + 1;
            RequireBytes(data, idPos, idLength);
            entryIds.Add((System.Text.Encoding.ASCII.GetString(data, idPos, idLength), type));
            pos = start + EntryHeaderSize + size;
        }

        SkipModels(data, ref pos, ReadU64(data, ref pos)); // groups

        // Layers in file order; entries give stacking order (higher index = above).
        var layerOrder = entryIds.Where(e => e.Type == 0).Select(e => e.Id).ToList();
        var layerPixels = new Dictionary<string, (string ContentId, bool Visible)>();
        var layerCount = ReadU64(data, ref pos);
        for (ulong i = 0; i < layerCount; i++)
        {
            var start = pos;
            var size = (int)ReadU32(data, ref pos);
            var idLength = ReadByte(data, ref pos);
            var nameLength = ReadByte(data, ref pos);
            pos = start + ModelHeaderSize;
            var id = ReadAscii(data, ref pos, idLength);
            pos += nameLength;
            var frameCount = ReadU64(data, ref pos);
            string? firstContentId = null;
            for (ulong f = 0; f < frameCount; f++)
            {
                var frameStart = pos;
                var frameSize = (int)ReadU32(data, ref pos); // content length only (header excluded)
                var frameIdLength = ReadByte(data, ref pos);
                var contentIdLength = ReadByte(data, ref pos);
                pos = frameStart + ModelHeaderSize;
                pos += frameIdLength + 4 + 1; // id, duration, selected
                var contentId = ReadAscii(data, ref pos, contentIdLength);
                firstContentId ??= contentId;
                pos = frameStart + ModelHeaderSize + frameSize;
            }
            // Layer `size` is content length (header excluded); tail is inside it:
            // opacity f16, visible, locked, selected, alphaLocked, blendMode u16, linked,
            // [crop] u64, [clip] u64, color 4B, [fx] u64.
            RequireBytes(data, pos + 2, 1);
            var visible = data[pos + 2] != 0;
            layerPixels[id] = (firstContentId ?? "", visible);
            pos = start + ModelHeaderSize + size;
        }


        // Frame contents keyed by id
        var contents = new Dictionary<string, byte[]>();
        var contentCount = ReadU64(data, ref pos);
        for (ulong i = 0; i < contentCount; i++)
        {
            var start = pos;
            ReadU64(data, ref pos); // size: unreliable (undercounts stream); clen is authoritative
            var idLength = ReadByte(data, ref pos);
            var uncompressedLength = (int)ReadU32(data, ref pos);
            var compressedLength = (int)ReadU32(data, ref pos);
            // Header fields occupy 18 bytes, zero-padded to 32. Data follows the id.
            pos = start + ModelHeaderSize;
            var id = ReadAscii(data, ref pos, idLength);
            RequireBytes(data, pos, compressedLength);
            var compressed = data.AsSpan(pos, compressedLength).ToArray();
            pos += compressedLength; // clean files: palette follows immediately; Pixquare: anchor below tolerates the 7-byte trailer
            contents[id] = ZlibDecompressLenient(compressed, uncompressedLength);
        }

        // Palette follows a 0–8 byte garbage trailer after the last frame content.
        // Anchor: plausible count followed by well-formed ARGB entries.
        var paletteCount = 0UL;
        for (var skip = 0; skip <= 8 && paletteCount == 0; skip++)
        {
            if (pos + skip + 8 > data.Length) break;
            var candidate = BitConverter.ToUInt64(data, pos + skip);
            if (candidate is > 0 and <= 4096 && pos + skip + 8 + (int)candidate * 4 <= data.Length)
            {
                paletteCount = candidate;
                pos += skip;
            }
        }
        if (paletteCount == 0)
        {
            RequireBytes(data, pos, Math.Min(16, data.Length - pos));
            throw new InvalidDataException($"px palette not found after frame contents (pos={pos}, next={Convert.ToHexString(data.AsSpan(pos, Math.Min(16, data.Length - pos)))})");
        }
        pos += 8;
        RequireBytes(data, pos, (int)paletteCount * 4);
        var paletteEntries = new (byte R, byte G, byte B, byte A)[paletteCount];
        for (var i = 0; i < paletteEntries.Length; i++)
        {
            paletteEntries[i] = (data[pos], data[pos + 1], data[pos + 2], data[pos + 3]);
            pos += 4;
        }

        // Skip trailing sections to reach color depth
        SkipModels(data, ref pos, ReadU64(data, ref pos)); // reference layers
        SkipByteArrays(data, ref pos);                      // reference PNGs
        SkipModels(data, ref pos, ReadU64(data, ref pos)); // symmetry lines
        SkipModels(data, ref pos, ReadU64(data, ref pos)); // tags
        SkipModels(data, ref pos, ReadU64(data, ref pos)); // tilesets
        SkipModels(data, ref pos, ReadU64(data, ref pos)); // tilemap layers
        SkipModels(data, ref pos, ReadU64(data, ref pos)); // tilemap frame contents
        var indexed = pos < data.Length && data[pos] == 1;

        var canvas = new Canvas(width, height);
        var palette = new Palette();
        var bytesPerPixel = indexed ? 1 : 4;

        // Flatten visible layers in entry order (topmost last so it wins).
        foreach (var layerId in layerOrder)
        {
            if (!layerPixels.TryGetValue(layerId, out var layer) || !layer.Visible) continue;
            if (!contents.TryGetValue(layer.ContentId, out var raw)) continue;
            if (raw.Length < width * height * bytesPerPixel) continue;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var offset = (y * width + x) * bytesPerPixel;
                    if (indexed)
                    {
                        var pi = raw[offset];
                        if (pi >= paletteEntries.Length || paletteEntries[pi].A == 0) continue;
                        var e = paletteEntries[pi];
                        canvas.SetPixel(x, y, palette.AddColor(new Rgb(Unpremultiply(e.R, e.A), Unpremultiply(e.G, e.A), Unpremultiply(e.B, e.A))));
                    }
                    else
                    {
                        var a = raw[offset + 3];
                        if (a == 0) continue;
                        canvas.SetPixel(x, y, palette.AddColor(new Rgb(
                            Unpremultiply(raw[offset], a),
                            Unpremultiply(raw[offset + 1], a),
                            Unpremultiply(raw[offset + 2], a))));
                    }
                }
            }
        }

        return (canvas, palette);
    }

    private static byte Unpremultiply(byte channel, byte alpha) =>
        alpha == 0 ? (byte)0 : (byte)Math.Min(255, channel * 255 / alpha);

    private static void RequireBytes(byte[] d, int p, int count)
    {
        if (p < 0 || count < 0 || p + count > d.Length)
            throw new InvalidDataException($"truncated px file: need {count} bytes at {p}, have {d.Length}");
    }

    private static byte ReadByte(byte[] d, ref int p)
    {
        RequireBytes(d, p, 1);
        return d[p++];
    }

    private static uint ReadU32(byte[] d, ref int p) { RequireBytes(d, p, 4); var v = BitConverter.ToUInt32(d, p); p += 4; return v; }
    private static ulong ReadU64(byte[] d, ref int p) { RequireBytes(d, p, 8); var v = BitConverter.ToUInt64(d, p); p += 8; return v; }
    private static string ReadAscii(byte[] d, ref int p, int length) { RequireBytes(d, p, length); var s = System.Text.Encoding.ASCII.GetString(d, p, length); p += length; return s; }

    private static void SkipModels(byte[] d, ref int pos, ulong count)
    {
        for (ulong i = 0; i < count; i++)
        {
            var size = (int)ReadU32(d, ref pos);
            pos += size - 4;
        }
    }

    private static void SkipByteArrays(byte[] d, ref int pos)
    {
        var count = ReadU64(d, ref pos);
        for (ulong i = 0; i < count; i++)
        {
            var length = ReadU64(d, ref pos);
            pos += (int)length;
        }
    }

    internal static byte[] ZlibCompress(byte[] data)
    {
        using var stream = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionLevel.Optimal, leaveOpen: true))
            zlib.Write(data, 0, data.Length);
        return stream.ToArray();
    }

    /// <summary>
    /// Zlib decompress tolerating Pixquare's corrupt trailer (bad adler32 + padding zeros).
    /// Returns whatever the deflate stream yields before the trailer; the trailer error is swallowed.
    /// </summary>
    internal static byte[] ZlibDecompressLenient(byte[] data, int expectedLength)
    {
        using var stream = new MemoryStream(data);
        using var zlib = new ZLibStream(stream, CompressionMode.Decompress);
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        try
        {
            int read;
            while ((read = zlib.Read(buffer, 0, buffer.Length)) > 0)
                result.Write(buffer, 0, read);
        }
        catch (InvalidDataException)
        {
            // corrupt adler32 trailer: data already recovered
        }
        var bytes = result.ToArray();
        if (bytes.Length != expectedLength)
            throw new InvalidDataException($"decompressed {bytes.Length} bytes, expected {expectedLength}");
        return bytes;
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
