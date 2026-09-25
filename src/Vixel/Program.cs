using System.Text;
using Terminal.Gui.App;
using Terminal.Gui.Drivers;
using Terminal.Gui.Drawing;
using Terminal.Gui.Input;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;
using Vixel.Core;
using Vixel.Core.Formats;
using Attribute = Terminal.Gui.Drawing.Attribute;

// vixel — in-terminal pixel drawing. vim-modal.
// Usage: vixel [file] [--colors truecolor|256|16]

var args0 = args;
string? openPath = null;
ColorCapabilityLevel? colorOverride = null;
for (var i = 0; i < args0.Length; i++)
{
    if (args0[i] == "--colors" && i + 1 < args0.Length)
    {
        colorOverride = args0[++i].ToLowerInvariant() switch
        {
            "truecolor" => ColorCapabilityLevel.TrueColor,
            "256" => ColorCapabilityLevel.Colors256,
            "16" => ColorCapabilityLevel.Colors16,
            _ => throw new ArgumentException("--colors must be truecolor, 256, or 16"),
        };
    }
    else if (!args0[i].StartsWith('-'))
    {
        openPath = args0[i];
    }
}

var app = Application.Create();
app.Init();

var detected = TerminalEnvironmentDetector.DetectColorCapabilities();
var level = colorOverride ?? detected.Capability;
if (level == ColorCapabilityLevel.Colors16)
    Driver.Force16Colors = true;

var session = new EditorSession(openPath);
var canvasView = new CanvasView(session, level) { Width = Dim.Fill(), Height = Dim.Fill() - 2 };
var paletteBar = new PaletteBar(session)
{
    Y = Pos.Bottom(canvasView),
    Width = Dim.Fill(),
    Height = 1,
};
var statusBar = new Label
{
    Y = Pos.Bottom(paletteBar),
    Width = Dim.Fill(),
    Height = 1,
};

session.StatusChanged = () =>
{
    statusBar.Text = session.StatusLine;
    paletteBar.SetNeedsDraw();
};
var config = VixelConfig.Default();
var window = new Window { Title = "vixel" };

if (config.SplashDismissed)
{
    window.Add(canvasView, paletteBar, statusBar);
}
else
{
    SplashView? splash = null;
    void Dismiss(bool forever)
    {
        if (splash is null) return;
        if (forever) config.DismissSplashForever();
        window.Remove(splash);
        window.Add(canvasView, paletteBar, statusBar);
        canvasView.SetFocus();
        splash = null;
    }

    splash = new SplashView(level, dismissForever: () => Dismiss(forever: true), dismissOnce: () => Dismiss(forever: false), config);
    window.Add(splash);
    app.AddTimeout(TimeSpan.FromSeconds(5), () =>
    {
        Dismiss(forever: false);
        return false; // one-shot
    });
}
session.StatusChanged();

// Help overlay: shown on :help or F1; any key closes.
HelpView? help = null;
app.AddTimeout(TimeSpan.FromMilliseconds(100), () =>
{
    if (session.HelpRequested)
    {
        session.HelpRequested = false;
        if (help is null)
        {
            help = new HelpView(close: () =>
            {
                window.Remove(help);
                help = null;
                canvasView.SetFocus();
            });
            window.Add(help);
            help.SetFocus();
        }
    }
    return true;
});

// Cursor blink ticks always; marching ants only while an overlay is animated.
app.AddTimeout(TimeSpan.FromMilliseconds(200), () =>
{
    session.AdvanceBlink();
    if (session.HasAnimatedOverlay)
    {
        session.AdvanceAnts();
    }
    canvasView.SetNeedsDraw();
    return true;
});

app.Run(window);
window.Dispose();
app.Dispose();

// ---------------------------------------------------------------------------

/// <summary>Editing state: modes, current color/tool, overlay, history. Framework-free.</summary>
public sealed class EditorSession
{
    public enum Mode { Normal, Paint, Command, Line, Rect, Select, Paste }

    public Canvas Canvas;
    public Palette Palette = new();
    public string Name;
    public string? Path;
    public Mode CurrentMode = Mode.Normal;
    public readonly UndoStack History = new();

    public int CursorX;
    public int CursorY;
    public int CurrentColorIndex;
    public bool Erasing;
    public int BrushSize = 1;
    public bool CircleBrush;
    public int PalettePage;
    public bool Dirty;

    public int AnchorX; // two-point anchor
    public int AnchorY;
    public Tools.Clipboard? Clipboard;

    public int AntPhase;
    public int BlinkPhase;
    public bool HasAnimatedOverlay => CurrentMode is Mode.Line or Mode.Rect or Mode.Select or Mode.Paste;

    public string CommandBuffer = "";
    public int CommandCursor; // insertion point within CommandBuffer
    public string Message = "";

    public Action? StatusChanged;

    /// <summary>Set by :help; the TUI shows the help view and clears it.</summary>
    public bool HelpRequested;

