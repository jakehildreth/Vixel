namespace Vixel.Core;

/// <summary>
/// Half-block cell mapping: the pure decisions of how two canvas pixels become one
/// terminal cell (glyph + which color rides fg/bg), plus the empty-canvas checkerboard.
/// Framework-free; the render layer substitutes its own color type at the boundary.
/// </summary>
public static class CellMapping
{
    /// <summary>A terminal cell: glyph plus fg/bg assignment.</summary>
    public readonly record struct Cell(char Glyph, Rgb Fg, Rgb Bg);

    /// <summary>The 2×2-pixel checkerboard shade for an empty pixel (2 cells wide × 1 cell row per check).</summary>
    public static Rgb BackgroundShade(int x, int y) =>
        (x / 2 + y / 2) % 2 == 0 ? new Rgb(38, 38, 38) : new Rgb(22, 22, 22);

    /// <summary>
    /// Maps two canvas pixels (null = empty) at canvas position (x, y of the top pixel)
    /// to a terminal cell. Empty halves take the checkerboard shade. ▀ and ▄ both draw
    /// their half from fg, so the drawn half's color always rides fg; bg carries the
    /// other half when both halves are set.
    /// </summary>
    public static Cell Map(Rgb? top, Rgb? bottom, int x = 0, int topY = 0)
    {
        var topColor = top ?? BackgroundShade(x, topY);
        var bottomColor = bottom ?? BackgroundShade(x, topY + 1);

        return (top.HasValue, bottom.HasValue) switch
        {
            (true, true) => new Cell('▀', topColor, bottomColor),
            (true, false) => new Cell('▀', topColor, bottomColor),
            (false, true) => new Cell('▄', bottomColor, topColor),
            (false, false) => new Cell('▀', topColor, bottomColor),
        };
    }
}

/// <summary>A 24-bit RGB color.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6) throw new FormatException($"Invalid hex color: {hex}");
        return new Rgb(
            Convert.ToByte(h[..2], 16),
            Convert.ToByte(h[2..4], 16),
            Convert.ToByte(h[4..6], 16));
    }

    public string ToHex() => $"#{R:x2}{G:x2}{B:x2}";
}

/// <summary>An ordered set of colors; index = palette slot. Deduplicates.</summary>
public sealed class Palette
{
    private readonly List<Rgb> _colors = [];

    public IReadOnlyList<Rgb> Colors => _colors;

    public int AddColor(Rgb color)
    {
        var existing = _colors.IndexOf(color);
        if (existing >= 0) return existing;
        _colors.Add(color);
        return _colors.Count - 1;
    }
}

/// <summary>A single undoable canvas mutation: size before/after plus every touched pixel.</summary>
public sealed record PixelChange(int WidthBefore, int HeightBefore, IReadOnlyList<PixelTouch> Touched);

/// <summary>One touched pixel: position and palette-index before/after (null = transparent).</summary>
public readonly record struct PixelTouch(int X, int Y, int? Old, int? New);

/// <summary>
/// A grid of palette-indexed pixels (null = transparent). Writes go through methods that
/// return a <see cref="PixelChange"/>; the change record is the undo/time-lapse unit.
/// </summary>
public sealed class Canvas
{
    private int?[,] _pixels;

