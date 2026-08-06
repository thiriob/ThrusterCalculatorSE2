using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace ThrusterCalculator.Gui.Services;

/// <summary>
/// User settings, stored as a plain INI file.
/// </summary>
/// <remarks>
/// Same shape as the sibling <c>BlueprintHelperSE2</c>, deliberately: written on clean shutdown
/// only, so a crash leaves the previous known-good file intact rather than persisting whatever
/// state caused it. The format is plain text with comments, so a setting that makes the app
/// misbehave can be corrected — or deleted — by hand.
/// <para>
/// Missing or unparsable keys fall back to their defaults and the file is rewritten immediately,
/// so it always ends up complete and self-documenting rather than growing silently as features
/// are added. A value that is present but nonsensical is treated as missing: settings are a
/// convenience, and refusing to start over one would be the wrong trade.
/// </para>
/// </remarks>
public sealed class AppSettings
{
    private const string FileName = "settings.ini";

    /// <summary>Id of the last departure planet, matched against the config's planet ids.</summary>
    public string? SelectedPlanetId { get; set; }

    /// <summary>Whether the user's own gravity overrides whatever the planet states.</summary>
    public bool UseCustomGravity { get; set; }

    /// <summary>
    /// The user's own surface gravity in m/s², stored only as an override.
    /// </summary>
    /// <remarks>
    /// Deliberately never a copy of a gravity read from the config. A stored copy would go stale
    /// the moment the config is rebuilt and would then quietly win over the newer number — the
    /// app would be serving a value from a previous extraction while showing no sign of it. What
    /// is persisted is the user's decision to override, plus what they chose; the planet's own
    /// value is looked up fresh every time.
    /// <para>
    /// It matters more than it sounds: surface gravity is the one number the producer genuinely
    /// cannot read from the game (Research §5.3), so on a real config it is the user's for every
    /// planet, and retyping it each launch would be the app's most obvious papercut.
    /// </para>
    /// </remarks>
    public double CustomGravity { get; set; } = DefaultCustomGravity;

    /// <summary>Whether the user has chosen to override the planet's stated radius.</summary>
    public bool UseCustomRadius { get; set; }

    /// <summary>The override value in kilometres, kept whether or not it is in force.</summary>
    public double CustomRadiusKm { get; set; } = DefaultCustomRadiusKm;

    /// <summary>Every planet in the game is 60 km or 20 km, so this is the commoner of the two.</summary>
    public const double DefaultCustomRadiusKm = 60.0;

    public double TargetThrustToWeight { get; set; } = DefaultTargetThrustToWeight;

    public const double DefaultCustomGravity = 9.81;

