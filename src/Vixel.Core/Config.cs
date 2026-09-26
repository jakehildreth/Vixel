using System.Text.Json;

namespace Vixel.Core;

/// <summary>
/// User configuration at ~/.config/vixel/config.json (%USERPROFILE%\.config\vixel on Windows —
/// same relative shape, no APPDATA split). Missing or corrupt config reads as defaults.
/// </summary>
public sealed class VixelConfig
{
    private readonly string _path;
    private readonly string _splashPath;

    private sealed class State
    {
        public bool SplashDismissed { get; set; }
    }

    private State _state = new();

    /// <summary>Config rooted at the given directory (tests pass a temp dir).</summary>
    public VixelConfig(string directory)
    {
        _path = Path.Combine(directory, "config.json");
        _splashPath = Path.Combine(directory, "splash.txt");
        Load();
    }

    /// <summary>Config at the default per-user location.</summary>
    public static VixelConfig Default() => new(DefaultDirectory());

    public static string DefaultDirectory() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "vixel");


    /// <summary>
    /// User-edited splash lines from splash.txt, or null when absent/unreadable
    /// (caller falls back to the embedded default). {version} substitution is the caller's job.
    /// </summary>
    public IReadOnlyList<string>? LoadSplashOverride()
    {
        try
        {
            if (File.Exists(_splashPath))
                return File.ReadAllLines(_splashPath);
        }
        catch (Exception)
        {
            // treat unreadable as absent
        }
        return null;
    }

    public bool SplashDismissed => _state.SplashDismissed;

    public void DismissSplashForever()
    {
        _state.SplashDismissed = true;
        Save();
    }

    private void Load()
    {
        try
        {
            if (File.Exists(_path))
            {
                _state = JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new State();
            }
        }
        catch (Exception)
        {
            // Corrupt or unreadable config behaves as a fresh install.
            _state = new State();
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        AtomicWrite.WriteAllText(_path, JsonSerializer.Serialize(_state));
    }
}
