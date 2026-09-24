using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

// Canvas background options: five panels, identical content —
// a green L-shape committed, blue flashing brush parked on an odd pixel row.
// [1] per-cell checker  [2] per-pixel checker  [3] flat gray  [4] terminal black  [5] 2x2 checker

var app = Application.Create();
app.Init();

var demo = new BgDemo(app) { Width = Dim.Fill(), Height = Dim.Fill() };
var window = new Window { Title = "canvas backgrounds:  1 cell-check  2 pixel-check  3 flat gray  4 black  5 2x2-check   (q quits)" };
window.Add(demo);

app.AddTimeout(TimeSpan.FromMilliseconds(400), () =>
{
    demo.Tick();
    return true;
});

app.Run(window);
window.Dispose();
app.Dispose();

sealed class BgDemo : View
{
    private const int PanelWidth = 24;   // cells
    private const int PanelHeight = 10;  // cell rows (20 pixel rows)

    private readonly IApplication _app;
    private readonly (byte R, byte G, byte B)?[,] _pixels =
        new (byte, byte, byte)?[PanelWidth, PanelHeight * 2];

    private int _blink;

    private static readonly Color CheckA = new(38, 38, 38);
    private static readonly Color CheckB = new(22, 22, 22);
    private static readonly Color Flat = new(26, 26, 26);
    private static readonly Color Brush = new(64, 128, 255);

    public BgDemo(IApplication app)
    {
        _app = app;
        CanFocus = true;

        // Committed L-shape in green: vertical bar x=5 rows 4-11, foot rows 10-11 x=5-9.
        for (var y = 4; y <= 11; y++) _pixels[5, y] = (64, 200, 64);
        for (var x = 5; x <= 9; x++) { _pixels[x, 10] = (64, 200, 64); _pixels[x, 11] = (64, 200, 64); }
    }

    public void Tick()
    {
        _blink = (_blink + 1) % 2;
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q) { _app.RequestStop(); return true; }
        return base.OnKeyDown(key);
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        for (var panel = 0; panel < 5; panel++)
        {
            var x0 = panel * (PanelWidth + 2);
            for (var cellRow = 0; cellRow < PanelHeight; cellRow++)
            {
                Move(x0, cellRow);
                for (var x = 0; x < PanelWidth; x++)
                {
                    // Resolve each half: committed pixel color, else the panel's background shade
                    // (null shade = transparent to terminal background).
                    var top = HalfColor(panel, x, cellRow * 2);
                    var bottom = HalfColor(panel, x, cellRow * 2 + 1);

                    Color fg, bg;
                    char glyph;
                    if (top.HasValue && bottom.HasValue) { fg = top.Value; bg = bottom.Value; glyph = '▀'; }
                    else if (top.HasValue) { fg = top.Value; bg = Color.Black; glyph = '▀'; }
                    else if (bottom.HasValue) { fg = bottom.Value; bg = Color.Black; glyph = '▄'; }
                    else { fg = Color.Black; bg = Color.Black; glyph = ' '; }

                    SetAttribute(new Attribute(fg, bg));
                    AddRune(glyph);
                }
            }

            DrawBrush(panel, x0);
        }
        return true;
    }

    // Committed pixel color if set; else the panel's background shade; null = terminal default.
    private Color? HalfColor(int panel, int x, int y)
    {
        if (_pixels[x, y] is { } p) return new Color(p.R, p.G, p.B);
        return panel switch
        {
            0 => (x + y / 2) % 2 == 0 ? CheckA : CheckB, // per-cell checker: both halves share
            1 => (x + y) % 2 == 0 ? CheckA : CheckB,     // per-pixel checker: halves alternate
            2 => Flat,
            3 => null,                                    // terminal background
            // 2x2 checker: 2 pixels wide x 2 pixels tall (2 cells x 1 cell-row)
            _ => (x / 2 + y / 2) % 2 == 0 ? CheckA : CheckB,
        };
    }

    private void DrawBrush(int panel, int x0)
    {
        const int brushX = 14;
        const int brushY = 7; // odd pixel row — bottom half of its cell
        var shown = _blink == 0 ? Brush : new Color(Brush.R / 2, Brush.G / 2, Brush.B / 2);

        var otherY = brushY % 2 == 0 ? brushY + 1 : brushY - 1;
        var other = HalfColor(panel, brushX, otherY) ?? Color.Black;

        SetAttribute(new Attribute(shown, other));
        Move(x0 + brushX, brushY / 2);
        AddRune(brushY % 2 == 0 ? '▀' : '▄');
    }
}