    public EditorSession(string? path)
    {
        if (path is not null && File.Exists(path))
        {
            var (canvas, palette, name) = OpenFile(path);
            Canvas = canvas;
            Palette = palette;
            Name = name;
            Path = path;
        }
        else
        {
            Canvas = new Canvas(64, 32);
            Path = path;
            Name = path is not null ? System.IO.Path.GetFileNameWithoutExtension(path) : "untitled";
            LoadDefaultPalette();
        }
    }

    /// <summary>Loads a file by extension: .vixel (JSON) or imported .ase/.aseprite/.px/.piskel.</summary>
    public static (Canvas Canvas, Palette Palette, string Name) OpenFile(string path)
    {
        var name = System.IO.Path.GetFileNameWithoutExtension(path);
        switch (System.IO.Path.GetExtension(path).ToLowerInvariant())
        {
            case ".ase":
            case ".aseprite":
                var (aseCanvas, asePalette) = AseFormat.Import(File.ReadAllBytes(path));
                return (aseCanvas, asePalette, name);
            case ".px":
                var (pxCanvas, pxPalette) = PxFormat.Import(File.ReadAllBytes(path));
                return (pxCanvas, pxPalette, name);
            case ".piskel":
                var (piskelCanvas, piskelPalette) = PiskelFormat.Import(File.ReadAllText(path));
                return (piskelCanvas, piskelPalette, name);
            default:
                return VixelFile.Load(File.ReadAllText(path));
        }
    }

    private void LoadDefaultPalette()
    {
        // PICO-8
        string[] pico8 =
        [
            "#000000", "#1d2b53", "#7e2553", "#008751", "#ab5236", "#5f574f", "#c2c3c7", "#fff1e8",
            "#ff004d", "#ffa300", "#ffec27", "#00e436", "#29adff", "#83769c", "#ff77a8", "#ffccaa",
        ];
        foreach (var hex in pico8) Palette.AddColor(Rgb.FromHex(hex));
    }

    public int? EffectiveColor => Erasing ? null : CurrentColorIndex;

    public string StatusLine =>
        CurrentMode == Mode.Command
            ? ":" + CommandBuffer.Insert(CommandCursor, "█")
            : $" {ModeLabel} | {Name}{(Dirty ? "*" : "")} | {CursorX},{CursorY} | color {CurrentColorIndex}{(Erasing ? " (erase)" : "")}" +
              $" | brush {BrushSize}{(CircleBrush ? "○" : "□")} | {(Message.Length > 0 ? Message : "q via :q")}";
    /// <summary>Test seam: extend the palette to include index, then select it.</summary>
    public void SetPaletteForTest(int index)
    {
        while (Palette.Colors.Count <= index)
            Palette.AddColor(new Rgb((byte)Palette.Colors.Count, 0, 0));
        CurrentColorIndex = index;
    }


    private string ModeLabel => CurrentMode.ToString().ToUpperInvariant();

    public void AdvanceAnts() => AntPhase = (AntPhase + 1) % 3;
    public void AdvanceBlink() => BlinkPhase = (BlinkPhase + 1) % 2;

    // --- tool actions -------------------------------------------------------

    public void Stamp()
    {
        History.Push(Tools.Stamp(Canvas, CursorX, CursorY, EffectiveColor, BrushSize, CircleBrush));
        Dirty = true;
    }

    /// <summary>Erases the brush footprint under the cursor (vi x semantics).</summary>
    public void EraseUnderBrush()
    {
        History.Push(Tools.Stamp(Canvas, CursorX, CursorY, null, BrushSize, CircleBrush));
        Dirty = true;
    }

    /// <summary>Erases the selected region (vi d on a visual selection).</summary>
    public void DeleteRegion()
    {
        History.Push(Tools.Rect(Canvas, AnchorX, AnchorY, CursorX, CursorY, null, filled: true));
        CurrentMode = Mode.Normal;
        Dirty = true;
        Message = "deleted region";
    }

    public void Fill()
    {
        History.Push(FloodFill.Fill(Canvas, CursorX, CursorY, EffectiveColor));
        Dirty = true;
    }

    public void EnterPaint() { CurrentMode = Mode.Paint; History.BeginStroke(Canvas); PaintStep(); }

    public void PaintStep()
    {
        History.Push(Tools.Stamp(Canvas, CursorX, CursorY, EffectiveColor, BrushSize, CircleBrush));
        Dirty = true;
    }

    public void ExitPaint() { History.EndStroke(Canvas); CurrentMode = Mode.Normal; }

    public void Eyedrop()
    {
        if (Canvas[CursorX, CursorY] is { } i)
        {
            CurrentColorIndex = i;
            Erasing = false;
        }
    }

    public void BeginTwoPoint(Mode mode) { AnchorX = CursorX; AnchorY = CursorY; CurrentMode = mode; }

