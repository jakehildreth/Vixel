namespace Vixel.Core;

/// <summary>The v1 tool set. Every operation returns its undoable change.</summary>
public static class Tools
{
    /// <summary>Writes the brush footprint at (x, y) as one change.</summary>
    public static PixelChange Stamp(Canvas canvas, int x, int y, int? index, int brushSize, bool circle)
        => canvas.SetPixels(Footprint(x, y, brushSize, circle).Select(p => (p.X, p.Y, index)));

    /// <summary>Pixel writes for one step of a stroke (used inside an undo stroke boundary).</summary>
    public static IEnumerable<(int X, int Y, int? Index)> StrokePixels(int x, int y, int? index)
        => [(x, y, index)];

    /// <summary>Bresenham line with the brush stamped at each point.</summary>
    public static PixelChange Line(Canvas canvas, int x0, int y0, int x1, int y1, int? index, int brushSize, bool circle)
        => canvas.SetPixels(
            Bresenham.Line(x0, y0, x1, y1)
                .SelectMany(p => Footprint(p.X, p.Y, brushSize, circle))
                .Distinct()
                .Select(p => (p.X, p.Y, index)));

    /// <summary>Rectangle between two corners, either orientation; outline or filled.</summary>
    public static PixelChange Rect(Canvas canvas, int x0, int y0, int x1, int y1, int? index, bool filled)
    {
        var (left, right) = (Math.Min(x0, x1), Math.Max(x0, x1));
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));

        var writes = new List<(int, int, int?)>();
        for (var y = top; y <= bottom; y++)
        {
            for (var x = left; x <= right; x++)
            {
                if (filled || y == top || y == bottom || x == left || x == right)
                    writes.Add((x, y, index));
            }
        }
        return canvas.SetPixels(writes);
    }

    /// <summary>Brush footprint centered on (x, y); square N×N or inscribed circle.</summary>
    public static IEnumerable<(int X, int Y)> Footprint(int x, int y, int size, bool circle)
    {
        if (size <= 1)
        {
            yield return (x, y);
            yield break;
        }

        var radius = (size - 1) / 2.0;
        var extent = (size - 1 + 1) / 2; // integer radius covering the square
        for (var dy = -extent; dy <= extent; dy++)
        {
            for (var dx = -extent; dx <= extent; dx++)
            {
                if (circle && dx * dx + dy * dy > radius * radius + 0.5) continue;
                yield return (x + dx, y + dy);
            }
        }
    }

    /// <summary>A copied region: dimensions + pixels (null = transparent).</summary>
    public sealed record Clipboard(int Width, int Height, int?[,] Pixels);

    /// <summary>Copies the rect region (either corner orientation) into a buffer.</summary>
    public static Clipboard Copy(Canvas canvas, int x0, int y0, int x1, int y1)
    {
        var (left, right) = (Math.Min(x0, x1), Math.Max(x0, x1));
        var (top, bottom) = (Math.Min(y0, y1), Math.Max(y0, y1));
        var w = right - left + 1;
        var h = bottom - top + 1;
        var pixels = new int?[w, h];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                pixels[x, y] = canvas[left + x, top + y];
        return new Clipboard(w, h, pixels);
    }

    /// <summary>Stamps the buffer at (x, y); transparent source pixels never overwrite.</summary>
    public static PixelChange Paste(Canvas canvas, Clipboard buffer, int x, int y)
    {
        var writes = new List<(int, int, int?)>();
        for (var dy = 0; dy < buffer.Height; dy++)
            for (var dx = 0; dx < buffer.Width; dx++)
                if (buffer.Pixels[dx, dy] is { } value)
                    writes.Add((x + dx, y + dy, value));
        return canvas.SetPixels(writes);
    }
}

/// <summary>
/// Inverse-command history. Steps coalesce per the modal model: BeginStroke/EndStroke
/// merges every write inside into one step. Unbounded; survives saves; new action after
/// undo clears redo. Write order is preserved inside each step — the stack doubles as
/// the time-lapse data source via <see cref="ReplayOnto"/>.
/// </summary>
public sealed class UndoStack
{
    private readonly List<PixelChange> _undo = [];
    private readonly List<PixelChange> _redo = [];
    private List<PixelTouch>? _openStroke;
    private int _strokeWidthBefore;
    private int _strokeHeightBefore;
    private Canvas? _strokeCanvas;

    public bool CanUndo => _openStroke is null && _undo.Count > 0;
    public bool CanRedo => _openStroke is null && _redo.Count > 0;

    /// <summary>Pushes a completed action as one step. Ignores no-ops.</summary>
    public void Push(PixelChange change)
    {
        if (_openStroke is not null)
        {
            _openStroke.AddRange(change.Touched);
            return;
        }
        if (change.Touched.Count == 0) return;
        _undo.Add(change);
        _redo.Clear();
    }

    /// <summary>Opens a stroke boundary (PAINT mode entry).</summary>
    public void BeginStroke()
    {
        if (_openStroke is not null) throw new InvalidOperationException("stroke already open");
        _openStroke = [];
    }

    /// <summary>Opens a stroke, capturing canvas size for the coalesced change record.</summary>
    public void BeginStroke(Canvas canvas)
    {
        BeginStroke();
        _strokeCanvas = canvas;
        _strokeWidthBefore = canvas.Width;
        _strokeHeightBefore = canvas.Height;
    }

    /// <summary>Closes the stroke and pushes the coalesced step (PAINT mode exit).</summary>
    public void EndStroke(Canvas canvas)
    {
        if (_openStroke is null) throw new InvalidOperationException("no stroke open");
        var touched = _openStroke;
        _openStroke = null;
        _strokeCanvas = null;
        if (touched.Count == 0) return;

        // Coalesce: keep the earliest old value and the latest new value per pixel.
        var merged = new Dictionary<(int, int), PixelTouch>();
        foreach (var t in touched)
        {
            if (merged.TryGetValue((t.X, t.Y), out var prior))
                merged[(t.X, t.Y)] = prior with { New = t.New };
            else
                merged[(t.X, t.Y)] = t;
        }

        _undo.Add(new PixelChange(_strokeWidthBefore, _strokeHeightBefore, merged.Values.ToArray()));
        _redo.Clear();
    }

    public void Undo(Canvas canvas)
    {
        if (!CanUndo) return;
        var change = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        canvas.ApplyInverse(change);
        _redo.Add(change);
    }

    public void Redo(Canvas canvas)
    {
        if (!CanRedo) return;
        var change = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        canvas.ApplyForward(change);
        _undo.Add(change);
    }

    /// <summary>Replays steps onto a fresh canvas (time-lapse). upToStep is exclusive-end count.</summary>
    public void ReplayOnto(Canvas canvas, int upToStep)
    {
        foreach (var change in _undo.Take(upToStep))
            canvas.ApplyForward(change);
    }
}
