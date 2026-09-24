using Vixel.Core;

namespace Vixel.Tests;

/// <summary>Regression tests for user-reported bugs from the first hands-on session (2026-09-23).</summary>
[TestFixture]
public class BugRegressionTests
{
    private static CanvasView NewView() =>
        new(new EditorSession(null), Terminal.Gui.Drivers.ColorCapabilityLevel.TrueColor);

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

    // Bug: CellColors put the bottom-half color in bg for ▄ cells, but ▄ draws its
    // bottom half from fg — bottom-half pixels rendered black.
    // Contract: for any occupancy, the drawn half's color is in fg.
    [Test]
    public void CellColors_bottom_only_puts_bottom_color_in_fg()
    {
        var (fg, bg) = NewView().CellColorsForTest(null, new Rgb(10, 200, 30));
        Assert.Multiple(() =>
        {
            Assert.That(fg.R, Is.EqualTo(10));
            Assert.That(fg.G, Is.EqualTo(200));
            Assert.That(bg.R, Is.EqualTo(0), "empty half must be black");
        });
    }

    [Test]
    public void CellColors_top_only_puts_top_color_in_fg_and_black_bg()
    {
        var (fg, bg) = NewView().CellColorsForTest(new Rgb(200, 10, 30), null);
        Assert.Multiple(() =>
        {
            Assert.That(fg.R, Is.EqualTo(200));
            Assert.That(bg.R, Is.EqualTo(0), "empty half must be black");
        });
    }

    [Test]
    public void CellColors_both_halves_carry_their_own_colors()
    {
        var (fg, bg) = NewView().CellColorsForTest(new Rgb(255, 0, 0), new Rgb(0, 0, 255));
        Assert.Multiple(() =>
        {
            Assert.That(fg.R, Is.EqualTo(255));
            Assert.That(bg.B, Is.EqualTo(255));
        });
    }

    // Contract: both halves set → ▀ (top fg, bottom bg).
    [Test]
    public void GlyphFor_both_set_is_upper_half_block()
    {
        Assert.That(CanvasView.GlyphForTest(new Rgb(1, 1, 1), new Rgb(2, 2, 2)), Is.EqualTo('▀'));
    }

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

    // Design decision (2026-09-23): empty pixels render a 2×2-pixel checkerboard
    // (2 cells wide × 1 cell row per check), never raw terminal background.
    [Test]
    public void Background_checker_alternates_per_2x2_pixels()
    {
        Assert.Multiple(() =>
        {
            // Same check: (0,0),(1,0),(0,1),(1,1) share a shade
            Assert.That(CanvasView.BackgroundShadeForTest(0, 0), Is.EqualTo(CanvasView.BackgroundShadeForTest(1, 1)));
            // Neighbor checks differ: x+2 flips, y+2 flips, both flip back
            Assert.That(CanvasView.BackgroundShadeForTest(2, 0), Is.Not.EqualTo(CanvasView.BackgroundShadeForTest(0, 0)));
            Assert.That(CanvasView.BackgroundShadeForTest(0, 2), Is.Not.EqualTo(CanvasView.BackgroundShadeForTest(0, 0)));
            Assert.That(CanvasView.BackgroundShadeForTest(2, 2), Is.EqualTo(CanvasView.BackgroundShadeForTest(0, 0)));
        });
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
