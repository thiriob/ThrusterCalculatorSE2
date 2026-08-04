using System;
using System.IO;
using System.Reflection;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Gui.Services;

/// <summary>Where a loaded config came from, so the UI can say so.</summary>
public enum ConfigOrigin
{
    /// <summary>A real config produced by <c>tc extract</c>.</summary>
    Extracted,

    /// <summary>The bundled sample. Invented numbers — the UI must say so prominently.</summary>
    Sample,
}

/// <summary>A loaded config and where it came from.</summary>
public sealed record LoadedConfig(GameData Data, ConfigOrigin Origin, string Description);

/// <summary>
/// Finds and loads <c>gamedata.json</c>.
/// </summary>
/// <remarks>
/// The repository ships no real config (Technic §3.4), so on a clean clone the only thing available
/// is the bundled sample. That is deliberate: the app must be runnable and demonstrable without a
/// Space Engineers install, and must be unmistakable about which it is showing.
/// <para>
/// Reads through streams rather than paths, keeping the door open for a hosted build to fetch the
/// same bytes over HTTP (Technic §9).
/// </para>
/// </remarks>
public static class ConfigSource
{
    public const string FileName = "gamedata.json";

    /// <summary>Loads the best available config, falling back to the bundled sample.</summary>
    public static LoadedConfig Load()
    {
        foreach (var candidate in CandidatePaths())
        {
            if (!File.Exists(candidate)) continue;

            try
            {
                using var stream = File.OpenRead(candidate);
                return new LoadedConfig(
                    GameDataSerializer.Read(stream), ConfigOrigin.Extracted, candidate);
            }
            catch (Exception ex) when (ex is GameDataFormatException or IOException)
            {
                // A broken config must not prevent the app starting; fall through to the sample and
                // let the UI report it.
                return LoadSample($"{Path.GetFileName(candidate)} could not be read: {ex.Message}");
            }
        }

        return LoadSample("no gamedata.json found");
    }

    /// <summary>Loads a config from an explicit file, for the "open…" affordance.</summary>
    public static LoadedConfig LoadFrom(string path)
    {
        using var stream = File.OpenRead(path);
        return new LoadedConfig(GameDataSerializer.Read(stream), ConfigOrigin.Extracted, path);
    }

    private static LoadedConfig LoadSample(string reason)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var sample = Path.Combine(AppContext.BaseDirectory, "Assets", "sample-gamedata.json");

        using var stream = File.OpenRead(sample);
        return new LoadedConfig(GameDataSerializer.Read(stream), ConfigOrigin.Sample, reason);
    }

    private static string[] CandidatePaths() =>
    [
        // Beside the executable — how a packaged release ships one.
        Path.Combine(AppContext.BaseDirectory, FileName),

        // Where `tc extract` writes by default for a user who ran it themselves.
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ThrusterCalculatorSE2", FileName),
    ];
}