    public void CommitTwoPoint(bool shifted = false)
    {
        switch (CurrentMode)
        {
            case Mode.Line:
                History.Push(Tools.Line(Canvas, AnchorX, AnchorY, CursorX, CursorY, EffectiveColor, BrushSize, CircleBrush));
                break;
            case Mode.Rect:
                History.Push(Tools.Rect(Canvas, AnchorX, AnchorY, CursorX, CursorY, EffectiveColor, filled: shifted, BrushSize));
                break;
            case Mode.Select:
                Clipboard = Tools.Copy(Canvas, AnchorX, AnchorY, CursorX, CursorY);
                Message = $"yanked {Clipboard.Width}x{Clipboard.Height}";
                break;
            case Mode.Paste when Clipboard is not null:
                History.Push(Tools.Paste(Canvas, Clipboard, CursorX, CursorY));
                break;
        }
        Dirty = CurrentMode is Mode.Line or Mode.Rect or Mode.Paste ? true : Dirty;
        CurrentMode = Mode.Normal;
    }

    public void CancelMode() { CurrentMode = Mode.Normal; Message = ""; }

    public void Undo() { History.Undo(Canvas); Dirty = true; }
    public void Redo() { History.Redo(Canvas); Dirty = true; }

    public void Move(int dx, int dy)
    {
        CursorX = Math.Clamp(CursorX + dx, 0, Canvas.Width - 1);
        CursorY = Math.Clamp(CursorY + dy, 0, Canvas.Height - 1);
        if (CurrentMode == Mode.Paint) PaintStep();
    }

    // --- command line -------------------------------------------------------

    public bool ExecuteCommand(string command, out bool quit)
    {
        quit = false;
        var parts = command.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return true;

        try
        {
            return ExecuteCommandCore(parts, ref quit);
        }
        catch (Exception e)
        {
            Message = $"error: {e.Message}";
            return true;
        }
    }

    /// <summary>Expands a leading ~ to the user's home directory.</summary>
    private static string ExpandPath(string path) =>
        path == "~" ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        : path.StartsWith("~/") ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + path[1..]
        : path;

    /// <summary>Stars the repo via an authed gh CLI. Null when gh is missing, unauthed, or the call fails.</summary>
    private static string? StarViaGitHubCli()
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("gh", "api -X PUT /user/starred/jakehildreth/Vixel")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = System.Diagnostics.Process.Start(psi);
            if (process is null) return null;
            if (!process.WaitForExit(5000)) { process.Kill(); return null; }
            return process.ExitCode == 0 ? "starred jakehildreth/Vixel via gh" : null;
        }
        catch (Exception) { return null; } // gh not installed
    }

    private static string OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
            return $"opened {new Uri(url).Host}/jakehildreth/vixel — star it there";
        }
        catch (Exception) { return $"star: {url}"; }
    }

    private bool ExecuteCommandCore(string[] parts, ref bool quit)
    {

        switch (parts[0])
        {
            case "w":
                var target = parts.Length > 1 ? ExpandPath(parts[1]) : Path ?? Name + ".vixel";
                File.WriteAllText(target, VixelFile.Save(Canvas, Palette, Name));
                Path = target;
                Dirty = false;
                Message = $"wrote {target}";
                return true;
            case "star":
                Message = StarViaGitHubCli() ?? OpenInBrowser("https://github.com/jakehildreth/vixel");
                return true;
            case "help":
                HelpRequested = true;
                Message = "help";
                return true;
            case "clear":
            case "%d":
                History.Push(Canvas.SetPixels(
                    Enumerable.Range(0, Canvas.Width).SelectMany(x =>
                        Enumerable.Range(0, Canvas.Height).Select(y => (x, y, (int?)null)))));
                Dirty = true;
                Message = "cleared";
                return true;
            case "q":
                if (Dirty && parts[0] == "q")
                {
                    Message = "unsaved changes — :q! to discard";
                    return true;
                }
                quit = true;
                return true;
            case "q!":
                quit = true;
                return true;
            case "wq":
                ExecuteCommand("w", out _);
                quit = true;
                return true;
            case "e" when parts.Length > 1:
                try
                {
                    var openTarget = ExpandPath(parts[1]);
                    var (canvas, palette, name) = OpenFile(openTarget);
                    Canvas = canvas; Palette = palette; Name = name; Path = openTarget;
                    Dirty = false;
                    CursorX = CursorY = 0;
                    Message = $"opened {openTarget}";
                }
                catch (Exception e) { Message = e.Message; }
                return true;
            case "color" when parts.Length > 1:
                try
                {
                    CurrentColorIndex = Palette.AddColor(Rgb.FromHex(parts[1]));
                    Erasing = false;
                    Message = $"color {CurrentColorIndex}";
                }
                catch (FormatException) { Message = "usage: :color #rrggbb"; }
                return true;
            case "export" when parts.Length > 1:
                var scale = parts.Length > 2 && int.TryParse(parts[2], out var s) ? s : 1;
                var exportTarget = ExpandPath(parts[1]);
                File.WriteAllBytes(exportTarget, PngFormat.Export(Canvas, Palette, scale));
                Message = $"exported {exportTarget} ×{scale}";
                return true;
            case "new":
                var w = parts.Length > 1 && int.TryParse(parts[1].Split('x')[0], out var nw) ? nw : 64;
                var h = parts.Length > 1 && parts[1].Contains('x') && int.TryParse(parts[1].Split('x')[1], out var nh) ? nh : 32;
                Canvas = new Canvas(Math.Min(w, 512), Math.Min(h, 512));
                Dirty = false;
                Message = $"new canvas {Canvas.Width}x{Canvas.Height}";
                return true;
            case "resize" when parts.Length > 1:
                var rw = int.Parse(parts[1].Split('x')[0]);
                var rh = int.Parse(parts[1].Split('x')[1]);
                History.Push(Canvas.Resize(Math.Min(rw, 512), Math.Min(rh, 512)));
                Dirty = true;
                return true;
            default:
                Message = $"unknown :{parts[0]}";
                return true;
        }
    }
}

