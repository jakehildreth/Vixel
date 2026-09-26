using Vixel.Core;

namespace Vixel.Tests;

[TestFixture]
public class PaletteTests
{
    [Test]
    public void New_palette_is_empty()
    {
        var palette = new Palette();
        Assert.That(palette.Colors, Is.Empty);
    }

    [Test]
    public void AddColor_appends_and_returns_index()
    {
        var palette = new Palette();
        var red = palette.AddColor(new Rgb(255, 0, 0));
        var blue = palette.AddColor(new Rgb(0, 0, 255));
        Assert.Multiple(() =>
        {
            Assert.That(red, Is.EqualTo(0));
            Assert.That(blue, Is.EqualTo(1));
            Assert.That(palette.Colors, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void AddColor_deduplicates_existing_color()
    {
        var palette = new Palette();
        var first = palette.AddColor(new Rgb(255, 0, 0));
        var again = palette.AddColor(new Rgb(255, 0, 0));
        Assert.Multiple(() =>
        {
            Assert.That(again, Is.EqualTo(first));
            Assert.That(palette.Colors, Has.Count.EqualTo(1));
        });
    }
}

[TestFixture]
public class CanvasTests
{
    [Test]
    public void New_canvas_is_fully_transparent()
    {
        var canvas = new Canvas(8, 4);
        Assert.Multiple(() =>
        {
            Assert.That(canvas.Width, Is.EqualTo(8));
            Assert.That(canvas.Height, Is.EqualTo(4));
            Assert.That(canvas[0, 0], Is.Null);
            Assert.That(canvas[7, 3], Is.Null);
        });
    }

    [Test]
    public void Set_and_get_pixel_round_trips()
    {
        var canvas = new Canvas(8, 4);
        canvas.SetPixel(3, 2, 5);
        Assert.That(canvas[3, 2], Is.EqualTo(5));
    }

    [Test]
    public void SetPixel_out_of_bounds_throws()
    {
        var canvas = new Canvas(8, 4);
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetPixel(8, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetPixel(0, -1, 1));
    }

    [Test]
    public void SetPixel_out_of_bounds_names_the_offending_parameter()
    {
        // Regression for #31: y out of range used to throw naming x.
        var canvas = new Canvas(8, 4);
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetPixel(0, 4, 1))!.ParamName, Is.EqualTo("y"));
        Assert.That(Assert.Throws<ArgumentOutOfRangeException>(() => canvas.SetPixel(8, 0, 1))!.ParamName, Is.EqualTo("x"));
    }

    [Test]
    public void Indexer_out_of_bounds_get_returns_null_not_throw()
    {
        // Reads off-canvas are legal (fill bounds checks, viewport clamping);
        // writes are not.
        var canvas = new Canvas(8, 4);
        Assert.That(canvas[100, 100], Is.Null);
    }

    [Test]
    public void Resize_grow_extends_with_transparent_and_preserves_art()
    {
        var canvas = new Canvas(4, 4);
        canvas.SetPixel(3, 3, 7);
        var change = canvas.Resize(6, 6);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Width, Is.EqualTo(6));
            Assert.That(canvas[3, 3], Is.EqualTo(7));
            Assert.That(canvas[5, 5], Is.Null);
        });
    }

    [Test]
    public void Resize_shrink_crops_and_records_lost_pixels()
    {
        var canvas = new Canvas(4, 4);
        canvas.SetPixel(3, 3, 7); // will be cropped by 2x2
        canvas.SetPixel(0, 0, 2); // survives
        var change = canvas.Resize(2, 2);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Width, Is.EqualTo(2));
            Assert.That(canvas[0, 0], Is.EqualTo(2));
            // The cropped pixel must be in the change record so undo can restore it.
            Assert.That(change.Touched.Any(t => t.X == 3 && t.Y == 3 && t.Old == 7), Is.True);
        });
    }

    [Test]
    public void Undo_of_resize_restores_cropped_pixels_and_size()
    {
        var canvas = new Canvas(4, 4);
        canvas.SetPixel(3, 3, 7);
        var change = canvas.Resize(2, 2);
        canvas.ApplyInverse(change);

        Assert.Multiple(() =>
        {
            Assert.That(canvas.Width, Is.EqualTo(4));
            Assert.That(canvas[3, 3], Is.EqualTo(7));
        });
    }

    [Test]
    public void Resize_grow_records_no_null_to_null_noop_touches()
    {
        // Regression for #30: grow used to record a no-op (null→null) touch for every exposed cell.
        var canvas = new Canvas(2, 2);
        canvas.SetPixel(1, 1, 5);
        var change = canvas.Resize(512, 512);

        Assert.That(change.Touched, Is.Empty, "grow onto transparent cells has nothing meaningful to undo pixel-wise");
    }
}

