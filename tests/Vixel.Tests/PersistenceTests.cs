using Vixel.Core;
using Vixel.Core.Formats;

namespace Vixel.Tests;

[TestFixture]
public class VixelFileTests
{
    private static (Canvas canvas, Palette palette) SampleArt()
    {
        var palette = new Palette();
        var red = palette.AddColor(new Rgb(255, 0, 0));
        var blue = palette.AddColor(new Rgb(0, 0, 255));
        var canvas = new Canvas(3, 2);
        canvas.SetPixel(0, 0, red);
        canvas.SetPixel(2, 1, blue);
        return (canvas, palette);
    }

    [Test]
    public void Save_load_round_trips_canvas_and_palette()
    {
        var (canvas, palette) = SampleArt();
        var json = VixelFile.Save(canvas, palette, "test");
        var (loadedCanvas, loadedPalette, name, _) = VixelFile.Load(json);

        Assert.Multiple(() =>
        {
            Assert.That(name, Is.EqualTo("test"));
            Assert.That(loadedCanvas.Width, Is.EqualTo(3));
            Assert.That(loadedCanvas.Height, Is.EqualTo(2));
            Assert.That(loadedPalette.Colors, Is.EqualTo(palette.Colors));
            Assert.That(loadedCanvas[0, 0], Is.EqualTo(0));
            Assert.That(loadedCanvas[2, 1], Is.EqualTo(1));
            Assert.That(loadedCanvas[1, 1], Is.Null);
        });
    }

    [Test]
    public void Saved_json_has_version_field()
    {
        var (canvas, palette) = SampleArt();
        var json = VixelFile.Save(canvas, palette, "x");
        Assert.That(json, Does.Contain("\"version\": 1").Or.Contain("\"version\":1"));
    }

    [Test]
    public void Load_ignores_unknown_fields()
    {
        var (canvas, palette) = SampleArt();
        var json = VixelFile.Save(canvas, palette, "x");
        json = json.Replace("\"version\": 1", "\"version\": 1, \"futureField\": {\"anything\": true}");
        if (json == VixelFile.Save(canvas, palette, "x"))
            json = json.Replace("\"version\":1", "\"version\":1,\"futureField\":true");
        Assert.DoesNotThrow(() => VixelFile.Load(json));
    }

    [Test]
    public void Load_rejects_newer_version()
    {
        var json = """{"version": 99, "width": 1, "height": 1, "palette": [], "rows": [[null]]}""";
        Assert.Throws<InvalidDataException>(() => VixelFile.Load(json));
    }

    [Test]
    public void Save_preserves_created_timestamp_across_resaves()
    {
        // Regression for #32: meta.created was rewritten on every save.
        var (canvas, palette) = SampleArt();
        var first = VixelFile.Save(canvas, palette, "x", created: new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
        var second = VixelFile.Save(canvas, palette, "x", created: new DateTimeOffset(2020, 1, 2, 3, 4, 5, TimeSpan.Zero));
        Assert.Multiple(() =>
        {
            Assert.That(first, Does.Contain("2020-01-02"), "created preserved");
            Assert.That(second, Does.Contain("2020-01-02"), "created preserved on resave");
        });
    }

    [Test]
    public void Load_rejects_palette_index_out_of_range()
    {
        // Regression for #28: a hand-edited .vixel with a palette index >= palette count
        // used to load, then crash at render time indexing Palette.Colors[i].
        const string json = """
            { "version": 1, "name": "bad", "width": 2, "height": 1,
              "palette": ["#000000"],
              "rows": [[0, 5]] }
            """;
        var e = Assert.Throws<InvalidDataException>(() => VixelFile.Load(json));
        Assert.That(e!.Message, Does.Contain("palette"));
    }

    [Test]
    public void Load_rejects_row_that_is_not_an_array()
    {
        // Regression for #56: a scalar/object row must throw InvalidDataException, not InvalidCastException.
        const string json = """
            { "version": 1, "name": "bad", "width": 2, "height": 1,
              "palette": ["#000000"],
              "rows": [42] }
            """;
        Assert.Throws<InvalidDataException>(() => VixelFile.Load(json));
    }

    [Test]
    public void Load_rejects_dimension_mismatch()
    {
        var json = """{"version": 1, "width": 3, "height": 1, "palette": [], "rows": [[null]]}""";
        Assert.Throws<InvalidDataException>(() => VixelFile.Load(json));
    }
}

[TestFixture]
public class PngTests
{
    [Test]
    public void Export_one_to_one_produces_canvas_sized_png()
    {
        var palette = new Palette();
        var red = palette.AddColor(new Rgb(255, 0, 0));
        var canvas = new Canvas(4, 3);
        canvas.SetPixel(1, 1, red);

        var bytes = PngFormat.Export(canvas, palette, scale: 1);
        var (w, h) = PngFormat.ReadDimensions(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(w, Is.EqualTo(4));
            Assert.That(h, Is.EqualTo(3));
        });
    }