/// <summary>The canvas view: half-block rendering, overlay compositing, ants, cursor.</summary>
public sealed class CanvasView : View
{
    private readonly EditorSession _s;
    private readonly ColorCapabilityLevel _level;
    private readonly Dictionary<Rgb, Rgb> _quantizeCache = new();

    public CanvasView(EditorSession session, ColorCapabilityLevel level)
    {
        _s = session;
        _level = level;
        CanFocus = true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (_s.CurrentMode == EditorSession.Mode.Command)
        {
            if (key == Key.Enter)
            {
                _s.ExecuteCommand(_s.CommandBuffer, out var quit);
                _s.CommandBuffer = "";
                _s.CommandCursor = 0;
                _s.CurrentMode = EditorSession.Mode.Normal;
                if (quit) App?.RequestStop();
            }
            else if (key == Key.Esc)
            {
                _s.CommandBuffer = "";
                _s.CommandCursor = 0;
                _s.CurrentMode = EditorSession.Mode.Normal;
            }
            else if (key == Key.CursorLeft && _s.CommandCursor > 0)
            {
                _s.CommandCursor--;
            }
            else if (key == Key.CursorRight && _s.CommandCursor < _s.CommandBuffer.Length)
            {
                _s.CommandCursor++;
            }
            else if (key == Key.Home) { _s.CommandCursor = 0; }
            else if (key == Key.End) { _s.CommandCursor = _s.CommandBuffer.Length; }
            else if (key == Key.Backspace && _s.CommandCursor > 0)
            {
                _s.CommandBuffer = _s.CommandBuffer.Remove(_s.CommandCursor - 1, 1);
                _s.CommandCursor--;
            }
            else if (key == Key.Delete && _s.CommandCursor < _s.CommandBuffer.Length)
            {
                _s.CommandBuffer = _s.CommandBuffer.Remove(_s.CommandCursor, 1);
            }
            else if (key.TryGetPrintableRune(out var rune) && rune.Value != 0)
            {
                _s.CommandBuffer = _s.CommandBuffer.Insert(_s.CommandCursor, ((char)rune.Value).ToString());
                _s.CommandCursor++;
            }
            SetNeedsDraw();
            _s.StatusChanged?.Invoke();
            return true;
        }

        var handled = true;
        if (key == Key.CursorLeft || key == Key.H) _s.Move(-1, 0);
        else if (key == Key.CursorRight || key == Key.L) _s.Move(1, 0);
        else if (key == Key.CursorUp || key == Key.K) _s.Move(0, -1);
        else if (key == Key.CursorDown || key == Key.J) _s.Move(0, 1);
        else if (key == Key.X && _s.CurrentMode == EditorSession.Mode.Normal) _s.EraseUnderBrush();
        else if (key == Key.Space && _s.CurrentMode == EditorSession.Mode.Normal) _s.Stamp();
        else if (key == Key.R && _s.CurrentMode == EditorSession.Mode.Normal) _s.Stamp();
        else if (key == Key.F && _s.CurrentMode == EditorSession.Mode.Normal) _s.Fill();
        else if (key == Key.I && _s.CurrentMode == EditorSession.Mode.Normal) _s.EnterPaint();
        else if (key == Key.Esc) { if (_s.CurrentMode == EditorSession.Mode.Paint) _s.ExitPaint(); else _s.CancelMode(); }
        else if (key == Key.P.WithShift && _s.CurrentMode == EditorSession.Mode.Normal) _s.Eyedrop();
        else if (key == Key.E && _s.CurrentMode == EditorSession.Mode.Normal) _s.Erasing = !_s.Erasing;
        else if (key == Key.O && _s.CurrentMode == EditorSession.Mode.Normal) _s.CircleBrush = !_s.CircleBrush;
        else if (key == Key.L.WithShift && _s.CurrentMode == EditorSession.Mode.Normal) _s.BeginTwoPoint(EditorSession.Mode.Line);
        else if (key == Key.R.WithShift && _s.CurrentMode == EditorSession.Mode.Normal) _s.BeginTwoPoint(EditorSession.Mode.Rect);
        else if (key == Key.V && _s.CurrentMode == EditorSession.Mode.Normal) _s.BeginTwoPoint(EditorSession.Mode.Select);
        else if (key == Key.P && _s.CurrentMode == EditorSession.Mode.Select) { _s.CommitTwoPoint(); }
        else if (key == Key.D && _s.CurrentMode == EditorSession.Mode.Select) { _s.DeleteRegion(); }
        else if (key == Key.Y && _s.CurrentMode == EditorSession.Mode.Select) { _s.CommitTwoPoint(); }
        else if (key == Key.P && _s.CurrentMode == EditorSession.Mode.Normal && _s.Clipboard is not null) _s.BeginTwoPoint(EditorSession.Mode.Paste);
        else if (key == Key.Enter && _s.CurrentMode is EditorSession.Mode.Line or EditorSession.Mode.Rect or EditorSession.Mode.Paste) _s.CommitTwoPoint();
        else if (key == Key.Enter.WithShift && _s.CurrentMode == EditorSession.Mode.Rect) _s.CommitTwoPoint(shifted: true);
        else if (key == Key.U && _s.CurrentMode == EditorSession.Mode.Normal) _s.Undo();
        else if (key == Key.R.WithCtrl && _s.CurrentMode == EditorSession.Mode.Normal) _s.Redo();
        else if (key.TryGetPrintableRune(out var colon) && colon.Value == ':') { _s.CurrentMode = EditorSession.Mode.Command; _s.CommandBuffer = ""; _s.CommandCursor = 0; }
        else if (key.TryGetPrintableRune(out var digit) && digit.Value >= '1' && digit.Value <= '9') SetPaletteSlot(digit.Value - '1');
        else if (key.TryGetPrintableRune(out var minus) && minus.Value is '-' or '_') { _s.BrushSize = Math.Max(1, _s.BrushSize - 1); }
        else if (key.TryGetPrintableRune(out var plus) && plus.Value is '=' or '+') { _s.BrushSize = Math.Min(9, _s.BrushSize + 1); }
        else if (key == Key.Tab) { _s.PalettePage++; }
        else if (key == Key.Tab.WithShift) { _s.PalettePage = Math.Max(0, _s.PalettePage - 1); }
        else if (key == Key.F1) { _s.HelpRequested = true; }
        else handled = false;

        SetNeedsDraw();
        _s.StatusChanged?.Invoke();
        return handled || base.OnKeyDown(key);
    }