[TestFixture]
public class BresenhamTests
{
    [Test]
    public void Horizontal_line_touches_every_column()
    {
        var points = Bresenham.Line(0, 0, 5, 0).ToArray();
        Assert.That(points.Select(p => p.X), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));
        Assert.That(points.Select(p => p.Y).Distinct().Single(), Is.EqualTo(0));
    }

    [Test]
    public void Diagonal_line_is_45_degrees()
    {
        var points = Bresenham.Line(0, 0, 4, 4).ToArray();
        Assert.That(points, Has.Length.EqualTo(5));
        Assert.That(points.Select(p => p.X - p.Y).Distinct().Single(), Is.EqualTo(0));
    }

    [Test]
    public void Steep_line_includes_endpoints()
    {
        var points = Bresenham.Line(2, 8, 3, 1).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(points.First(), Is.EqualTo((2, 8)));
            Assert.That(points.Last(), Is.EqualTo((3, 1)));
        });
    }

    [Test]
    public void Zero_length_line_is_single_point()
    {
        Assert.That(Bresenham.Line(3, 3, 3, 3).ToArray(), Is.EqualTo(new[] { (3, 3) }));
    }
}

[TestFixture]
public class FillTests
{
    [Test]
    public void Fill_floods_contiguous_same_color_region()
    {
        var canvas = new Canvas(4, 4); // all transparent = one region
        var change = FloodFill.Fill(canvas, 1, 1, 3);
        Assert.Multiple(() =>
        {
            Assert.That(change.Touched, Has.Count.EqualTo(16));
            Assert.That(canvas[0, 0], Is.EqualTo(3));
            Assert.That(canvas[3, 3], Is.EqualTo(3));
        });
    }

    [Test]
    public void Fill_stops_at_different_color_boundary()
    {
        var canvas = new Canvas(4, 1);
        canvas.SetPixel(2, 0, 9); // wall
        var change = FloodFill.Fill(canvas, 0, 0, 3);

        Assert.Multiple(() =>
        {
            Assert.That(change.Touched, Has.Count.EqualTo(2), "only x=0 and x=1 flood");
            Assert.That(canvas[2, 0], Is.EqualTo(9), "wall untouched");
            Assert.That(canvas[3, 0], Is.Null, "beyond wall untouched");
        });
    }

    [Test]
    public void Fill_with_same_color_is_noop()
    {
        var canvas = new Canvas(2, 2);
        canvas.SetPixel(0, 0, 5); // rest transparent
        var change = FloodFill.Fill(canvas, 0, 0, 5);
        Assert.That(change.Touched, Is.Empty);
    }
}

[TestFixture]
public class QuantizerTests
{
    [Test]
    public void Exact_cube_color_maps_to_itself()
    {
        // xterm cube level 215 at r=g=b=4: 16 + 36*4 + 6*4 + 4 = 188
        Assert.That(Xterm256.ToIndex(new Rgb(215, 215, 215)), Is.EqualTo(188));
    }

    [Test]
    public void Near_gray_uses_grayscale_ramp()
    {
        // 128 is closest to gray ramp step v=8+10*12=128 → index 232+12=244
        var index = Xterm256.ToIndex(new Rgb(128, 128, 128));
        Assert.That(index, Is.EqualTo(244));
    }

    [Test]
    public void Palette_table_has_256_entries()
    {
        Assert.That(Xterm256.Palette, Has.Length.EqualTo(256));
    }

    [Test]
    public void Round_trip_is_stable()
    {
        // quantize(palette[i]) must return i for cube/ramp entries (16..255)
        for (var i = 16; i < 256; i++)
        {
            Assert.That(Xterm256.ToIndex(Xterm256.Palette[i]), Is.EqualTo(i), $"palette[{i}]");
        }
    }

    [Test]
    public void Boundary_grays_quantize_to_nearest_ramp_step()
    {
        // Regression for #40: grays must land on the nearest ramp step. Ramp steps are
        // 8,18,28,...,238; 127 and 128 both sit inside [123,133) so both map to step 128 (index 244).
        Assert.Multiple(() =>
        {
            Assert.That(Xterm256.ToIndex(new Rgb(127, 127, 127)), Is.EqualTo(244), "127 rounds to ramp step 128");
            Assert.That(Xterm256.ToIndex(new Rgb(128, 128, 128)), Is.EqualTo(244), "128 is ramp step 128");
            Assert.That(Xterm256.ToIndex(new Rgb(0, 0, 0)), Is.EqualTo(16), "pure black is exact cube entry 16");
            Assert.That(Xterm256.ToIndex(new Rgb(255, 255, 255)), Is.EqualTo(231), "pure white is exact cube entry 231");
        });
    }
}

[TestFixture]
public class AtomicWriteTests
{
    [Test]
    public void WriteAllText_leaves_target_and_no_temp_file()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var target = System.IO.Path.Combine(dir, "a.txt");
            AtomicWrite.WriteAllText(target, "hello");
            Assert.Multiple(() =>
            {
                Assert.That(File.ReadAllText(target), Is.EqualTo("hello"));
                Assert.That(File.Exists(target + ".tmp"), Is.False, "temp file moved away");
            });

            AtomicWrite.WriteAllText(target, "overwritten");
            Assert.That(File.ReadAllText(target), Is.EqualTo("overwritten"), "overwrite replaces in place");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
