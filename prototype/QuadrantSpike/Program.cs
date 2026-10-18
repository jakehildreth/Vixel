using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

// QuadrantSpike: quarter-block (quadrant, 2×2 pixels per cell) canvas prototype.
// Answers ticket #72: does the quarter-block mapping hold up on the real canvas —
// glyph fidelity, the 3-color-per-cell degradation, the checkerboard, the cursor read?
//
// Mapping (candidate from #72): each 2×2 pixel cell → one of 16 quadrant shapes,
// fg = majority color, bg = minority color. Degenerate cases (0/1 set pixels, or 2
// set pixels sharing a color) collapse to half-block shapes; 2 set pixels with
// different colors keep quadrants for fidelity.
//
// Keys: hjkl/arrows move · 1/2/3 cycle checkerboard scale · q quits.

Application.Init();

var canvas = new QuadrantCanvas { Width = Dim.Fill(), Height = Dim.Fill() - 1 };
var status = new Label { Y = Pos.Bottom(canvas), Width = Dim.Fill(), Height = 1 };

void UpdateStatus() =>
    status.Text = $"quarter-block spike   cursor=({canvas.CursorX},{canvas.CursorY})   checkerboard={canvas.CheckName} (1/2/3)   q=quit";

canvas.OnChanged = UpdateStatus;
UpdateStatus();

var window = new Window { Title = "Vixel quadrant spike" };
window.Add(canvas, status);
Application.Run(window);
window.Dispose();
Application.Shutdown();

/// <summary>24-bit RGB.</summary>
readonly record struct Rgb(byte R, byte G, byte B)
{
    public Color ToColor() => new(R, G, B);
}

/// <summary>
/// Canvas of abstract pixels rendered 4 per cell (2×2) via quadrant block elements.
/// </summary>
sealed class QuadrantCanvas : View
{
    // A small fixed demo canvas of pixels: sprites chosen to stress the mapping.
    // null = empty (checkerboard). Cells wider than tall are NOT modelled here —
    // we render the abstract grid 1 cell = 2×2 pixels, square-ish under most fonts.
    private readonly Rgb?[,] _px;
    public int CursorX, CursorY;
    public int CheckScale = 2; // 1 = 1×1px checks, 2 = 2×2 (current Vixel), 4 = 4×4
    public Action? OnChanged;

    public string CheckName => CheckScale switch { 1 => "1×1", 2 => "2×2 (current)", _ => "4×4" };

    public QuadrantCanvas()
    {
        _px = BuildDemo();
        CanFocus = true;
    }

    public int PixW => _px.GetLength(0);
    public int PixH => _px.GetLength(1);

    private static Rgb C(string h)
    {
        h = h.TrimStart('#');
        return new Rgb(Convert.ToByte(h[..2], 16), Convert.ToByte(h[2..4], 16), Convert.ToByte(h[4..6], 16));
    }

    private static readonly Rgb Red = C("#e06c75"), Grn = C("#98c379"), Blu = C("#61afef"),
        Yel = C("#e5c07b"), Mag = C("#c678dd"), Cyn = C("#56b6c2"), Wht = C("#d8dce3");

    // Demo sprites that stress the quarter-block mapping.
    private static Rgb?[,] BuildDemo()
    {
        var g = new Rgb?[64, 24];

        // 1. Single-pixel-wide diagonal (1px line fidelity at quarter res)
        for (var i = 0; i < 14; i++) g[2 + i, 2 + i] = Wht;

        // 2. 2×2 blocks each a single color (clean quadrant-full cells)
        for (var x = 0; x < 4; x++) for (var y = 0; y < 4; y++) g[20 + x, 2 + y] = Red;

        // 3. Checkerboard of two colors at 1px — the dither / quadrant-rich case
        for (var x = 0; x < 8; x++) for (var y = 0; y < 8; y++)
            if ((x + y) % 2 == 0) g[30 + x, 2 + y] = Grn; else g[30 + x, 2 + y] = Blu;

        // 4. THE DEGRADE CASE: 3+ colors inside single 2×2 cells
        //    rows of 2×2 cells, each cell = 3 distinct colors (majority bg, minority fg)
        Rgb[] trio = { Red, Yel, Blu };
        for (var cy = 0; cy < 3; cy++) for (var cx = 0; cx < 6; cx++)
        {
            var bx = 44 + cx * 2; var by = 2 + cy * 2;
            g[bx, by] = trio[(cx + cy) % 3];
            g[bx + 1, by] = trio[(cx + cy) % 3];
            g[bx, by + 1] = trio[(cx + cy + 1) % 3];
            g[bx + 1, by + 1] = trio[(cx + cy + 2) % 3];
        }

        // 5. Fine dithering gradient (1px speckle of 3 colors)
        Rgb[] grad = { C("#28324a"), C("#3d5a80"), C("#98c1d9") };
        var rnd = new Random(7);
        for (var x = 0; x < 30; x++) for (var y = 0; y < 8; y++)
            if (rnd.NextDouble() < 0.6) g[2 + x, 13 + y] = grad[rnd.Next(grad.Length)];

        // 6. Single isolated pixels on empty (the ▖▗▘▝ singleton quadrants)
        g[50, 13] = Mag; g[53, 14] = Cyn; g[56, 13] = Yel; g[59, 15] = Red;

        return g;
    }

    private Rgb BackgroundShade(int x, int y) =>
        ((x / CheckScale) + (y / CheckScale)) % 2 == 0 ? new Rgb(38, 38, 38) : new Rgb(22, 22, 22);