    private void SetPaletteSlot(int slot)
    {
        var index = _s.PalettePage * 10 + slot;
        if (index < _s.Palette.Colors.Count)
        {
            _s.CurrentColorIndex = index;
            _s.Erasing = false;
        }
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        var canvas = _s.Canvas;
        var cellRows = (canvas.Height + 1) / 2;

        for (var cellRow = 0; cellRow < cellRows; cellRow++)
        {
            Move(0, cellRow);
            for (var x = 0; x < canvas.Width; x++)
            {
                var top = RenderColor(canvas[x, cellRow * 2]) ?? BackgroundShade(x, cellRow * 2);
                var bottom = cellRow * 2 + 1 < canvas.Height
                    ? RenderColor(canvas[x, cellRow * 2 + 1]) ?? BackgroundShade(x, cellRow * 2 + 1)
                    : (Rgb?)null;

                var (fg, bg) = CellColors(top, bottom);
                SetAttribute(new Attribute(fg, bg));
                AddRune(GlyphFor(top, bottom));
            }
        }

        DrawOverlay();
        DrawCursor();
        return true;
    }

    private (Color Fg, Color Bg) CellColors(Rgb? top, Rgb? bottom)
    {
        // ▀ draws the top half from fg; ▄ draws the bottom half from fg as well.
        // fg always carries the drawn half's color: top when set, else bottom.
        // bg carries the other half when both are set; otherwise black.
        var fg = top.HasValue ? ToTerminal(top.Value) : (bottom.HasValue ? ToTerminal(bottom.Value) : Color.Black);
        var bg = top.HasValue && bottom.HasValue ? ToTerminal(bottom.Value) : Color.Black;
        return (fg, bg);
    }

    private static char GlyphFor(Rgb? top, Rgb? bottom) =>
        (top.HasValue, bottom.HasValue) switch
        {
            (true, true) => '▀',
            (true, false) => '▀',
            (false, true) => '▄',
            (false, false) => ' ',
        };

    /// <summary>The 2×2-pixel checkerboard shade for an empty pixel: 2 cells wide × 1 cell row per check.</summary>
    private static Rgb BackgroundShade(int x, int y) =>
        (x / 2 + y / 2) % 2 == 0 ? new Rgb(38, 38, 38) : new Rgb(22, 22, 22);
    public static Rgb BackgroundShadeForTest(int x, int y) => BackgroundShade(x, y);


