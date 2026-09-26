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

    // Bug: EnterPaint began the stroke but laid no pixel until the first move;
    // Aseprite-style stroke start stamps immediately.
    [Test]
    public void EnterPaint_stamps_the_current_pixel_immediately()
    {
        var session = new EditorSession(null);
        session.SetPaletteForTest(0);
        session.EnterPaint();
        Assert.That(session.Canvas[session.CursorX, session.CursorY], Is.Not.Null);
    }

    // Bug: [ and ] were never bound; brush size could not change.
    // Contract: footprint follows the odd-step brush ladder 1/3/5/7/9.
    [Test]
    public void Footprint_follows_odd_brush_steps_1_3_5_7_9()
    {
        var expected = new[] { 1, 9, 25, 49, 81 };
        for (var step = 0; step < 5; step++)
        {
            var size = 1 + step * 2;
            Assert.That(Tools.Footprint(20, 20, size, circle: false).Count(),
                Is.EqualTo(expected[step]), $"brush {size} should be {size}×{size}");
        }
    }
}