    // Quadrant bit layout: TL=1, TR=2, BL=4, BR=8 → one of 16 glyphs.
    private static readonly char[] QuadrantGlyph =
    {
        ' ',       // 0  .... (empty; bg only)
        '▖',       // 1  TL
        '▝',       // 2  TR
        '▀',       // 3  TL+TR  (top half)
        '▗',       // 4  BL
        '▌',       // 5  TL+BL  (left half)
        '▚',       // 6  TL+BR  (diag)
        '▛',       // 7  TL+TR+BL
        '▘',       // 8  BR
        '▞',       // 9  TR+BL  (diag)
        '▐',       // 10 TR+BR  (right half)
        '▜',       // 11 TL+TR+BR
        '▄',       // 12 BL+BR  (bottom half)
        '▙',       // 13 TL+BL+BR
        '▟',       // 14 TR+BL+BR
        '█',       // 15 full
    };

    // Map a 2×2 pixel cell to (glyph, fg, bg).
    private (char Glyph, Rgb Fg, Rgb Bg) MapCell(int cellX, int cellY)
    {
        var px = cellX * 2; var py = cellY * 2;
        // Gather the 4 quadrant pixels: bit set = "this quadrant shows the FG color".
        // We pick fg = the color shared by the majority of set pixels; bg = the rest.
        var colors = new List<Rgb>(4);
        var mask = new (Rgb? px, int bit)[]
        {
            (Pix(px, py), 1), (Pix(px + 1, py), 2), (Pix(px, py + 1), 4), (Pix(px + 1, py + 1), 8),
        };

        var set = mask.Where(m => m.px.HasValue).ToArray();

        // Empty cell → full-block cell of checkerboard (both shades averaged by parity).
        if (set.Length == 0)
        {
            // A fully-empty cell still needs a glyph; use full block of the TL checker shade.
            return (' ', BackgroundShade(px, py), BackgroundShade(px, py));
        }

        // Majority color among set pixels.
        var fg = set.GroupBy(m => m.px!.Value)
                    .OrderByDescending(gp => gp.Count())
                    .ThenBy(gp => Array.FindIndex(set, m => m.px!.Value.Equals(gp.Key)))
                    .First().Key;

        // Bits where the pixel equals fg → fg; else bg.
        var glyph = 0;
        Rgb? minority = null;
        foreach (var m in set)
        {
            if (m.px!.Value.Equals(fg)) glyph |= m.bit;
            else minority = m.px!.Value;
        }

        // bg: minority set color if any, else the checkerboard shade of the first empty slot.
        Rgb bg;
        if (minority.HasValue) bg = minority.Value;
        else
        {
            // all set pixels share fg; empty quadrants take their own checker shade (use first empty).
            var firstEmpty = mask.FirstOrDefault(m => !m.px.HasValue);
            var (ex, ey) = EmptyCoord(px, py, firstEmpty.bit);
            bg = firstEmpty.px is null && firstEmpty.bit != 0 ? BackgroundShade(ex, ey) : BackgroundShade(px, py);
        }

        return (QuadrantGlyph[glyph], fg, bg);
    }

    private static (int x, int y) EmptyCoord(int px, int py, int bit) => bit switch
    {
        1 => (px, py), 2 => (px + 1, py), 4 => (px, py + 1), 8 => (px + 1, py + 1), _ => (px, py),
    };

    private Rgb? Pix(int x, int y) =>
        x >= 0 && y >= 0 && x < PixW && y < PixH ? _px[x, y] : null;

    protected override bool OnDrawingContent(DrawContext context)
    {
        var vp = Viewport;
        for (var cellRow = 0; cellRow < vp.Height; cellRow++)
        {
            Move(0, cellRow);
            for (var cellX = 0; cellX < vp.Width; cellX++)
            {
                var (glyph, fg, bg) = MapCell(cellX, cellRow);
                SetAttribute(new Attribute(fg.ToColor(), bg.ToColor()));
                AddRune(glyph);
            }
        }
        // Cursor: invert the cell under the cursor pixel.
        var cx = CursorX / 2; var cy = CursorY / 2;
        if (cx >= 0 && cy >= 0 && cx < vp.Width && cy < vp.Height)
        {
            var (glyph, fg, bg) = MapCell(cx, cy);
            Move(cx, cy);
            SetAttribute(new Attribute(bg.ToColor(), fg.ToColor()));
            AddRune(glyph);
        }
        SetAttribute(new Attribute(Color.White, Color.Black));
        return true;
    }

    protected override bool OnKeyDown(Key key)
    {
        var moved = true;
        if (key == Key.CursorLeft || key == Key.H) CursorX = Math.Max(0, CursorX - 1);
        else if (key == Key.CursorRight || key == Key.L) CursorX = Math.Min(PixW - 1, CursorX + 1);
        else if (key == Key.CursorUp || key == Key.K) CursorY = Math.Max(0, CursorY - 1);
        else if (key == Key.CursorDown || key == Key.J) CursorY = Math.Min(PixH - 1, CursorY + 1);
        else if (key == Key.D1) CheckScale = 1;
        else if (key == Key.D2) CheckScale = 2;
        else if (key == Key.D3) CheckScale = 4;
        else if (key == Key.Q) { Application.RequestStop(); return true; }
        else moved = false;

        if (moved) { SetNeedsDraw(); OnChanged?.Invoke(); }
        return moved;
    }
}
