using Vixel.Core;

namespace Vixel.Tests;

/// <summary>Regression tests for user-reported bugs from the first hands-on session (2026-09-23).</summary>
[TestFixture]
public class BugRegressionTests
{
    // Bug: a 9× square stamp rendered as alternating bars — even pixel rows invisible.
    // Pin the data contract: the footprint writes every row; SetPixels never drops an
    // in-bounds write.
    [Test]
    public void Stamp_9x9_square_writes_all_81_pixels_including_every_row()
    {
        var canvas = new Canvas(32, 32);
        var change = Tools.Stamp(canvas, 16, 16, 1, brushSize: 9, circle: false);

        Assert.Multiple(() =>
        {
            Assert.That(change.Touched, Has.Count.EqualTo(81));
            for (var y = 12; y <= 20; y++)
                for (var x = 12; x <= 20; x++)
                    Assert.That(canvas[x, y], Is.EqualTo(1), $"pixel ({x},{y}) missing");
        });
    }

    // Cell-mapping contract (drawn-half color rides fg, checkerboard parity) is pinned
    // in CellMappingTests against CellMapping directly; CanvasView now delegates to it (#33).




    // Brush design: square steps ±1 and allows even sizes; an even N×N block focuses 1 up and
    // 1 left of center. Circle steps ±1 but skips 2 (a size-2 circle has no inscribed center).
    [Test]
    public void Brush_steps_by_1_and_allows_even_sizes()
    {
        var s = new EditorSession(null);
        Assert.That(s.BrushSize, Is.EqualTo(1));
        s.AdjustBrush(+1);
        Assert.That(s.BrushSize, Is.EqualTo(2), "even square sizes are allowed");
        s.AdjustBrush(-1);
        Assert.That(s.BrushSize, Is.EqualTo(1));
    }

    [Test]
    public void Brush_clamps_to_1_through_9()
    {
        var s = new EditorSession(null);
        for (var i = 0; i < 20; i++) s.AdjustBrush(+1);
        Assert.That(s.BrushSize, Is.EqualTo(9), "clamps at 9");
        for (var i = 0; i < 20; i++) s.AdjustBrush(-1);
        Assert.That(s.BrushSize, Is.EqualTo(1), "clamps at 1");
    }

    [Test]
    public void Even_square_footprint_is_exactly_N_by_N_with_focus_up_left()
    {
        // Focus 1 up and 1 left of center: for size 4 the geometric center sits between
        // cells 19/20 and 21/22, so focus is (20,20) and the block is x∈[19,22], y∈[19,22].
        var cells = Tools.Footprint(20, 20, size: 4, circle: false).ToHashSet();
        Assert.That(cells, Has.Count.EqualTo(16), "size 4 is exactly 4×4, not 5×5");
        for (var x = 19; x <= 22; x++)
            for (var y = 19; y <= 22; y++)
                Assert.That(cells, Does.Contain((x, y)), $"({x},{y}) should be painted");
        Assert.That(cells, Does.Not.Contain((18, 20)), "focus leans up-left, not symmetric");
    }

    [Test]
    public void Size_2_square_covers_focus_plus_three_right_and_down()
    {
        var cells = Tools.Footprint(10, 10, size: 2, circle: false).ToHashSet();
        Assert.That(cells, Has.Count.EqualTo(4), "size 2 is 2×2");
        Assert.That(cells, Is.EquivalentTo(new[] { (10, 10), (11, 10), (10, 11), (11, 11) }),
            "focus is the up-left cell of the 2×2 block");
    }

    [Test]
    public void Circle_brush_skips_only_size_2()
    {
        var s = new EditorSession(null) { CircleBrush = true };
        s.AdjustBrush(+1);
        Assert.That(s.BrushSize, Is.EqualTo(3), "circle skips 2");
        s.AdjustBrush(+1);
        Assert.That(s.BrushSize, Is.EqualTo(4), "circle allows even sizes from 4 up");
    }

    [Test]
    public void Circle_brush_descending_skips_2_down_to_1()
    {
        var s = new EditorSession(null) { CircleBrush = true };
        s.AdjustBrush(+1); // 3
        s.AdjustBrush(+1); // 4
        s.AdjustBrush(-1); // 3
        s.AdjustBrush(-1); // skip 2 → 1
        Assert.That(s.BrushSize, Is.EqualTo(1), "descending past 2 lands on 1");
    }
}
