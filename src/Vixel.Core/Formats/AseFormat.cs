namespace Vixel.Core.Formats;

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

}
