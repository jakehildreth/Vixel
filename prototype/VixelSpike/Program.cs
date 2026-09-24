using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Attribute = Terminal.Gui.Drawing.Attribute;

// Vixel spike: Terminal.Gui v2.5 half-block canvas.
// Verifies: per-cell truecolor fg/bg, keyboard-driven cursor, redraw throughput,
// capability detection surface, and ~5 Hz overlay animation (marching ants) cost.

Application.Init();

var capabilities = TerminalEnvironmentDetector.DetectColorCapabilities();

var canvas = new CanvasView { Width = Dim.Fill(), Height = Dim.Fill() - 1 };

var status = new Label
{
    Y = Pos.Bottom(canvas),
    Width = Dim.Fill(),
    Height = 1,
};

void UpdateStatus()
{
    status.Text =
        $" q quit | arrows/hjkl move | x stamp | v ants {(canvas.AntsEnabled ? "on " : "off")} | " +
        $"cursor {canvas.CursorX},{canvas.CursorY} | caps: {capabilities.Capability} | " +
        $"SupportsTrueColor: {Application.Driver?.SupportsTrueColor} | redraws: {canvas.RedrawCount} in {canvas.LastRedrawMs:F1}ms";
}

canvas.OnChanged = UpdateStatus;
UpdateStatus();

var window = new Window { Title = "Vixel spike" };
window.Add(canvas, status);

// Marching ants: ~5 Hz overlay phase advance via the app's own timeout scheduler.
Application.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
{
    if (canvas.AntsEnabled)
    {
        canvas.AdvanceAnts();
    }

    return true; // keep the timeout repeating
});

Application.Run(window);
window.Dispose();
Application.Shutdown();

/// <summary>
/// Canvas of abstract pixels, rendered 2 vertical pixels per half-block cell:
/// top pixel = foreground, bottom pixel = background, glyph U+2580.
/// </summary>
sealed class CanvasView : View
{
    private const int CanvasWidth = 80;
    private const int CanvasHeight = 48; // pixels; renders as 24 cell rows

    private readonly (byte R, byte G, byte B)[,] _pixels = new (byte, byte, byte)[CanvasWidth, CanvasHeight];
    private readonly Random _rng = new(42);

    // Brush: current stamp color + preview semantics.
    // The brush cell always renders _brushColor (preview). On Stamp, the pixel under the
    // brush takes _brushColor and _brushColor re-rolls — the stamped pixel stays hidden
    // under the preview until the brush moves off it.
    private (byte R, byte G, byte B) _brushColor = (255, 64, 64);

    public int CursorX { get; private set; }
    public int CursorY { get; private set; }
    public bool AntsEnabled { get; private set; } = true;
    public int RedrawCount { get; private set; }
    public double LastRedrawMs { get; private set; }
    public Action? OnChanged { get; set; }

    private int _antPhase;
    private readonly System.Diagnostics.Stopwatch _drawWatch = new();

    public CanvasView()
    {
        CanFocus = true;
        SeedGradient();
    }

    protected override bool OnKeyDown(Key key)
    {
        var handled = true;

        if (key == Key.CursorLeft || key == Key.H) MoveCursor(-1, 0);
        else if (key == Key.CursorRight || key == Key.L) MoveCursor(1, 0);
        else if (key == Key.CursorUp || key == Key.K) MoveCursor(0, -1);
        else if (key == Key.CursorDown || key == Key.J) MoveCursor(0, 1);
        else if (key == Key.X) Stamp();
        else if (key == Key.V) ToggleAnts();
        else if (key == Key.Q) Application.RequestStop();
        else handled = false;

        return handled || base.OnKeyDown(key);
    }

    private void SeedGradient()
    {
        for (var y = 0; y < CanvasHeight; y++)
        {
            for (var x = 0; x < CanvasWidth; x++)
            {
                _pixels[x, y] = (
                    (byte)(x * 255 / (CanvasWidth - 1)),
                    (byte)(y * 255 / (CanvasHeight - 1)),
                    128);
            }
        }
    }

    private void MoveCursor(int dx, int dy)
    {
        CursorX = Math.Clamp(CursorX + dx, 0, CanvasWidth - 1);
        CursorY = Math.Clamp(CursorY + dy, 0, CanvasHeight - 1);
        SetNeedsDraw();
        OnChanged?.Invoke();
    }

    private void Stamp()
    {
        _pixels[CursorX, CursorY] = _brushColor;
        _brushColor = ((byte)_rng.Next(256), (byte)_rng.Next(256), (byte)_rng.Next(256));
        SetNeedsDraw();
        OnChanged?.Invoke();
    }

    private void ToggleAnts()
    {
        AntsEnabled = !AntsEnabled;
        SetNeedsDraw();
        OnChanged?.Invoke();
    }

    public void AdvanceAnts()
    {
        _antPhase = (_antPhase + 1) % 3;
        SetNeedsDraw();
    }

    protected override bool OnDrawingContent(DrawContext context)
    {
        _drawWatch.Restart();

        for (var cellRow = 0; cellRow < CanvasHeight / 2; cellRow++)
        {
            Move(0, cellRow);
            for (var x = 0; x < CanvasWidth; x++)
            {
                var (tr, tg, tb) = _pixels[x, cellRow * 2];
                var (br, bg, bb) = _pixels[x, cellRow * 2 + 1];

                // Marching ants: border cells chase around the perimeter clockwise.
                var perimeter = PerimeterIndex(x, cellRow, CanvasWidth, CanvasHeight / 2);
                if (AntsEnabled && perimeter >= 0 && (perimeter + _antPhase) % 3 == 0)
                {
                    (tr, tg, tb) = ((byte)(255 - tr), (byte)(255 - tg), (byte)(255 - tb));
                }

                SetAttribute(new Attribute(new Color(tr, tg, tb), new Color(br, bg, bb)));
                AddRune('▀');
            }
        }

        // Brush cursor: previews the current stamp color over the pixel's other half.
        // Covers whatever the pixel holds — including a fresh stamp — until the brush moves.
        var (pr, pg, pb) = _pixels[CursorX, CursorY];
        SetAttribute(CursorY % 2 == 0
            ? new Attribute(new Color(_brushColor.R, _brushColor.G, _brushColor.B), new Color(pr, pg, pb))
            : new Attribute(new Color(pr, pg, pb), new Color(_brushColor.R, _brushColor.G, _brushColor.B)));
        Move(CursorX, CursorY / 2);
        AddRune('▀');

        _drawWatch.Stop();
        LastRedrawMs = _drawWatch.Elapsed.TotalMilliseconds;
        RedrawCount++;
        return true;
    }

    /// <summary>
    /// Position of a border cell along the perimeter ring, clockwise from top-left:
    /// top row L→R, right column T→B, bottom row R→L, left column B→T. -1 for interior cells.
    /// </summary>
    private static int PerimeterIndex(int x, int y, int width, int height)
    {
        var lastX = width - 1;
        var lastY = height - 1;
        var topLen = width;
        var rightLen = height - 1;
        var bottomLen = width - 1;

        if (y == 0) return x;                                               // top: left → right
        if (x == lastX) return topLen + y - 1;                              // right: top → bottom
        if (y == lastY) return topLen + rightLen + (lastX - x) - 1;         // bottom: right → left
        if (x == 0) return topLen + rightLen + bottomLen + (lastY - y) - 1; // left: bottom → top
        return -1;
    }
}