    [Test]
    public void Export_scale_multiplies_dimensions()
    {
        var canvas = new Canvas(4, 3);
        var bytes = PngFormat.Export(canvas, new Palette(), scale: 4);
        var (w, h) = PngFormat.ReadDimensions(bytes);
        Assert.Multiple(() =>
        {
            Assert.That(w, Is.EqualTo(16));
            Assert.That(h, Is.EqualTo(12));
        });
    }

    [Test]
    public void Export_writes_pixel_colors_and_transparency()
    {
        var palette = new Palette();
        var red = palette.AddColor(new Rgb(255, 0, 0));
        var canvas = new Canvas(2, 1);
        canvas.SetPixel(0, 0, red); // (1,0) transparent

        var bytes = PngFormat.Export(canvas, palette, scale: 1);
        var pixelRed = PngFormat.ReadPixel(bytes, 0, 0);
        var pixelClear = PngFormat.ReadPixel(bytes, 1, 0);

        Assert.Multiple(() =>
        {
            Assert.That(pixelRed, Is.EqualTo((255, 0, 0, 255)));
            Assert.That(pixelClear.Item4, Is.EqualTo(0), "transparent pixel exports alpha 0");
        });
    }

    [Test]
    public void Export_rejects_nonpositive_scale()
    {
        var canvas = new Canvas(1, 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => PngFormat.Export(canvas, new Palette(), 0));
    }
}

[TestFixture]
public class PiskelTests
{
    [Test]
    public void Round_trip_preserves_pixels()
    {
        var palette = new Palette();
        var red = palette.AddColor(new Rgb(255, 0, 0));
        var canvas = new Canvas(2, 2);
        canvas.SetPixel(1, 1, red);

        var json = PiskelFormat.Export(canvas, palette);
        var (loaded, _) = PiskelFormat.Import(json);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Width, Is.EqualTo(2));
            Assert.That(loaded[1, 1], Is.Not.Null);
            Assert.That(loaded[0, 0], Is.Null);
        });
    }

    [Test]
    public void Import_flattens_first_frame_only()
    {
        // A two-frame piskel file imports its first frame, ignoring the rest.
        var json = PiskelFormat.Export(new Canvas(1, 1), new Palette(), frameCount: 3);
        var (loaded, _) = PiskelFormat.Import(json);
        Assert.That(loaded.Width, Is.EqualTo(1)); // parsed without trying to model frames
    }

    [Test]
    public void Import_rejects_invalid_json()
    {
        Assert.Throws<InvalidDataException>(() => PiskelFormat.Import("not json"));
    }

    [Test]
    public void Import_rejects_missing_piskel_key()
    {
        Assert.Throws<InvalidDataException>(() => PiskelFormat.Import("""{"other":{}}"""));
    }

    [Test]
    public void Import_rejects_missing_dimensions()
    {
        const string json = """{"piskel":{"frames":[]}}""";
        Assert.Throws<InvalidDataException>(() => PiskelFormat.Import(json));
    }

    [Test]
    public void Import_rejects_frame_without_dataUri()
    {
        const string json = """{"piskel":{"width":1,"height":1,"frames":[{}]}}""";
        Assert.Throws<InvalidDataException>(() => PiskelFormat.Import(json));
    }

    [Test]
    public void Import_rejects_bad_base64()
    {
        const string json = """{"piskel":{"width":1,"height":1,"frames":[{"dataUri":"data:image/png;base64,!!!"}]}}""";
        Assert.Throws<InvalidDataException>(() => PiskelFormat.Import(json));
    }
}