    // Test seams: expose the cell-mapping decisions without a terminal.
    public (Color Fg, Color Bg) CellColorsForTest(Rgb? top, Rgb? bottom) => CellColors(top, bottom);
    public static char GlyphForTest(Rgb? top, Rgb? bottom) => GlyphFor(top, bottom);

    private void DrawOverlay()
    {
        // Collect overlay pixels per half-block cell, then render each cell once —
        // writing halves independently would clobber the first half with the second's bg.
        var cells = new Dictionary<(int X, int Row), (bool Top, bool Bottom)>();

        void Mark(int x, int y)
        {
            if (x < 0 || x >= _s.Canvas.Width || y < 0 || y >= _s.Canvas.Height) return;
            var key = (x, y / 2);
            cells.TryGetValue(key, out var halves);
            cells[key] = y % 2 == 0 ? (true, halves.Bottom) : (halves.Top, true);
        }

        switch (_s.CurrentMode)
        {
            case EditorSession.Mode.Line:
                // Preview the committed shape: Bresenham spine with the brush footprint at each point.
                foreach (var p in Bresenham.Line(_s.AnchorX, _s.AnchorY, _s.CursorX, _s.CursorY)
                             .SelectMany(p => Tools.Footprint(p.X, p.Y, _s.BrushSize, _s.CircleBrush)))
                    Mark(p.X, p.Y);
                break;
            case EditorSession.Mode.Rect:
            case EditorSession.Mode.Select:
                var (l, r) = (Math.Min(_s.AnchorX, _s.CursorX), Math.Max(_s.AnchorX, _s.CursorX));
                var (t, b) = (Math.Min(_s.AnchorY, _s.CursorY), Math.Max(_s.AnchorY, _s.CursorY));
                // Rect previews the committed outline: brush-thick, growing inward. Select stays 1px ants.
                var thickness = _s.CurrentMode == EditorSession.Mode.Rect ? Math.Max(1, _s.BrushSize) : 1;
                for (var y = t; y <= b; y++)
                    for (var x = l; x <= r; x++)
                        if (y - t < thickness || b - y < thickness || x - l < thickness || r - x < thickness)
                        {
                            if (_s.CurrentMode == EditorSession.Mode.Select
                                && (PerimeterIndex(x - l, y - t, r - l + 1, b - t + 1) + _s.AntPhase) % 3 != 0)
                                continue; // marching ants: only every third perimeter pixel is lit
                            Mark(x, y);
                        }
                break;
            case EditorSession.Mode.Paste when _s.Clipboard is not null:
                for (var dy = 0; dy < _s.Clipboard.Height; dy++)
                    for (var dx = 0; dx < _s.Clipboard.Width; dx++)
                        if (_s.Clipboard.Pixels[dx, dy] is not null)
                            Mark(_s.CursorX + dx, _s.CursorY + dy);
                break;
        }

        // Transient pixels flash like the cursor: full color ↔ 50% dim.
        var rgb = _s.EffectiveColor is { } i ? _s.Palette.Colors[i] : new Rgb(255, 255, 255);
        var shown = _s.BlinkPhase == 0 ? rgb : new Rgb((byte)(rgb.R / 2), (byte)(rgb.G / 2), (byte)(rgb.B / 2));
        var term = ToTerminal(shown);

        foreach (var ((x, row), (top, bottom)) in cells)
        {
            var otherY = top ? row * 2 + 1 : row * 2;
            var other = top && bottom
                ? term
                : ToTerminal(otherY < _s.Canvas.Height && RenderColor(_s.Canvas[x, otherY]) is { } o
                    ? o
                    : BackgroundShade(x, otherY));
            SetAttribute(new Attribute(term, other));
            Move(x, row);
            AddRune(top ? '▀' : '▄');
        }
    }


    private void DrawCursor()
    {
        // The brush renders its full footprint (N×N square or inscribed circle, centered
        // on the cursor) flashing full color ↔ 50% dim. Covers pixels beneath (stamp-reveal).
        // Footprint pixels sharing a cell are rendered together — writing halves
        // independently would clobber the first half with the second's bg.
        var rgb = _s.EffectiveColor is { } i ? _s.Palette.Colors[i] : new Rgb(255, 255, 255);
        var shown = _s.BlinkPhase == 0 ? rgb : new Rgb((byte)(rgb.R / 2), (byte)(rgb.G / 2), (byte)(rgb.B / 2));
        var term = ToTerminal(shown);

        var cells = new Dictionary<(int X, int Row), (bool Top, bool Bottom)>();
        foreach (var (px, py) in Tools.Footprint(_s.CursorX, _s.CursorY, _s.BrushSize, _s.CircleBrush))
        {
            if (px < 0 || px >= _s.Canvas.Width || py < 0 || py >= _s.Canvas.Height) continue;
            var key = (px, py / 2);
            cells.TryGetValue(key, out var halves);
            cells[key] = py % 2 == 0 ? (true, halves.Bottom) : (halves.Top, true);
        }

        foreach (var ((x, row), (top, bottom)) in cells)
        {
            var otherY = top ? row * 2 + 1 : row * 2;
            var other = top && bottom
                ? term
                : ToTerminal(otherY < _s.Canvas.Height && RenderColor(_s.Canvas[x, otherY]) is { } o
                    ? o
                    : BackgroundShade(x, otherY));
            SetAttribute(new Attribute(term, other));
            Move(x, row);
            AddRune(top ? '▀' : '▄');
        }
    }

