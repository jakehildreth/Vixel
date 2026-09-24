using Vixel.Core;
using Vixel.Core.Formats;

namespace Vixel.Tests;

// EditorSession is Terminal.Gui-free by design: these tests cover the modal
// interaction contract from the keymap ticket (#5) without a TUI lifecycle.

[TestFixture]
public class SessionTests
{
    private static EditorSession NewSession() => new(path: null);

    [Test]
    public void New_session_has_default_canvas_and_palette()
    {
        var s = NewSession();
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas.Width, Is.EqualTo(64));
            Assert.That(s.Canvas.Height, Is.EqualTo(32));
            Assert.That(s.Palette.Colors, Has.Count.EqualTo(16), "PICO-8 default");
            Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Normal));
        });
    }

    [Test]
    public void Stamp_never_moves_the_brush()
    {
        var s = NewSession();
        s.CursorX = 10;
        s.CursorY = 7;
        s.Stamp();
        Assert.Multiple(() =>
        {
            Assert.That(s.CursorX, Is.EqualTo(10));
            Assert.That(s.CursorY, Is.EqualTo(7));
            Assert.That(s.Canvas[10, 7], Is.EqualTo(0));
        });
    }

    [Test]
    public void EraseUnderBrush_clears_pixel_and_is_undoable()
    {
        var s = NewSession();
        s.CursorX = 5;
        s.CursorY = 5;
        s.Stamp();
        Assert.That(s.Canvas[5, 5], Is.EqualTo(0));

        s.EraseUnderBrush();
        Assert.That(s.Canvas[5, 5], Is.Null);

        s.Undo();
        Assert.That(s.Canvas[5, 5], Is.EqualTo(0), "undo restores the erased pixel");
    }

    [Test]
    public void DeleteRegion_clears_selection_and_returns_to_normal()
    {
        var s = NewSession();
        for (var x = 0; x < 4; x++)
        {
            s.CursorX = x;
            s.CursorY = 2;
            s.Stamp();
        }

        s.CursorX = 1;
        s.CursorY = 2;
        s.BeginTwoPoint(EditorSession.Mode.Select);
        s.CursorX = 2;
        s.CursorY = 2;
        s.DeleteRegion();

        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[0, 2], Is.EqualTo(0), "outside region untouched");
            Assert.That(s.Canvas[1, 2], Is.Null);
            Assert.That(s.Canvas[2, 2], Is.Null);
            Assert.That(s.Canvas[3, 2], Is.EqualTo(0), "outside region untouched");
            Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Normal));
        });

        s.Undo();
        Assert.That(s.Canvas[1, 2], Is.EqualTo(0), "undo restores region");
    }

    [Test]
    public void New_session_names_untitled_not_null()
    {
        // Regression: fresh sessions saved with "name": null (found in smoke test).
        var s = NewSession();
        Assert.That(s.Name, Is.EqualTo("untitled"));
    }

    [Test]
    public void Paint_mode_lays_pixels_while_moving_and_is_one_undo_step()
    {
        var s = NewSession();
        s.EnterPaint();
        s.Move(1, 0);
        s.Move(1, 0);
        s.Move(0, 1);
        s.ExitPaint();

        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[1, 0], Is.Not.Null);
            Assert.That(s.Canvas[2, 1], Is.Not.Null);
            Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Normal));
        });

        s.Undo();
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[1, 0], Is.Null, "whole stroke reverted in one step");
            Assert.That(s.Canvas[2, 1], Is.Null);
        });
    }

    [Test]
    public void Normal_mode_movement_never_paints()
    {
        var s = NewSession();
        s.Move(2, 0);
        s.Move(0, 2);
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[1, 0], Is.Null);
            Assert.That(s.Canvas[2, 2], Is.Null);
        });
    }

    [Test]
    public void Two_point_line_commits_and_cancels()
    {
        var s = NewSession();
        s.BeginTwoPoint(EditorSession.Mode.Line);
        s.Move(4, 0);
        s.CommitTwoPoint();
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[2, 0], Is.EqualTo(0));
            Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Normal));
        });

        s.BeginTwoPoint(EditorSession.Mode.Line);
        s.Move(0, 9);
        s.CancelMode();
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[0, 5], Is.Null, "cancelled line leaves no pixels");
            Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Normal));
        });
    }

    [Test]
    public void Rect_shift_enter_fills()
    {
        var s = NewSession();
        s.BeginTwoPoint(EditorSession.Mode.Rect);
        s.Move(3, 3);
        s.CommitTwoPoint(shifted: true);
        Assert.That(s.Canvas[1, 1], Is.EqualTo(0), "interior filled on Shift+Enter");
    }

    [Test]
    public void Select_yanks_and_paste_stamps()
    {
        var s = NewSession();
        s.Stamp(); // pixel at 0,0
        s.BeginTwoPoint(EditorSession.Mode.Select);
        s.Move(1, 0); // cursor 0,0 → 1,0: selects the 2x1 region
        s.CommitTwoPoint(); // yank

        s.Move(4, 5); // cursor 1,0 → 5,5
        s.BeginTwoPoint(EditorSession.Mode.Paste);
        s.CommitTwoPoint(); // pastes with top-left at 5,5

        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas[5, 5], Is.EqualTo(0), "pasted at cursor");
            Assert.That(s.Canvas[0, 0], Is.EqualTo(0), "source preserved");
        });
    }

    [Test]
    public void Eyedropper_picks_color_under_cursor()
    {
        var s = NewSession();
        s.SetPaletteForTest(5);
        s.Stamp();
        s.CurrentColorIndex = 0;
        s.Eyedrop();
        Assert.That(s.CurrentColorIndex, Is.EqualTo(5));
    }

    [Test]
    public void Eraser_writes_transparent()
    {
        var s = NewSession();
        s.Stamp();
        s.Erasing = true;
        s.Stamp();
        Assert.That(s.Canvas[0, 0], Is.Null);
    }

    [Test]
    public void Command_w_marks_clean_and_q_refuses_when_dirty()
    {
        var s = NewSession();
        s.Stamp();
        Assert.That(s.Dirty, Is.True);

        s.ExecuteCommand("q", out var quit);
        Assert.Multiple(() =>
        {
            Assert.That(quit, Is.False, ":q refuses with unsaved changes");
            Assert.That(s.Message, Does.Contain("unsaved"));
        });

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.vixel");
        s.ExecuteCommand($"w {temp}", out _);
        Assert.That(s.Dirty, Is.False);

        s.ExecuteCommand("q", out quit);
        Assert.That(quit, Is.True);
        File.Delete(temp);
    }

    [Test]
    public void Command_color_adds_custom_color()
    {
        var s = NewSession();
        s.ExecuteCommand("color #123abc", out _);
        Assert.Multiple(() =>
        {
            Assert.That(s.Palette.Colors, Has.Count.EqualTo(17));
            Assert.That(s.CurrentColorIndex, Is.EqualTo(16));
        });
    }

    [Test]
    public void OpenFile_dispatches_aseprite_by_extension()
    {
        var source = new Canvas(2, 2);
        var palette = new Palette();
        var index = palette.AddColor(new Rgb(200, 30, 40));
        source.SetPixel(1, 1, index);

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.aseprite");
        File.WriteAllBytes(temp, AseFormat.Export(source, palette));
        try
        {
            var (canvas, loadedPalette, name) = EditorSession.OpenFile(temp);
            Assert.Multiple(() =>
            {
                Assert.That(canvas[1, 1], Is.Not.Null);
                Assert.That(name, Is.EqualTo(System.IO.Path.GetFileNameWithoutExtension(temp)));
                Assert.That(loadedPalette.Colors, Is.Not.Empty);
            });
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void OpenFile_dispatches_pixquare_by_extension()
    {
        var source = new Canvas(3, 1);
        var palette = new Palette();
        var index = palette.AddColor(new Rgb(1, 2, 3));
        source.SetPixel(2, 0, index);

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.px");
        File.WriteAllBytes(temp, PxFormat.Export(source, palette));
        try
        {
            var (canvas, _, _) = EditorSession.OpenFile(temp);
            Assert.That(canvas[2, 0], Is.Not.Null);
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void OpenFile_dispatches_piskel_by_extension()
    {
        var source = new Canvas(1, 1);
        var palette = new Palette();
        var index = palette.AddColor(new Rgb(9, 8, 7));
        source.SetPixel(0, 0, index);

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.piskel");
        File.WriteAllText(temp, PiskelFormat.Export(source, palette));
        try
        {
            var (canvas, _, _) = EditorSession.OpenFile(temp);
            Assert.That(canvas[0, 0], Is.Not.Null);
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void OpenFile_loads_vixel_as_default()
    {
        var canvas = new Canvas(2, 1);
        var palette = new Palette();
        canvas.SetPixel(0, 0, palette.AddColor(new Rgb(255, 0, 0)));

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.vixel");
        File.WriteAllText(temp, VixelFile.Save(canvas, palette, "roundtrip"));
        try
        {
            var (loaded, _, name) = EditorSession.OpenFile(temp);
            Assert.Multiple(() =>
            {
                Assert.That(loaded[0, 0], Is.Not.Null);
                Assert.That(name, Is.EqualTo("roundtrip"));
            });
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void OpenFile_extension_match_is_case_insensitive()
    {
        var source = new Canvas(1, 1);
        var palette = new Palette();
        source.SetPixel(0, 0, palette.AddColor(new Rgb(10, 20, 30)));

        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.PX");
        File.WriteAllBytes(temp, PxFormat.Export(source, palette));
        try
        {
            var (canvas, _, _) = EditorSession.OpenFile(temp);
            Assert.That(canvas[0, 0], Is.Not.Null);
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void OpenFile_imports_real_pixquare_file()
    {
        // Real Pixquare export (captured from the app): exercises mixed size semantics
        // (Entry/Frame/Layer sizes are content-only; Layer includes its tail) and the
        // corrupt adler32 trailer Pixquare appends after each zlib stream.
        var fixture = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-fixture-{Guid.NewGuid()}.px");
        File.WriteAllBytes(fixture, Convert.FromBase64String(RealPx));
        try
        {
            var (canvas, palette, _) = EditorSession.OpenFile(fixture);
            var painted = 0;
            for (var y = 0; y < canvas.Height; y++)
                for (var x = 0; x < canvas.Width; x++)
                    if (canvas[x, y] is not null) painted++;
            Assert.Multiple(() =>
            {
                Assert.That(canvas.Width, Is.EqualTo(77));
                Assert.That(canvas.Height, Is.EqualTo(26));
                Assert.That(painted, Is.EqualTo(2002));
                Assert.That(palette.Colors, Has.Count.GreaterThan(0));
            });
        }
        finally { File.Delete(fixture); }
    }

    private const string RealPx =
        "hAgAAAAAAAAkAgAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAADc0RENEQTk5LTFG" +
        "MUYtNDY3MC1CMkU3LUFENTNDRUZDNTNBRE0AAAAaAAAAAQAAAAAAAAAlAAAAAAAAAAAAAAAAAAAAJEM2MEFGNERBLTQyRkEtNDk3" +
        "QS1CMjJCLUVCRjAzMDUzRkE3RAAAAAAAAAAAAQAAAAAAAADRAAAAJAcJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAEM2MEFGNERB" +
        "LTQyRkEtNDk3QS1CMjJCLUVCRjAzMDUzRkE3RExheWVyIDEBAAAAAAAAAFkAAAAkJAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "MjM1QTE3QUItNTIyNC00NzExLTgwRTEtQTc5MTdGMTNCMTkyZAAAAAE5NzVGMkQzQS1DMTdFLTQyMDMtOUM2QS0zMkQ0NDY0OUY0" +
        "QzkAQAAAAAAAAAAAAAAAPAEAAQAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAABuBQAAAAAAACRIHwAASgUA" +
        "AAEAAAAAAAAAAAAAAAAAADk3NUYyRDNBLUMxN0UtNDIwMy05QzZBLTMyRDQ0NjQ5RjRDOXic7Zn3VxRJEMf5Q1SEQySKeAp6koSF" +
        "XSSYUJEg2QiCmDAgCgIiBiSq5915Ub189w9+b2tue6e7pnqC+qPzXr0nbU1N7+fVd7qqJiMjAxmfLbLVbTqG2OY21G85joYtJxHP" +
        "bEci8xQat3bg4NZONGV1oSX7NFqze3Doi96k9eFwTj+ObBvAsdwhtOWewfHtZ3Fi+zmczDuP9vwLOJU/jI6CEXQWXEJX4Si6i8Zw" +
        "uugyeouvoG/HVfTvuIb+kusYKLmBwZ0TOFN6C2dLb+Pcrjs4v2sSF768i4u7pzC8+z5G9kzjUtkMRsseYKx8FpfL53Bl70Nc3beA" +
        "a189Stoiru9/jImKp7hZ8Qy3Kpdwu/I57lQtY7JqBXer1zBVvY57NRu4f+AFpmtfYqbuFR7UvcZs7BvMxb7FfP13eFj/BgsN32Mx" +
        "/iMex3/Ck8TPeJr4Bc8a32Lp4Ds8b/rVYFa/mXidQJwxI17NWd0Gs8M5fTiSM4Cj2wY9zNrziNdFdDBmxKuneNxgNpBkNlgygaGd" +
        "NwVmU0lm9zC8x2WmeI3vnTeYEa8b+5+YzKqI2Qomq1f/Z1bjMpupJV5fY5YxI16PGn4wmTUSs3dJZu8NZrFNbQ4zKQe9zNwck/yJ" +
        "mbROzKR1yjHFTM8xnRnPMYfZPmLm5pjOTM8xnRnxkvYwH3NzTGem55iHWUqX9G/90plJupT8SZfSumLG1z+VLomZrMtVQ5fSHnRd" +
        "EjObLpebfjOYkS6leJIuiRnpUvInXUrrPUXj4npYXRIzWZeLFl0ui7q0MQujS52ZepcFM3NzLDKzYpmZblyX/P/JXF0uODkm+dhs" +
        "+sBLcQ9hdRmFGfFqzTZ1eTTXnxk3xUw3fq+uS8WM+xAzXZfcx3Y5zGqDmcm6fO/wWmn+Pb134kU1hhRPqjHUeSn58/OSagzSJT8v" +
        "7cxcXUo+XJefhFnDG4su3xo5xpnFLcx0I15BuULMuA9ZmlmqJgt6li0+MfPzsV3kRzWGFFN6l0m6XG3+Q2Tmt1/FjK/rRjkm+RCv" +
        "Pq2ODfu7uR/lWNAebBbEzOEVt+vSZMZq/6zOSMyo9qf3mKr9bcz02l8/L4OYUY0xXu6el1J8sfavMWt/qmOlez26TMi61Jnptb/q" +
        "laTYVPtL67z292OmeqUozHiNIcUPY2GZES9eYxCzteY/DWZOjmV2+DOz1bEaM3r3y8zM/nKo1K39wzIjXlT7+/nbrjDMJF0SM5Vj" +
        "ay0aM6Enj8SM9eSSj19PHsRM1yWdlx/O7LW4N7+eXNeljZnqlaTYyvg678ltzBxeqZ5cjxfETLePYTYbhplQ+ytdrrf8ld5HIslM" +
        "16Vi5sdJN12XXQWjMjNdlylmfjFtz5XqWNu93PyYcV0qZnqOmcxMXfKeXHqO36wsiBnpUvLhPXkUZraenM/KpHvD6nK91cvMNiuz" +
        "MbPNyiR/Vfvrxn14Ty75SLU/j2szG7MwRjm20fp3+m+uS96TS89xcsw4L0ccXXYXjonPDFP781mZFIfmPnwtzEV+cylmH3Kvh5mP" +
        "Ln2Z5aeY6f1l4Vh6JsvrWCnOR83KKpYi/W4+w45yL+lyo/Ufk1my9rfNymzMrDNsoSen81Jm5jMrKw+elUX53WlmqZ48yr2UYy80" +
        "ZrIuewNn2JIuPXN/7d0vxTGYlU1bZtgL4gxbMQtrfIYd5K+bHzPbrMzzbSkvWJdS7S/NsImZpEs+K9OZkS79ZtjOeam+lQjflsL2" +
        "5Oq8dJgd+tdlJuiSz8o4M70nD5qV8Z7cZObVJe/JrTPsSvsMW/Xknu9xEWZlipnKMZ3ZZ4tm/wEzAAAAAAAAAKWlpf8rKyv/PDw8" +
        "/+bm5v80NDT/4uLi/x8fH/8mJib/2NjY/6mpqf+JiYn/ZWVl/8jIyP/e3t7/IyMj/62trf+UlJT/Xl5e/2FhYf/o6Oj/+Pj4/zMz" +
        "M/9NTU3/v7+//9LS0v+AgID/JSUl/8bGxv8kJCT/Hh4e/5CQkP+ZmZn/8PDw/ykpKf9dXV3/V1dX/yIiIv+enp7/7e3t/5qamv90" +
        "dHT//////9bW1v8/Pz//bW1t/1ZWVv/f39//pKSk/3Fxcf+Ojo7/s7Oz/wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAABAAAAAAAAAAAAAAAAAAAACFEgAAiAAAAAwAAAAAAAAAABAAAAAQAAAAzMzM/+bm5v8A" +
        "AAAADgAAAAAAAAAAABAAAAAQAAAAAAD//wAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAQAAAAAAAAAAAAEAAAABAAAA";
}
