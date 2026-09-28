using Terminal.Gui.App;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

// PROTOTYPE — throwaway. Question: which cursor blink stays visible on every brush
// color, including black? One cursor at a time, sitting on Vixel's real checkerboard
// (CellMapping.BackgroundShade), animating at the app's real 200ms tick.
//
// Run:  dotnet run --project prototype/BlinkDemo
// Keys: ←/→ (or 1-9) switch strategy, b switch brush color, q quits.

Application.Init();

var demo = new CursorView { Width = Dim.Fill(), Height = Dim.Fill() };
var window = new Window { Title = "blink prototype — arrows/1-9 strategy, b brush, q quit" };
window.Add(demo);

Application.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
{
    demo.Tick();
    return true;
});

Application.Run(window);
window.Dispose();
Application.Shutdown();

// ---------------------------------------------------------------------------

internal sealed record Rgb(byte R, byte G, byte B)
{
    public static readonly Rgb White = new(255, 255, 255);
    public static readonly Rgb Black = new(0, 0, 0);

    public byte Luma => (byte)(R * 0.299 + G * 0.587 + B * 0.114); // Rec. 601
    public Rgb Invert() => new((byte)(255 - R), (byte)(255 - G), (byte)(255 - B));
    public Rgb Dim(int pct) => new((byte)(R * pct / 100), (byte)(G * pct / 100), (byte)(B * pct / 100));
    public Rgb Gray(byte v) => new(v, v, v);
    public Rgb ContrastGray() => Luma < 128 ? Gray(224) : Gray(32);
    public Rgb Saturate()
    {
        var max = Math.Max(R, Math.Max(G, B));
        if (max == 0) return White;
        return new((byte)(R * 255 / max), (byte)(G * 255 / max), (byte)(B * 255 / max));
    }
}

internal sealed record Strategy(string Name, Func<Rgb, Rgb[]> Frames);

internal sealed class CursorView : View
{
    // Vixel's checkerboard (src/Vixel.Core/Core.cs: BackgroundShade).
    private static Rgb Shade(int x, int y) => (x / 2 + y / 2) % 2 == 0 ? new Rgb(38, 38, 38) : new Rgb(22, 22, 22);

    private static readonly Rgb[] Brushes =
    [
        new(0, 0, 0),       // black — the bug
        new(90, 90, 90),    // dark gray
        new(64, 128, 255),  // mid blue
        new(230, 60, 60),   // red
        new(255, 255, 255), // white
    ];

    private static readonly Strategy[] Strategies =
    [
        new("A color<>50%dim  (current)", c => [c, c.Dim(50)]),
        new("B color<>inverse",           c => [c, c.Invert()]),
        new("C color<>contrast gray",     c => [c, c.ContrastGray()]),
        new("D color<>white",             c => [c, Rgb.White]),
        new("E 4f: c>inv>c>gray",         c => [c, c.Invert(), c, c.ContrastGray()]),
        new("F 3f: c>inv>black>c",        c => [c, c.Invert(), Rgb.Black, c]),
        new("G 3f: c>white>black>c",      c => [c, Rgb.White, Rgb.Black, c]),
        new("H 4f: c>inv>gray>inv",       c => [c, c.Invert(), c.ContrastGray(), c.Invert()]),
        new("I 4f: c>satur>inv>satur",    c => [c, c.Saturate(), c.Invert(), c.Saturate()]),
    ];
    private const int CellsWide = 40; // checkerboard width in cells
    private const int CellsHigh = 20; // cell rows (40 pixel rows)

    private int _tick;
    private int _strategy;
    private int _brush;

    public CursorView() => CanFocus = true;

    public void Tick() { _tick++; SetNeedsDraw(); }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Q) { Application.RequestStop(); return true; }
        if (key == Key.CursorRight) { _strategy = (_strategy + 1) % Strategies.Length; SetNeedsDraw(); return true; }
        if (key == Key.CursorLeft) { _strategy = (_strategy + Strategies.Length - 1) % Strategies.Length; SetNeedsDraw(); return true; }
        if (key.TryGetPrintableRune(out var r) && r.Value is >= '1' and <= '9')
        {
            _strategy = r.Value - '1';
            SetNeedsDraw();
            return true;
        }
        if (key == Key.B) { _brush = (_brush + 1) % Brushes.Length; SetNeedsDraw(); return true; }
        return base.OnKeyDown(key);
    }

    protected override bool OnDrawingContent(DrawContext context)
    {
        var ox = Math.Max(0, (Viewport.Width - CellsWide) / 2);
        var oy = Math.Max(2, (Viewport.Height - CellsHigh) / 2);

        // checkerboard
        for (var row = 0; row < CellsHigh; row++)
        {
            Move(ox, oy + row);
            for (var x = 0; x < CellsWide; x++)
            {
                var top = Shade(x, row * 2);
                var bottom = Shade(x, row * 2 + 1);
                SetAttribute(new Attribute(ToC(top), ToC(bottom)));
                AddRune('▀');
            }
        }

        // one cursor: the current brush, blinking by the current strategy, 2x2 pixels
        // (2 cells wide x 1 cell row) at the center.
        var brush = Brushes[_brush];
        var frames = Strategies[_strategy].Frames(brush);
        var shown = frames[_tick % frames.Length];
        var cx = ox + CellsWide / 2 - 1;
        var cy = oy + CellsHigh / 2;
        SetAttribute(new Attribute(ToC(shown), ToC(shown)));
        Move(cx, cy);
        AddStr("██");

        // legend
        Move(0, 0);
        SetAttribute(new Attribute(Color.White, Color.Black));
        AddStr($"{Strategies[_strategy].Name}   brush #{brush.R:x2}{brush.G:x2}{brush.B:x2}   frame {_tick % frames.Length + 1}/{frames.Length}   (←/→ strategy, b brush, q quit)");
        return true;
    }

    private static Color ToC(Rgb c) => new(c.R, c.G, c.B);
}
