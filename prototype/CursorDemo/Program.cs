using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

// Cursor-treatment demo: three canvases side by side.
// Left: checkerboard empty cells. Middle: flat dark gray. Right: black + blinking cursor.
// Same committed pixels on all three: a red stamp at (4,1) (odd row), green at (7,3).
// Brush (blue) sits at (6,2) top-half; press j/k to move it per half-row.

Application.Init();

var demo = new DemoView { Width = Dim.Fill(), Height = Dim.Fill() };

var window = new Window { Title = "cursor options: [1 checker] [2 gray] [3 black+blink]  j/k move  q quit" };
window.Add(demo);

Application.AddTimeout(TimeSpan.FromMilliseconds(400), () =>
{
    demo.Tick();
    return true;
});

Application.Run(window);
window.Dispose();
Application.Shutdown();

sealed class DemoView : View
{
    private const int PanelWidth = 20;   // cells per canvas
    private const int PanelHeight = 12;  // cell rows per canvas (24 pixel rows)

    // Committed pixels: null = transparent.
    private readonly (byte R, byte G, byte B)?[,] _pixels =
        new (byte, byte, byte)?[PanelWidth, PanelHeight * 2];

    private static readonly (byte R, byte G, byte B) BrushColor = (64, 128, 255);
    private int _brushY = 2; // pixel row; even = top half
    private int _blinkPhase;

    public DemoView()
    {
        CanFocus = true;
        _pixels[4, 1] = (255, 64, 64);   // red stamp, odd row
        _pixels[7, 3] = (64, 255, 64);   // green stamp, odd row
    }

    public void Tick()
    {
        _blinkPhase = (_blinkPhase + 1) % 2;
        SetNeedsDraw();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.J || key == Key.CursorDown) { _brushY = Math.Min(_brushY + 1, PanelHeight * 2 - 1); SetNeedsDraw(); return true; }
        if (key == Key.K || key == Key.CursorUp) { _brushY = Math.Max(_brushY - 1, 0); SetNeedsDraw(); return true; }
        if (key == Key.Q) { Application.RequestStop(); return true; }
        return base.OnKeyDown(key);
    }

    protected override bool OnDrawingContent(DrawContext context)
    {
        for (var panel = 0; panel < 3; panel++)
        {
            var x0 = panel * (PanelWidth + 2);
            for (var cellRow = 0; cellRow < PanelHeight; cellRow++)
            {
                Move(x0, cellRow);
                for (var x = 0; x < PanelWidth; x++)
                {
                    var top = CellColor(panel, x, cellRow * 2);
                    var bottom = CellColor(panel, x, cellRow * 2 + 1);
                    var fg = top ?? Color.Black;
                    var bg = bottom ?? Color.Black;
                    var glyph = (top.HasValue, bottom.HasValue) switch
                    {
                        (true, true) => '▀',
                        (true, false) => '▀',
                        (false, true) => '▄',
                        (false, false) => ' ',
                    };
                    SetAttribute(new Attribute(fg, bg));
                    AddRune(glyph);
                }
            }
            DrawBrush(panel, x0);
        }
        return true;
    }

    // Committed pixel → terminal color, with the panel's empty-cell treatment.
    private Color? CellColor(int panel, int x, int y)
    {
        if (_pixels[x, y] is { } p) return new Color(p.R, p.G, p.B);
        return panel switch
        {
            0 => ((x + y) % 2 == 0) ? new Color(40, 40, 40) : new Color(24, 24, 24), // checkerboard
            1 => new Color(26, 26, 26),                                                // flat gray
            _ => null,                                                                 // black
        };
    }

    private void DrawBrush(int panel, int x0)
    {
        const int brushX = 6;
        var y = _brushY;

        // Flash: brush color ↔ gray of the same perceived luminosity (Rec. 601), all panels.
        var lum = (byte)(BrushColor.R * 0.299 + BrushColor.G * 0.587 + BrushColor.B * 0.114);
        var brush = _blinkPhase == 0
            ? new Color(BrushColor.R, BrushColor.G, BrushColor.B)
            : new Color(lum, lum, lum);

        var other = CellColor(panel, brushX, y % 2 == 0 ? y + 1 : y - 1) ?? Color.Black;
        SetAttribute(y % 2 == 0 ? new Attribute(brush, other) : new Attribute(other, brush));
        Move(x0 + brushX, y / 2);
        AddRune(y % 2 == 0 ? '▀' : '▄');
    }
}
