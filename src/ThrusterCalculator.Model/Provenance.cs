using System.Text.Json.Serialization;

namespace ThrusterCalculator.Model;

/// <summary>
/// How much a value can be trusted (Schema.md §5, Design.md P2).
/// </summary>
public enum Provenance
{
    /// <summary>
    /// Read directly from the game's definition files. The default: never written to JSON,
    /// inferred from the absence of an entry in a <c>provenance</c> map.
    /// </summary>
    Measured,

    /// <summary>Computed by us from measured inputs — e.g. a recovered <c>occupiedCells</c>.</summary>
    Derived,

    /// <summary>A curated guess or a user edit — e.g. a planet's surface gravity.</summary>
    Assumed,

    /// <summary>
    /// Not available. The value itself must be <c>null</c>; consumers show a gap and never
    /// substitute a zero.
    /// </summary>
    Unknown,
}

/// <summary>
/// Base for entities that can annotate individual fields with a non-default <see cref="Provenance"/>.
/// </summary>
/// <remarks>
/// Values are plain scalars and implicitly <see cref="Provenance.Measured"/>; only fields that are
/// something else appear in <see cref="ProvenanceOverrides"/>. Wrapping every scalar in a
/// <c>{value, provenance}</c> object would roughly double the file and wreck hand-editability
/// (Schema.md R4/R5).
/// </remarks>
public abstract record ProvenanceAware
{
    /// <summary>Non-default provenance, keyed by JSON field name. Absent entries are measured.</summary>
    [JsonPropertyName("provenance")]
    public IReadOnlyDictionary<string, Provenance>? ProvenanceOverrides { get; init; }

    /// <summary>
    /// Provenance of <paramref name="jsonFieldName"/>, defaulting to
    /// <see cref="Provenance.Measured"/>.
    /// </summary>
    /// <remarks>
    /// A dictionary lookup with a default, kept here rather than in Core so that reading the
    /// contract never requires referencing the domain layer.
    /// </remarks>
    public Provenance ProvenanceOf(string jsonFieldName) =>
        ProvenanceOverrides is not null
        && ProvenanceOverrides.TryGetValue(jsonFieldName, out var p)
            ? p
            : Provenance.Measured;
}
