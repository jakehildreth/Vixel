using Vixel.Core;

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
}