    public Canvas(int width, int height)
    {
        if (width < 1 || height < 1) throw new ArgumentOutOfRangeException(nameof(width));
        Width = width;
        Height = height;
        _pixels = new int?[width, height];
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    /// <summary>Reads are legal off-canvas (return null); writes are not.</summary>
    public int? this[int x, int y] =>
        x >= 0 && x < Width && y >= 0 && y < Height ? _pixels[x, y] : null;

    public void SetPixel(int x, int y, int? paletteIndex)
    {
        if (x < 0 || x >= Width) throw new ArgumentOutOfRangeException(nameof(x));
        if (y < 0 || y >= Height) throw new ArgumentOutOfRangeException(nameof(y));
        _pixels[x, y] = paletteIndex;
    }

    /// <summary>Sets pixels and returns the undoable change. No change → empty Touched.</summary>
    public PixelChange SetPixels(IEnumerable<(int X, int Y, int? Index)> writes)
    {
        var touched = new List<PixelTouch>();
        foreach (var (x, y, index) in writes)
        {
            if (x < 0 || x >= Width || y < 0 || y >= Height) continue;
            var old = _pixels[x, y];
            if (old == index) continue;
            _pixels[x, y] = index;
            touched.Add(new PixelTouch(x, y, old, index));
        }
        return new PixelChange(Width, Height, touched);
    }

    /// <summary>Top-left anchored resize; grow extends transparent, shrink crops. Undoable.</summary>
    public PixelChange Resize(int newWidth, int newHeight)
    {
        if (newWidth < 1 || newHeight < 1) throw new ArgumentOutOfRangeException(nameof(newWidth));
        if (newWidth == Width && newHeight == Height)
            return new PixelChange(Width, Height, []);

        var touched = new List<PixelTouch>();
        var next = new int?[newWidth, newHeight];

        // Record cropped pixels (lost) and newly exposed cells that carried content, so undo of a
        // shrink restores what was cropped. ApplyInverse keys off WidthBefore for size restoration;
        // transparent exposed cells are no-ops (null→null) and are not recorded.
        for (var y = 0; y < Math.Max(Height, newHeight); y++)
        {
            for (var x = 0; x < Math.Max(Width, newWidth); x++)
            {
                var old = x < Width && y < Height ? _pixels[x, y] : (int?)null;
                if (x < newWidth && y < newHeight) next[x, y] = old;
                var cropped = x < Width && y < Height && (x >= newWidth || y >= newHeight);
                if (cropped) touched.Add(new PixelTouch(x, y, old, null));
            }
        }

        var change = new PixelChange(Width, Height, touched);
        _pixels = next;
        Width = newWidth;
        Height = newHeight;
        return change;
    }

    /// <summary>Reverts a change: size restored, then pixels back to old values.</summary>
    public void ApplyInverse(PixelChange change)
    {
        if (change.WidthBefore != Width || change.HeightBefore != Height)
            ForceResize(change.WidthBefore, change.HeightBefore);
        foreach (var t in change.Touched)
        {
            if (t.X < Width && t.Y < Height) _pixels[t.X, t.Y] = t.Old;
        }
    }

    /// <summary>Re-applies a change (redo): pixels to new values.</summary>
    public void ApplyForward(PixelChange change)
    {
        foreach (var t in change.Touched)
        {
            if (t.X < Width && t.Y < Height) _pixels[t.X, t.Y] = t.New;
        }
    }

    // Raw resize without change recording — used by ApplyInverse only.
    private void ForceResize(int width, int height)
    {
        var next = new int?[width, height];
        for (var y = 0; y < Math.Min(Height, height); y++)
            for (var x = 0; x < Math.Min(Width, width); x++)
                next[x, y] = _pixels[x, y];
        _pixels = next;
        Width = width;
        Height = height;
    }
}

/// <summary>Integer line between two points, endpoints included, in draw order.</summary>
public static class Bresenham
{
    public static IEnumerable<(int X, int Y)> Line(int x0, int y0, int x1, int y1)
    {
        var dx = Math.Abs(x1 - x0);
        var dy = -Math.Abs(y1 - y0);
        var sx = x0 < x1 ? 1 : -1;
        var sy = y0 < y1 ? 1 : -1;
        var error = dx + dy;

        while (true)
        {
            yield return (x0, y0);
            if (x0 == x1 && y0 == y1) yield break;
            var e2 = 2 * error;
            if (e2 >= dy) { error += dy; x0 += sx; }
            if (e2 <= dx) { error += dx; y0 += sy; }
        }
    }
}

/// <summary>Floods the contiguous same-color region under (x, y) with a new palette index.</summary>
public static class FloodFill
{
    public static PixelChange Fill(Canvas canvas, int x, int y, int? newIndex)
    {
        var target = canvas[x, y];
        if (target == newIndex) return new PixelChange(canvas.Width, canvas.Height, []);

        var writes = new List<(int, int, int?)>();
        var visited = new bool[canvas.Width, canvas.Height];
        var stack = new Stack<(int X, int Y)>();
        stack.Push((x, y));

        while (stack.Count > 0)
        {
            var (cx, cy) = stack.Pop();
            if (cx < 0 || cx >= canvas.Width || cy < 0 || cy >= canvas.Height) continue;
            if (visited[cx, cy] || canvas[cx, cy] != target) continue;
            visited[cx, cy] = true;
            writes.Add((cx, cy, newIndex));
            stack.Push((cx + 1, cy));
            stack.Push((cx - 1, cy));
            stack.Push((cx, cy + 1));
            stack.Push((cx, cy - 1));
        }

        return canvas.SetPixels(writes);
    }
}

/// <summary>24-bit → xterm-256 quantization (Vixel owns this tier per the research findings).</summary>
public static class Xterm256
{
    private static readonly byte[] CubeLevels = [0, 95, 135, 175, 215, 255];

    /// <summary>The 256-entry xterm palette as RGB.</summary>
    public static Rgb[] Palette { get; } = BuildPalette();

    private static Rgb[] BuildPalette()
    {
        var p = new Rgb[256];
        // 0-15: standard + bright ANSI (indices are user-themable; values are the common defaults)
        ReadOnlySpan<byte> standard =
        [
            0, 0, 0, 128, 0, 0, 0, 128, 0, 128, 128, 0, 0, 0, 128, 128, 0, 128, 0, 128, 128, 192, 192, 192,
            128, 128, 128, 255, 0, 0, 0, 255, 0, 255, 255, 0, 0, 0, 255, 255, 0, 255, 0, 255, 255, 255, 255, 255
        ];
        for (var i = 0; i < 16; i++)
            p[i] = new Rgb(standard[i * 3], standard[i * 3 + 1], standard[i * 3 + 2]);
        for (var r = 0; r < 6; r++)
            for (var g = 0; g < 6; g++)
                for (var b = 0; b < 6; b++)
                    p[16 + 36 * r + 6 * g + b] = new Rgb(CubeLevels[r], CubeLevels[g], CubeLevels[b]);
        for (var n = 0; n < 24; n++)
        {
            var v = (byte)(8 + 10 * n);
            p[232 + n] = new Rgb(v, v, v);
        }
        return p;
    }

    /// <summary>Nearest xterm-256 index for an RGB color (redmean distance, cube + ramp only).</summary>
    public static int ToIndex(Rgb color)
    {
        var best = 16;
        var bestDistance = long.MaxValue;
        for (var i = 16; i < 256; i++)
        {
            var d = Redmean(color, Palette[i]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }
        return best;
    }

    private static long Redmean(Rgb a, Rgb b)
    {
        long rMean = (a.R + (long)b.R) / 2;
        long dr = a.R - (long)b.R;
        long dg = a.G - (long)b.G;
        long db = a.B - (long)b.B;
        return ((512 + rMean) * dr * dr >> 8) + 4 * dg * dg + ((767 - rMean) * db * db >> 8);
    }
}

/// <summary>Atomic file writes: temp file in the same directory, then move over the target.</summary>
public static class AtomicWrite
{
    public static void WriteAllText(string path, string contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }

    public static void WriteAllBytes(string path, byte[] contents)
    {
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, contents);
        File.Move(tmp, path, overwrite: true);
    }
}
