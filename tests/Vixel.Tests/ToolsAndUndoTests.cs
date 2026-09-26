using Vixel.Core;

namespace Vixel.Tests;

[TestFixture]
public class ToolTests
{
    [Test]
    public void Stamp_writes_single_pixel_as_one_change()
    {
        var canvas = new Canvas(8, 8);
        var change = Tools.Stamp(canvas, 3, 4, 2, brushSize: 1, circle: false);
        Assert.Multiple(() =>
        {
            Assert.That(canvas[3, 4], Is.EqualTo(2));
            Assert.That(change.Touched, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void Line_draws_bresenham_with_brush()
    {
        var canvas = new Canvas(8, 8);
        var change = Tools.Line(canvas, 0, 0, 4, 4, 1, brushSize: 1, circle: false);
        Assert.Multiple(() =>
        {
            Assert.That(change.Touched, Has.Count.EqualTo(5));
            Assert.That(canvas[0, 0], Is.EqualTo(1));
            Assert.That(canvas[4, 4], Is.EqualTo(1));
        });
    }

    [Test]
    public void Rect_outline_touches_border_only()
    {
        var canvas = new Canvas(6, 6);
        var change = Tools.Rect(canvas, 1, 1, 4, 4, 1, filled: false);
        Assert.Multiple(() =>
        {
            Assert.That(change.Touched, Has.Count.EqualTo(12), "4x4 outline = 12 border pixels");
            Assert.That(canvas[2, 2], Is.Null, "interior untouched");
            Assert.That(canvas[1, 1], Is.EqualTo(1));
        });
    }

    [Test]
    public void Rect_outline_thickens_with_brush_size()
    {
        var canvas = new Canvas(10, 10);
        Tools.Rect(canvas, 1, 1, 8, 8, 1, filled: false, brushSize: 2);
        Assert.Multiple(() =>
        {
            Assert.That(canvas[2, 2], Is.EqualTo(1), "thickness 2 covers (2,2)");
            Assert.That(canvas[2, 1], Is.EqualTo(1));
            Assert.That(canvas[3, 3], Is.Null, "interior beyond thickness untouched");
        });
    }

    [Test]
    public void Rect_outline_default_brush_matches_legacy_1px()
    {
        var canvas = new Canvas(6, 6);
        var change = Tools.Rect(canvas, 1, 1, 4, 4, 1, filled: false);
        Assert.That(change.Touched, Has.Count.EqualTo(12), "default brushSize=1 keeps the 1px outline");
    }

    [Test]
    public void Line_brush_size_fattens_committed_pixels()
    {
        var canvas = new Canvas(10, 10);
        var change = Tools.Line(canvas, 0, 0, 9, 0, 1, brushSize: 3, circle: false);
        Assert.Multiple(() =>
        {
            Assert.That(canvas[5, 0], Is.EqualTo(1));
            Assert.That(canvas[5, 1], Is.EqualTo(1), "brush 3 covers one row below the spine");
            Assert.That(change.Touched.Count, Is.GreaterThan(10), "more than a 1px line");
        });
    }

    [Test]
    public void Rect_filled_touches_interior()
    {
        var canvas = new Canvas(6, 6);
        Tools.Rect(canvas, 1, 1, 3, 3, 1, filled: true);
        Assert.Multiple(() =>
        {
            Assert.That(canvas[2, 2], Is.EqualTo(1));
            Assert.That(canvas[3, 3], Is.EqualTo(1));
        });
    }

    [Test]
    public void Rect_handles_reversed_corners()
    {
        var canvas = new Canvas(8, 8);
        Tools.Rect(canvas, 4, 4, 1, 1, 1, filled: false); // dragged up-left
        Assert.Multiple(() =>
        {
            Assert.That(canvas[1, 1], Is.EqualTo(1));
            Assert.That(canvas[4, 4], Is.EqualTo(1));
        });
    }

    [Test]
    public void Circle_brush_stamps_round_footprint()
    {
        var canvas = new Canvas(9, 9);
        var change = Tools.Stamp(canvas, 4, 4, 1, brushSize: 3, circle: true);
        // 3x3 circle = the plus-shaped 5 pixels (corners excluded)
        Assert.That(change.Touched, Has.Count.EqualTo(5));
        Assert.That(canvas[3, 3], Is.Null, "corner excluded");
    }

    [Test]
    public void Square_brush_stamps_full_footprint()
    {
        var canvas = new Canvas(9, 9);
        var change = Tools.Stamp(canvas, 4, 4, 1, brushSize: 3, circle: false);
        Assert.That(change.Touched, Has.Count.EqualTo(9));
    }

    [Test]
    public void Brush_clips_at_canvas_edge()
    {
        var canvas = new Canvas(4, 4);
        var change = Tools.Stamp(canvas, 0, 0, 1, brushSize: 3, circle: false);
        Assert.That(change.Touched, Has.Count.EqualTo(4), "3x3 brush at corner clips to 2x2");
    }

    [Test]
    public void Copy_and_paste_moves_region_pixels()
    {
        var canvas = new Canvas(8, 8);
        canvas.SetPixel(0, 0, 1);
        canvas.SetPixel(1, 0, 2);

        var buffer = Tools.Copy(canvas, 0, 0, 1, 0);
        var change = Tools.Paste(canvas, buffer, 4, 4);

        Assert.Multiple(() =>
        {
            Assert.That(canvas[4, 4], Is.EqualTo(1));
            Assert.That(canvas[5, 4], Is.EqualTo(2));
            Assert.That(canvas[0, 0], Is.EqualTo(1), "source preserved (copy, not cut)");
            Assert.That(change.Touched, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void Paste_skips_transparent_source_pixels()
    {
        var canvas = new Canvas(8, 8);
        canvas.SetPixel(0, 0, 1); // (1,0) stays transparent
        canvas.SetPixel(5, 4, 9); // destination has art

        var buffer = Tools.Copy(canvas, 0, 0, 1, 0);
        var change = Tools.Paste(canvas, buffer, 4, 4);

        Assert.Multiple(() =>
        {
            Assert.That(canvas[4, 4], Is.EqualTo(1), "opaque pastes");
            Assert.That(canvas[5, 4], Is.EqualTo(9), "transparent source never overwrites");
            Assert.That(change.Touched, Has.Count.EqualTo(1));
        });
    }
}

[TestFixture]
public class UndoStackTests
{
    [Test]
    public void Undo_reverts_last_action_and_redo_reapplies()
    {
        var canvas = new Canvas(8, 8);
        var history = new UndoStack();

        history.Push(Tools.Stamp(canvas, 1, 1, 2, 1, false));
        Assert.That(canvas[1, 1], Is.EqualTo(2));

        history.Undo(canvas);
        Assert.That(canvas[1, 1], Is.Null);

        history.Redo(canvas);
        Assert.That(canvas[1, 1], Is.EqualTo(2));
    }

    [Test]
    public void Stroke_is_one_undo_step()
    {
        var canvas = new Canvas(8, 8);
        var history = new UndoStack();

        history.BeginStroke(canvas);
        history.Push(canvas.SetPixels(Tools.StrokePixels(0, 0, 1)));
        history.Push(canvas.SetPixels(Tools.StrokePixels(1, 1, 1)));
        history.Push(canvas.SetPixels(Tools.StrokePixels(2, 2, 1)));
        history.EndStroke(canvas);
        history.Undo(canvas);

        Assert.Multiple(() =>
        {
            Assert.That(canvas[0, 0], Is.Null);
            Assert.That(canvas[1, 1], Is.Null);
            Assert.That(canvas[2, 2], Is.Null);
        });
    }

    [Test]
    public void New_action_after_undo_clears_redo()
    {
        var canvas = new Canvas(8, 8);
        var history = new UndoStack();

        history.Push(Tools.Stamp(canvas, 1, 1, 1, 1, false));
        history.Undo(canvas);
        history.Push(Tools.Stamp(canvas, 5, 5, 2, 1, false));

        Assert.That(history.CanRedo, Is.False);
    }

    [Test]
    public void Undo_with_empty_history_is_noop()
    {
        var canvas = new Canvas(4, 4);
        var history = new UndoStack();
        Assert.DoesNotThrow(() => history.Undo(canvas));
        Assert.That(history.CanUndo, Is.False);
    }

    [Test]
    public void Replay_rebuilds_canvas_for_time_lapse()
    {
        var canvas = new Canvas(8, 8);
        var history = new UndoStack();
        history.Push(Tools.Stamp(canvas, 1, 1, 1, 1, false));
        history.Push(Tools.Stamp(canvas, 6, 6, 2, 1, false));

        var replay = new Canvas(8, 8);
        history.ReplayOnto(replay, upToStep: 1); // only first action

        Assert.Multiple(() =>
        {
            Assert.That(replay[1, 1], Is.EqualTo(1));
            Assert.That(replay[6, 6], Is.Null, "second step not replayed");
        });
    }


    [Test]
    public void Replay_across_a_resize_keeps_pixels_on_the_resized_canvas()
    {
        // Regression for #40: ApplyForward ignored size, so a time-lapse spanning a resize
        // replayed onto the wrong geometry.
        var canvas = new Canvas(4, 4);
        var history = new UndoStack();
        history.Push(Tools.Stamp(canvas, 0, 0, 1, 1, false));   // draw at 4x4
        history.Push(canvas.Resize(8, 8));                       // grow
        history.Push(Tools.Stamp(canvas, 7, 7, 2, 1, false));   // draw at 8x8

        var replay = new Canvas(4, 4);
        history.ReplayOnto(replay, upToStep: 3);

        Assert.Multiple(() =>
        {
            Assert.That(replay.Width, Is.EqualTo(8), "resize step must be replayed");
            Assert.That(replay.Height, Is.EqualTo(8), "resize step must be replayed");
            Assert.That(replay[0, 0], Is.EqualTo(1), "pre-resize pixel survives");
            Assert.That(replay[7, 7], Is.EqualTo(2), "post-resize pixel lands");
        });
    }
}
