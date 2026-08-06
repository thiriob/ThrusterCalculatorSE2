using System.Globalization;

namespace ThrusterCalculator.Model;

/// <summary>
/// A <c>major.minor</c> schema version and its compatibility rules (Schema.md §2).
/// </summary>
/// <remarks>
/// Configs outlive the app version that wrote them — a user may hand a newer file to an older
/// build — so the compatibility check has to be explicit rather than hopeful.
/// </remarks>
public readonly record struct SchemaVersion(int Major, int Minor) : IComparable<SchemaVersion>
{
    /// <summary>The version this build writes and expects.</summary>
    /// <remarks>
    /// 1.1 added <see cref="Atmosphere.Density"/>. Additive, so 1.0 files still load — they simply
    /// take the 1.0 default of full density, which was right for every planet but Palatine.
    /// <para>
    /// 1.2 added the gravity falloff — <see cref="Planet.GravityAccelerationDistance"/>,
    /// <see cref="Planet.GravityFallOffPower"/>, <see cref="Planet.GravityShape"/> and the
    /// <see cref="CalculationModels.GravityFalloff"/> model. Also additive: an older config simply
    /// carries no falloff, and a consumer that cannot read one declines to draw a climb rather than
    /// inventing a flat line.
    /// </para>
    /// </remarks>
    public static readonly SchemaVersion Current = new(1, 2);

    /// <summary>
    /// The first version whose configs can carry a gravity falloff.
    /// </summary>
    /// <remarks>
    /// Consumers need this to tell "this planet has no falloff" from "this file is too old to have
    /// one". Both leave the field null and only the version distinguishes them.
    /// </remarks>
    public static readonly SchemaVersion GravityFalloffIntroduced = new(1, 2);

    public static bool TryParse(string? text, out SchemaVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('.');
        if (parts.Length != 2) return false;

        if (!int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            return false;
        }

        version = new SchemaVersion(major, minor);
        return true;
    }

    /// <summary>
    /// Whether this build can read <paramref name="fileVersion"/>.
    /// </summary>
    /// <remarks>
    /// A differing major is a refusal. A higher minor loads: unknown fields are additive and are
    /// ignored (Schema.md R6), so an older reader still gets everything it understands.
    /// </remarks>
    public static bool IsReadableByCurrent(SchemaVersion fileVersion) =>
        fileVersion.Major == Current.Major;

    public int CompareTo(SchemaVersion other) =>
        Major != other.Major ? Major.CompareTo(other.Major) : Minor.CompareTo(other.Minor);

    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Major}.{Minor}");
}

/// <summary>Thrown when a config cannot be read, with a message intended for a user to act on.</summary>
public sealed class GameDataFormatException : Exception
{
    public GameDataFormatException(string message) : base(message) { }

    public GameDataFormatException(string message, Exception inner) : base(message, inner) { }
}
