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
    public static readonly SchemaVersion Current = new(1, 0);

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
