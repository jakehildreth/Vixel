using Vixel.Core;

namespace Vixel.Tests;

/// <summary>Cell-mapping contract: two canvas pixels → one half-block terminal cell.</summary>
[TestFixture]
public class CellMappingTests
{
    private static readonly Rgb Red = new(255, 0, 0);
    private static readonly Rgb Blue = new(0, 0, 255);

    // ▀ draws its top half from fg; ▄ draws its bottom half from fg as well.
    // The drawn half's color always rides fg; the other half rides bg.
    [Test]
    public void Both_halves_set_put_top_in_fg_bottom_in_bg()
    {
        var cell = CellMapping.Map(Red, Blue);
        Assert.Multiple(() =>
        {
            Assert.That(cell.Glyph, Is.EqualTo('▀'));
            Assert.That(cell.Fg, Is.EqualTo(Red));
            Assert.That(cell.Bg, Is.EqualTo(Blue));
        });
    }

    [Test]
    public void Top_only_draws_top_with_fg()
    {
        var cell = CellMapping.Map(Red, null);
        Assert.Multiple(() =>
        {
            Assert.That(cell.Glyph, Is.EqualTo('▀'));
            Assert.That(cell.Fg, Is.EqualTo(Red));
        });
    }

    [Test]
    public void Bottom_only_draws_bottom_with_fg_not_bg()
    {
        // Regression: bottom-half color in bg renders nothing — ▄ draws from fg.
        var cell = CellMapping.Map(null, Blue);
        Assert.Multiple(() =>
        {
            Assert.That(cell.Glyph, Is.EqualTo('▄'));
            Assert.That(cell.Fg, Is.EqualTo(Blue));
        });
    }

    // Empty halves resolve to the 2×2-pixel checkerboard (2 cells wide × 1 cell row
    // per check), never the raw terminal background.
    [Test]
    public void Empty_half_resolves_to_checkerboard_shade()
    {
        var cell = CellMapping.Map(null, null, 0, 0);
        Assert.That(cell.Bg, Is.EqualTo(CellMapping.BackgroundShade(0, 0)));
    }

    // Bug: brush on a bottom-half pixel rendered the empty top half with the wrong
    // checker shade (light where the check says dark). The empty half's shade must be
    // computed at the empty pixel's own coordinates.
    [Test]
    public void Brush_on_bottom_half_gives_top_half_its_own_checker_shade()
    {
        // Bottom-half brush at pixel (x=1, y=7); the top half is pixel (1,6), empty.
        // Both pixels sit in the same 2×2 check (x/2=0, y/2=3) → identical shades.
        var cell = CellMapping.Map(null, new Rgb(64, 128, 255), x: 1, topY: 6);
        Assert.That(cell.Bg, Is.EqualTo(CellMapping.BackgroundShade(1, 6)),
            "the empty top half must take the checker shade at its own coordinates");
        Assert.That(CellMapping.BackgroundShade(1, 6), Is.EqualTo(CellMapping.BackgroundShade(1, 7)),
            "both halves of a cell-row share a check row — shades must match");
    }

    [Test]
    public void Background_checker_alternates_per_2x2_pixels()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CellMapping.BackgroundShade(1, 1), Is.EqualTo(CellMapping.BackgroundShade(0, 0)));
            Assert.That(CellMapping.BackgroundShade(2, 0), Is.Not.EqualTo(CellMapping.BackgroundShade(0, 0)));
            Assert.That(CellMapping.BackgroundShade(0, 2), Is.Not.EqualTo(CellMapping.BackgroundShade(0, 0)));
            Assert.That(CellMapping.BackgroundShade(2, 2), Is.EqualTo(CellMapping.BackgroundShade(0, 0)));
        });
    }
}