    private Color ToTerminal(Rgb rgb)
    {
        if (_level == ColorCapabilityLevel.Colors256)
        {
            if (_quantizeCache.TryGetValue(rgb, out var cached)) return new Color(cached.R, cached.G, cached.B);
            var q = Xterm256.Palette[Xterm256.ToIndex(rgb)];
            _quantizeCache[rgb] = q;
            return new Color(q.R, q.G, q.B);
        }
        return new Color(rgb.R, rgb.G, rgb.B);
    }

    private static int PerimeterIndex(int x, int y, int width, int height)
    {
        if (width <= 1 || height <= 1) return x + y;
        var lastX = width - 1;
        var lastY = height - 1;
        var topLen = width;
        var rightLen = height - 1;
        var bottomLen = width - 1;
        if (y == 0) return x;
        if (x == lastX) return topLen + y - 1;
        if (y == lastY) return topLen + rightLen + (lastX - x) - 1;
        if (x == 0) return topLen + rightLen + bottomLen + (lastY - y) - 1;
        return -1;
    }

    private Rgb? RenderColor(int? index) =>
        index is { } i ? _s.Palette.Colors[i] : null;
}

/// <summary>Palette bar: current palette page, active slot highlighted.</summary>
public sealed class PaletteBar : View
{
    private readonly EditorSession _s;

    public PaletteBar(EditorSession session) => _s = session;

    protected override bool OnDrawingContent(DrawContext? context)
    {
        Move(0, 0);
        var start = _s.PalettePage * 10;
        for (var slot = 0; slot < 10; slot++)
        {
            var index = start + slot;
            if (index >= _s.Palette.Colors.Count) break;
            var c = _s.Palette.Colors[index];
            var attr = new Attribute(new Color(c.R, c.G, c.B), new Color(c.R, c.G, c.B));
            SetAttribute(attr);
            AddStr(index == _s.CurrentColorIndex ? "██" : "▄▄");
            SetAttribute(new Attribute(Color.White, Color.Black));
            AddStr(" ");
        }
        return true;
    }
}

/// <summary>The startup splash: the Pixquare wordmark centered. Esc dismisses forever; any other key continues this session only.</summary>
public sealed class SplashView : View
{
    private readonly ColorCapabilityLevel _level;
    private readonly Action _dismissForever;
    private readonly Action _dismissOnce;
    private readonly VixelConfig _config;

    public SplashView(ColorCapabilityLevel level, Action dismissForever, Action dismissOnce, VixelConfig config)
    {
        _level = level;
        _dismissForever = dismissForever;
        _dismissOnce = dismissOnce;
        _config = config;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
    }

