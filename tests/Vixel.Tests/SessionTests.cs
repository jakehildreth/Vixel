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
    public void Command_help_enters_help_mode()
    {
        var s = NewSession();
        s.ExecuteCommand("help", out _);
        Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Help));
    }

    [Test]
    public void Help_scroll_clamps_at_zero_and_exits_clean()
    {
        var s = NewSession();
        s.EnterHelp();
        s.ScrollHelp(5);
        Assert.That(s.HelpScroll, Is.EqualTo(5));
        s.ScrollHelp(-10);
        Assert.That(s.HelpScroll, Is.EqualTo(0), "clamped at top");
        s.ExitHelp();
        Assert.That(s.CurrentMode, Is.EqualTo(EditorSession.Mode.Normal));
    }

    [Test]
    public void HelpView_max_scroll_matches_line_count()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HelpView.MaxScroll(44), Is.EqualTo(1), "45 lines, 44-row viewport");
            Assert.That(HelpView.MaxScroll(20), Is.EqualTo(25));
            Assert.That(HelpView.MaxScroll(100), Is.EqualTo(0), "viewport taller than content: no scroll");
        });
    }

    [Test]
    public void Command_star_sets_message_without_throwing()
    {
        var s = NewSession();
        s.ExecuteCommand("star", out _);
        // gh-starred (CI/dev machines with authed gh) or browser fallback — both carry the repo slug.
        Assert.That(s.Message, Does.Contain("jakehildreth/Vixel").Or.Contain("jakehildreth/vixel"));
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
    public void Undo_on_empty_history_leaves_Dirty_false()
    {
        // Regression for #27: Undo/Redo set Dirty = true unconditionally.
        var s = NewSession();
        Assert.That(s.Dirty, Is.False);
        s.Undo();
        Assert.That(s.Dirty, Is.False, "no-op undo must not dirty the session");
    }

    [Test]
    public void Wq_with_failed_save_does_not_quit()
    {
        // Regression for #52: :wq quit unconditionally even when :w threw.
        var s = NewSession();
        s.Stamp(); // dirty
        s.ExecuteCommand("wq /nonexistent-dir-cannot-exist/file.vixel", out var quit);
        Assert.That(quit, Is.False, "save failed — must not quit and lose the work");
        Assert.That(s.Message, Does.Contain("error").Or.Contain("not"), "error surfaced");
    }

    [Test]
    public void Wq_with_successful_save_quits()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.vixel");
        try
        {
            var s = NewSession();
            s.Stamp();
            s.ExecuteCommand($"wq {temp}", out var quit);
            Assert.Multiple(() =>
            {
                Assert.That(quit, Is.True, "save landed — quit");
                Assert.That(File.Exists(temp), Is.True);
            });
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void New_with_zero_dimensions_is_rejected()
    {
        // Regression for #40: :new 0x0 used to throw or create a degenerate canvas.
        var s = NewSession();
        s.ExecuteCommand("new! 0x0", out _);
        Assert.That(s.Canvas.Width, Is.GreaterThanOrEqualTo(1), "falls back to default, no crash");
    }

    [Test]
    public void New_with_garbage_dimensions_falls_back_to_default()
    {
        var s = NewSession();
        s.ExecuteCommand("new! abc", out _);
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas.Width, Is.EqualTo(64), "garbage → default 64");
            Assert.That(s.Canvas.Height, Is.EqualTo(32), "garbage → default 32");
        });
    }

    [Test]
    public void Resize_with_garbage_dimensions_shows_usage_and_does_not_change_canvas()
    {
        var s = NewSession();
        var before = s.Canvas.Width;
        s.ExecuteCommand("resize abc", out _);
        Assert.Multiple(() =>
        {
            Assert.That(s.Message, Does.Contain("usage"), "malformed → usage message");
            Assert.That(s.Canvas.Width, Is.EqualTo(before), "canvas unchanged");
        });
    }

    [Test]
    public void Resize_with_valid_dimensions_applies_and_is_undoable()
    {
        var s = NewSession();
        s.ExecuteCommand("resize 16x8", out _);
        Assert.Multiple(() =>
        {
            Assert.That(s.Canvas.Width, Is.EqualTo(16));
            Assert.That(s.Canvas.Height, Is.EqualTo(8));
        });
        s.Undo();
        Assert.That(s.Canvas.Width, Is.EqualTo(64), "resize undo restores prior size");
    }

    [Test]
    public void Redo_on_empty_history_leaves_Dirty_false()
    {
        var s = NewSession();
        s.Redo();
        Assert.That(s.Dirty, Is.False, "no-op redo must not dirty the session");
    }

    [Test]
    public void Quit_after_undo_back_to_saved_state_quits_without_nag()
    {
        // Regression for #27: undo back to exactly the saved content must not nag.
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.vixel");
        try
        {
            var s = NewSession();
            s.ExecuteCommand($"w {temp}", out _); // save empty canvas
            s.Stamp();                            // draw → dirty
            s.Undo();                             // back to saved content, flag still set
            Assert.That(s.Dirty, Is.True, "flag-based check would nag");

            s.ExecuteCommand("q", out var quit);
            Assert.That(quit, Is.True, "content matches saved state: quit without nag");
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void Quit_with_real_unsaved_changes_still_nags()
    {
        var temp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}.vixel");
        try
        {
            var s = NewSession();
            s.ExecuteCommand($"w {temp}", out _);
            s.Stamp(); // real change vs saved state

            s.ExecuteCommand("q", out var quit);
            Assert.Multiple(() =>
            {
                Assert.That(quit, Is.False, "content differs: :q must refuse");
                Assert.That(s.Message, Does.Contain("unsaved"));
            });
        }
        finally { File.Delete(temp); }
    }

    [Test]
    public void Quit_after_undo_all_the_way_on_never_saved_session_does_not_nag()
    {
        // Reported live: blank start, draw, undo everything — :q must not nag.
        var s = NewSession(); // never saved
        s.Stamp();
        s.Stamp();
        s.Undo();
        s.Undo();
        s.ExecuteCommand("q", out var quit);
        Assert.That(quit, Is.True, "content back to initial blank state: quit clean");
    }

    [Test]
    public void Edit_command_with_unsaved_changes_warns_and_does_not_open()
    {
        // Reported live: :e discarded unsaved work without warning.
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var pathB = System.IO.Path.Combine(dir, "b.vixel");
            File.WriteAllText(pathB, VixelFile.Save(new Canvas(4, 4), new Palette(), "b"));

            var s = NewSession();
            s.Stamp(); // unsaved change
            s.ExecuteCommand($"e {pathB}", out _);
            Assert.Multiple(() =>
            {
                Assert.That(s.Message, Does.Contain("unsaved"), "warns before discarding");
                Assert.That(s.Canvas.Width, Is.EqualTo(64), "still on the original canvas");
            });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void Edit_force_discards_unsaved_changes_and_opens()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            var pathB = System.IO.Path.Combine(dir, "b.vixel");
            File.WriteAllText(pathB, VixelFile.Save(new Canvas(4, 4), new Palette(), "b"));

            var s = NewSession();
            s.Stamp();
            s.ExecuteCommand($"e! {pathB}", out _);
            Assert.That(s.Canvas.Width, Is.EqualTo(4), "e! forced the open");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void New_with_unsaved_changes_warns_and_keeps_canvas()
    {
        var s = NewSession();
        s.Stamp();
        s.ExecuteCommand("new 8x8", out _);
        Assert.Multiple(() =>
        {
            Assert.That(s.Message, Does.Contain("unsaved"), "warns before discarding");
            Assert.That(s.Canvas.Width, Is.EqualTo(64), "canvas not replaced");
        });
    }

    [Test]
    public void New_force_discards_unsaved_changes()
    {
        var s = NewSession();
        s.Stamp();
        s.ExecuteCommand("new! 8x8", out _);
        Assert.That(s.Canvas.Width, Is.EqualTo(8), "new! forced the replace");
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
            var (canvas, loadedPalette, name, _) = EditorSession.OpenFile(temp);
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
            var (canvas, _, _, _) = EditorSession.OpenFile(temp);
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
            var (canvas, _, _, _) = EditorSession.OpenFile(temp);
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
            var (loaded, _, name, _) = EditorSession.OpenFile(temp);
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
            var (canvas, _, _, _) = EditorSession.OpenFile(temp);
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
            var (canvas, palette, _, _) = EditorSession.OpenFile(fixture);
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

    [Test]
    public void Edit_command_after_drawing_does_not_let_undo_corrupt_the_new_canvas()
    {
        // Regression for #25: :e and :new kept the old UndoStack, so the next undo
        // applies the previous file's PixelChange to the fresh canvas (resize + pixel stomp).
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        try
        {
            // B is a different size from A's canvas and has content where A's stroke landed.
            var pathB = System.IO.Path.Combine(dir, "b.vixel");
            var b = new EditorSession(path: null);
            b.ExecuteCommand("new 16x8", out _);
            b.Canvas.SetPixel(0, 0, 7); // content exactly where A will draw
            File.WriteAllText(pathB, VixelFile.Save(b.Canvas, b.Palette, "b"));

            var a = new EditorSession(path: null); // 64x32
            a.CursorX = 0; a.CursorY = 0;
            a.Stamp(); // A's history: change recorded against a 64x32 canvas, touched (0,0)

            Assert.That(a.ExecuteCommand($"e! {pathB}", out _), Is.True); // a has unsaved changes; force the open
            Assert.Multiple(() =>
            {
                Assert.That(a.Canvas.Width, Is.EqualTo(16), "B loaded");
                Assert.That(a.Canvas[0, 0], Is.EqualTo(7), "B's content present");
            });

            a.Undo(); // must be a no-op on B, not A's change replayed onto B
            Assert.Multiple(() =>
            {
                Assert.That(a.Canvas.Width, Is.EqualTo(16), "undo must not resize B to A's geometry");
                Assert.That(a.Canvas.Height, Is.EqualTo(8), "undo must not resize B to A's geometry");
                Assert.That(a.Canvas[0, 0], Is.EqualTo(7), "undo must not stomp B's pixels with A's old value");
            });
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public void New_command_after_drawing_does_not_let_undo_corrupt_the_fresh_canvas()
    {
        var a = new EditorSession(path: null); // 64x32
        a.CursorX = 0; a.CursorY = 0;
        a.Stamp(); // A's history: touched (0,0) on a 64x32 canvas

        Assert.That(a.ExecuteCommand("new! 8x8", out _), Is.True); // a has unsaved changes; force the replace
        Assert.That(a.Canvas[0, 0], Is.Null, "fresh canvas is empty");

        a.Undo(); // must be a no-op, not A's stroke replayed + resized onto the new canvas
        Assert.Multiple(() =>
        {
            Assert.That(a.Canvas.Width, Is.EqualTo(8), "undo must not resize the new canvas");
            Assert.That(a.Canvas.Height, Is.EqualTo(8), "undo must not resize the new canvas");
            Assert.That(a.Canvas[0, 0], Is.Null, "undo must not stomp the new canvas");
        });
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

[TestFixture]
public class CommandHistoryAndCompletionTests
{
    private static EditorSession NewSession() => new(path: null);

    // --- history ----------------------------------------------------------

    [Test]
    public void Submitted_commands_are_recorded_and_recalled_with_up()
    {
        var s = NewSession();
        s.ExecuteCommand("w", out _);
        s.ExecuteCommand("clear", out _);

        Assert.That(s.HistoryRecall(-1), Is.EqualTo("clear"), "up recalls most recent");
        Assert.That(s.HistoryRecall(-1), Is.EqualTo("w"), "up again recalls the one before");
    }

    [Test]
    public void Consecutive_duplicate_commands_are_deduped()
    {
        var s = NewSession();
        s.ExecuteCommand("w", out _);
        s.ExecuteCommand("w", out _);
        s.ExecuteCommand("w", out _);

        Assert.That(s.HistoryRecall(-1), Is.EqualTo("w"));
        Assert.That(s.HistoryRecall(-1), Is.Null, "only one w was kept");
    }

    [Test]
    public void Down_past_most_recent_returns_to_empty_buffer()
    {
        var s = NewSession();
        s.ExecuteCommand("w", out _);

        s.HistoryRecall(-1);
        Assert.That(s.HistoryRecall(+1), Is.EqualTo(""), "down past newest restores the empty in-progress line");
    }

    [Test]
    public void Up_on_empty_history_returns_null()
    {
        var s = NewSession();
        Assert.That(s.HistoryRecall(-1), Is.Null);
    }

    // --- completion -------------------------------------------------------

    [Test]
    public void Tab_completes_a_unique_command_name()
    {
        var s = NewSession();
        var (completed, matches) = s.CompleteCommand("cle");
        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.EqualTo("clear "), "unique command completes with a trailing space, ready for args");
            Assert.That(matches, Is.Empty, "unique match completes without listing");
        });
    }

    [Test]
    public void Tab_completes_to_longest_common_prefix_and_lists_matches()
    {
        var s = NewSession();
        var (completed, matches) = s.CompleteCommand("e");
        Assert.Multiple(() =>
        {
            Assert.That(completed, Is.EqualTo("e"), "e vs export share only 'e'");
            Assert.That(matches, Is.EquivalentTo(new[] { "e", "e!", "export" }));
        });
    }

    [Test]
    public void Tab_completes_file_paths_for_e()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"vixel-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(dir);
        File.WriteAllText(System.IO.Path.Combine(dir, "art.vixel"), "{}");
        try
        {
            var s = NewSession();
            var (completed, _) = s.CompleteCommand($"e {dir}/ar");
            Assert.That(completed, Does.EndWith("art.vixel"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