[TestFixture]
public class PxTests
{
    [Test]
    public void Round_trip_preserves_rgba_pixels()
    {
        var palette = new Palette();
        var green = palette.AddColor(new Rgb(0, 255, 0));
        var canvas = new Canvas(3, 2);
        canvas.SetPixel(2, 0, green);

        var bytes = PxFormat.Export(canvas, palette);
        var (loaded, loadedPalette) = PxFormat.Import(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Width, Is.EqualTo(3));
            Assert.That(loaded.Height, Is.EqualTo(2));
            Assert.That(loaded[2, 0], Is.Not.Null);
            Assert.That(loadedPalette.Colors[loaded[2, 0]!.Value], Is.EqualTo(new Rgb(0, 255, 0)));
            Assert.That(loaded[0, 0], Is.Null);
        });
    }
    [Test]
    public void Import_rejects_truncated_at_every_length()
    {
        // Regression for #29: a truncated .px must throw InvalidDataException, not
        // IndexOutOfRangeException. Sweep every prefix of a valid file.
        var palette = new Palette();
        var green = palette.AddColor(new Rgb(0, 255, 0));
        var canvas = new Canvas(3, 2);
        canvas.SetPixel(2, 0, green);
        var bytes = PxFormat.Export(canvas, palette);

        for (var len = 0; len < bytes.Length; len++)
        {
            var truncated = bytes[..len];
            try
            {
                PxFormat.Import(truncated);
            }
            catch (InvalidDataException)
            {
                // expected
            }
            catch (Exception e)
            {
                Assert.Fail($"length {len}: expected InvalidDataException, got {e.GetType().Name}: {e.Message}");
            }
        }
    }

}

[TestFixture]
public class AseTests
{
    [Test]
    public void Round_trip_preserves_single_layer_art()
    {
        var palette = new Palette();
        var blue = palette.AddColor(new Rgb(0, 0, 255));
        var canvas = new Canvas(4, 3);
        canvas.SetPixel(3, 2, blue);

        var bytes = AseFormat.Export(canvas, palette);
        var (loaded, loadedPalette) = AseFormat.Import(bytes);

        Assert.Multiple(() =>
        {
            Assert.That(loaded.Width, Is.EqualTo(4));
            Assert.That(loaded.Height, Is.EqualTo(3));
            Assert.That(loaded[3, 2], Is.Not.Null);
            Assert.That(loadedPalette.Colors[loaded[3, 2]!.Value], Is.EqualTo(new Rgb(0, 0, 255)));
        });
    }

    [Test]
    public void Import_rejects_truncated_file()
    {
        Assert.Throws<InvalidDataException>(() => AseFormat.Import([0xA0, 0xE0]));
    }

    [Test]
    public void Import_rejects_truncation_at_every_length()
    {
        // Regression for #56: a truncated .ase must throw InvalidDataException, not
        // EndOfStreamException. Sweep every prefix of a valid file.
        var palette = new Palette();
        var red = palette.AddColor(new Rgb(255, 0, 0));
        var canvas = new Canvas(2, 2);
        canvas.SetPixel(1, 1, red);
        var bytes = AseFormat.Export(canvas, palette);

        for (var len = 0; len < bytes.Length; len++)
        {
            var truncated = bytes[..len];
            try
            {
                AseFormat.Import(truncated);
            }
            catch (InvalidDataException)
            {
                // expected
            }
            catch (Exception e)
            {
                Assert.Fail($"length {len}: expected InvalidDataException, got {e.GetType().Name}: {e.Message}");
            }
        }
    }
}