    private static string[] LoadEmbeddedSplash()
    {
        var asm = typeof(SplashView).Assembly;
        using var stream = asm.GetManifestResourceStream("Vixel.splash.txt");
        if (stream is null) return ["ViXeL - Vi for piXeLs"];
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.Esc) _dismissForever();
        else _dismissOnce();
        return true;
    }

    /// <summary>
    /// Pure splash layout: given text lines and viewport dims, returns art/text origin and
    /// the block's left edge. Extracted for tests — the composite (art + 1 blank + text) is
    /// centered vertically as a unit; text block is centered horizontally by longest line.
    /// </summary>
    public static (int BlockTop, int TextLeft, int TextTop) Layout(IReadOnlyList<string> lines, int viewportWidth, int viewportHeight)
    {
        var cellRows = SplashArt.Height / 2;
        var totalRows = cellRows + 1 + lines.Count;
        var blockTop = Math.Max(0, (viewportHeight - totalRows) / 2);
        var blockWidth = lines.Count == 0 ? 0 : lines.Max(l => l.Length);
        var textLeft = Math.Max(0, (viewportWidth - blockWidth) / 2);
        return (blockTop, textLeft, blockTop + cellRows + 1);
    }


    protected override bool OnDrawingContent(DrawContext? context)
    {
        // Package version (CalVer) travels in AssemblyInformationalVersion on every build.
        var informational = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(
                typeof(EditorSession).Assembly)?.InformationalVersion;
        var versionText = informational is null ? "" : informational.Split('+')[0]; // strip commit hash suffix
        var raw = _config.LoadSplashOverride() ?? LoadEmbeddedSplash();
        var lines = raw.Select(l => l.Replace("{version}", versionText)).ToArray();

        var cellRows = SplashArt.Height / 2;
        var (blockTop, textLeft, textTop) = Layout(lines, Viewport.Width, Viewport.Height);
        var originX = Math.Max(0, (Viewport.Width - SplashArt.Width) / 2);
        var originY = blockTop;

        for (var cellRow = 0; cellRow < cellRows && originY + cellRow < Viewport.Height; cellRow++)
        {
            for (var x = 0; x < SplashArt.Width; x++)
            {
                var ti = (cellRow * 2) * SplashArt.Width + x;
                var bi = (cellRow * 2 + 1) * SplashArt.Width + x;
                var top = SplashArt.Pixels[ti] is >= 0 and var t ? SplashArt.Palette[t] : (Rgb?)null;
                var bottom = SplashArt.Pixels[bi] is >= 0 and var b ? SplashArt.Palette[b] : (Rgb?)null;
                if (top is null && bottom is null) continue;

                var cell = CellMapping.Map(top, bottom, x, cellRow * 2);
                SetAttribute(new Attribute(ToTerm(cell.Fg), ToTerm(cell.Bg)));
                Move(originX + x, originY + cellRow);
                AddRune(cell.Glyph);
            }
        }

        // Color.None = terminal default fg/bg: normal text, no black box behind glyphs.
        // Positions come from Layout (unit-tested).
        SetAttribute(new Attribute(Color.None, Color.None));
        for (var i = 0; i < lines.Length; i++)
        {
            if (lines[i].Length == 0 || textTop + i >= Viewport.Height) continue;
            Move(textLeft, textTop + i);
            AddStr(lines[i]);
        }

        var hint = "esc: never show again · any other key: continue";
        Move(Math.Max(0, (Viewport.Width - hint.Length) / 2), Viewport.Height - 1);
        SetAttribute(new Attribute(Color.None, Color.None));
        AddStr(hint);
        return true;
    }

    private Color ToTerm(Rgb rgb) => new(rgb.R, rgb.G, rgb.B);
}

/// <summary>On-line help: the full key + command reference. Scrolls with j/k/arrows; any other key closes.</summary>
public sealed class HelpView : View
{
    private static readonly string[] Lines =
    [
        "VIXEL HELP",
        "",
        "Movement",
        "  h j k l / arrows   move the brush",
        "",
        "Editing",
        "  Space / r          stamp a pixel (brush does not move)",
        "  x                  erase pixel(s) under the brush",
        "  i                  PAINT mode: movement draws until Esc",
        "  e                  eraser toggle",
        "  f                  flood fill",
        "",
        "Shapes",
        "  L                  line: move to second point, Enter commits",
        "  R                  rectangle: Enter commits, Shift+Enter fills",
        "",
        "Selection",
        "  v                  start region; move to far corner",
        "  y                  yank the selection",
        "  d                  erase the selection",
        "  p                  paste floats at brush, Enter stamps",
        "",
        "Color and brush",
        "  P                  eyedropper (pick color under brush)",
        "  1-9, 0             palette slots; Tab / Shift+Tab flip pages",
        "  -/_  =/+           brush size down / up",
        "  o                  brush shape: square / circle",
        "",
        "History",
        "  u                  undo",
        "  Ctrl+R             redo",
        "",
        "Commands",
        "  :w [file]          write .vixel",
        "  :q  :q!  :wq       quit (with save / discard)",
        "  :e <file>          open .vixel/.ase/.aseprite/.px/.piskel",
        "  :export <png> [n]  PNG export, integer scale",
        "  :color #rrggbb     add palette color",
        "  :new [WxH]         fresh canvas (default 64x32)",
        "  :resize WxH        resize canvas",
        "  :clear  :%d        clear canvas",
        "  :star              open the GitHub repo",
        "  :help  <F1>        this help",
        "",
        "Esc                exit PAINT / cancel / close this help",
    ];

    private readonly Action _close;
    private int _scroll;

    public HelpView(Action close)
    {
        _close = close;
        Width = Dim.Fill();
        Height = Dim.Fill();
        CanFocus = true;
    }

    protected override bool OnKeyDown(Key key)
    {
        if (key == Key.J || key == Key.CursorDown)
            _scroll = Math.Min(Math.Max(0, Lines.Length - Viewport.Height), _scroll + 1);
        else if (key == Key.K || key == Key.CursorUp)
            _scroll = Math.Max(0, _scroll - 1);
        else
            _close();
        return true;
    }

    protected override bool OnDrawingContent(DrawContext? context)
    {
        SetAttribute(new Attribute(Color.None, Color.None));
        var maxWidth = Lines.Max(l => l.Length);
        var originX = Math.Max(0, (Viewport.Width - maxWidth) / 2);
        for (var row = 0; row < Viewport.Height; row++)
        {
            var lineIndex = _scroll + row;
            if (lineIndex >= Lines.Length) break;
            Move(originX, row);
            AddStr(Lines[lineIndex]);
        }
        return true;
    }
}
