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
}