    public const double DefaultTargetThrustToWeight = 1.0;

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ThrusterCalculatorSE2",
        FileName);

    /// <summary>
    /// Reads the settings file, filling in missing keys and writing it back when incomplete.
    /// </summary>
    public static AppSettings Load() => Load(FilePath);

    /// <summary>Reads from an explicit path. Used by tests, which must not touch the real file.</summary>
    public static AppSettings Load(string path)
    {
        var settings = new AppSettings();
        var complete = File.Exists(path);

        if (complete)
        {
            try
            {
                var values = Parse(File.ReadAllLines(path));

                // Non-short-circuiting & on purpose: every key must be attempted, or one missing
                // early key would hide the rest and the rewrite would not fill them in.
                complete =
                    ReadString(values, "SelectedPlanetId", v => settings.SelectedPlanetId = v)
                    & ReadBool(values, "UseCustomGravity", v => settings.UseCustomGravity = v)
                    & ReadDouble(values, "CustomGravity", IsPlausibleGravity,
                        v => settings.CustomGravity = v)
                    & ReadBool(values, "UseCustomRadius", v => settings.UseCustomRadius = v)
                    & ReadDouble(values, "CustomRadiusKm", IsPlausibleRadius,
                        v => settings.CustomRadiusKm = v)
                    & ReadDouble(values, "TargetThrustToWeight", IsPlausibleRatio,
                        v => settings.TargetThrustToWeight = v);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                complete = false;
            }
        }

        if (!complete) settings.Save(path);

        return settings;
    }

    /// <summary>Writes every setting, with comments, replacing whatever was there.</summary>
    public void Save() => Save(FilePath);

    /// <inheritdoc cref="Save()"/>
    public void Save(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var text = new StringBuilder()
                .AppendLine("# ThrusterCalculator SE2 settings.")
                .AppendLine("# Written when the application closes cleanly. Safe to edit by hand;")
                .AppendLine("# delete a line to restore its default, or delete the file for all.")
                .AppendLine()
                .AppendLine("# Departure planet, by id as it appears in gamedata.json.")
                .AppendLine($"SelectedPlanetId = {SelectedPlanetId}")
                .AppendLine()
                .AppendLine("# Your own surface gravity, in m/s², and whether it is in force.")
                .AppendLine("# Only the override is stored — a planet's own gravity is read from")
                .AppendLine("# gamedata.json each time, so rebuilding the config is never masked")
                .AppendLine("# by a stale copy here. Earth-like is 9.81.")
                .AppendLine($"UseCustomGravity = {UseCustomGravity}")
                .AppendLine("CustomGravity = "
                            + CustomGravity.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine($"UseCustomRadius = {UseCustomRadius}")
                .AppendLine("CustomRadiusKm = "
                            + CustomRadiusKm.ToString("0.####", CultureInfo.InvariantCulture))
                .AppendLine()
                .AppendLine("# Target thrust-to-weight. 1.0 hovers, 1.5 lifts off comfortably.")
                .AppendLine("TargetThrustToWeight = "
                            + TargetThrustToWeight.ToString("0.####", CultureInfo.InvariantCulture))
                .ToString();

            File.WriteAllText(path, text);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Losing settings is not worth failing shutdown over.
        }
    }

    /// <summary>
    /// Bounds a stored value has to clear to be used at all.
    /// </summary>
    /// <remarks>
    /// A hand-edited zero or negative gravity would make every loadout trivially feasible and the
    /// requirement zero — a wrong answer that looks like a working app. Rejecting it back to the
    /// default is better than rendering nonsense, and matches the control's own limits.
    /// </remarks>
    private static bool IsPlausibleGravity(double value) => value is > 0 and <= 100;

    /// <summary>
    /// Kilometres. Zero means "not supplied"; the upper bound only rejects a typo.
    /// </summary>
    /// <remarks>
    /// Deliberately wide. A measured Verdure came out near 50 km and the moons are smaller again,
    /// but nothing stops a world spawning something far larger, and a bound tight enough to be
    /// opinionated would reject a legitimate world.
    /// </remarks>
    private static bool IsPlausibleRadius(double value) => value is > 0 and <= 100_000;

    private static bool IsPlausibleRatio(double value) => value is >= 0.1 and <= 20;

    private static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] is '#' or ';' or '[') continue;

            var separator = line.IndexOf('=', StringComparison.Ordinal);
            if (separator <= 0) continue;

            values[line[..separator].Trim()] = line[(separator + 1)..].Trim();
        }

        return values;
    }

    private static bool ReadBool(
        Dictionary<string, string> values, string key, Action<bool> apply)
    {
        if (!values.TryGetValue(key, out var text) || !bool.TryParse(text, out var value))
        {
            return false;
        }

        apply(value);
        return true;
    }

    private static bool ReadString(
        Dictionary<string, string> values, string key, Action<string> apply)
    {
        if (!values.TryGetValue(key, out var text) || text.Length == 0) return false;

        apply(text);
        return true;
    }

    private static bool ReadDouble(
        Dictionary<string, string> values, string key, Func<double, bool> valid, Action<double> apply)
    {
        if (!values.TryGetValue(key, out var text)
            || !double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !valid(value))
        {
            return false;
        }

        apply(value);
        return true;
    }
}
