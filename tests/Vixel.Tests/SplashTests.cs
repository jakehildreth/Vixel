using Vixel.Core;

namespace Vixel.Tests;

/// <summary>Splash screen contract: shows every launch, 5s auto-dismiss, any key dismisses forever.</summary>
[TestFixture]
public class SplashTests
{
    private string _dir = null!;

    [SetUp]
    public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "vixel-test-" + Guid.NewGuid().ToString("N"));

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Test]
    public void Fresh_install_shows_splash()
    {
        var config = new VixelConfig(_dir);
        Assert.That(config.SplashDismissed, Is.False);
    }

    [Test]
    public void Dismissing_splash_persists_across_instances()
    {
        new VixelConfig(_dir).DismissSplashForever();
        var next = new VixelConfig(_dir);
        Assert.That(next.SplashDismissed, Is.True);
    }

    [Test]
    public void Missing_or_corrupt_config_is_a_fresh_install()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "config.json"), "not json {{{");
        var config = new VixelConfig(_dir);
        Assert.That(config.SplashDismissed, Is.False);
    }

    [Test]
    public void Splash_override_is_absent_by_default()
    {
        var config = new VixelConfig(_dir);
        Assert.That(config.LoadSplashOverride(), Is.Null);
    }

    [Test]
    public void Splash_override_reads_user_file()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllLines(Path.Combine(_dir, "splash.txt"), ["my custom", "splash {version}"]);
        var config = new VixelConfig(_dir);
        Assert.That(config.LoadSplashOverride(), Is.EqualTo(new[] { "my custom", "splash {version}" }));
    }

    [Test]
    public void Empty_splash_override_is_respected()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "splash.txt"), "");
        var config = new VixelConfig(_dir);
        Assert.That(config.LoadSplashOverride(), Is.EqualTo(new string[0]), "empty file = no text, intentional user choice");
    }

    [Test]
    public void Layout_centers_composite_vertically()
    {
        // 13 art rows + 1 blank + 11 text lines = 25; at height 40 → 7.5 → 7
        var (blockTop, _, textTop) = SplashView.Layout(Enumerable.Repeat("x", 11).ToArray(), viewportWidth: 90, viewportHeight: 40);
        Assert.Multiple(() =>
        {
            Assert.That(blockTop, Is.EqualTo(7), "composite centered, not art alone");
            Assert.That(textTop, Is.EqualTo(7 + 13 + 1), "text follows art + blank row");
        });
    }

    [Test]
    public void Layout_centers_text_block_by_longest_line()
    {
        var lines = new[] { "short", "", new string('x', 65) };
        var (_, textLeft, _) = SplashView.Layout(lines, viewportWidth: 90, viewportHeight: 40);
        Assert.That(textLeft, Is.EqualTo((90 - 65) / 2), "block width = longest line");
    }

    [Test]
    public void Layout_clamps_to_viewport()
    {
        var (blockTop, textLeft, _) = SplashView.Layout(Enumerable.Repeat(new string('x', 50), 50).ToArray(), viewportWidth: 10, viewportHeight: 20);
        Assert.Multiple(() =>
        {
            Assert.That(blockTop, Is.Zero);
            Assert.That(textLeft, Is.Zero);
        });
    }
}
