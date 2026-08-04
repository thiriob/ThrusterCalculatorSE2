namespace ThrusterCalculator.Model;

/// <summary>A planet or moon the player can depart from.</summary>
/// <remarks>
/// The game ships milestone-versioned duplicates (Verdure appears under both VS2_0 and VS2_3). The
/// producer resolves these — newest milestone wins — so a consumer never sees the same planet twice.
/// </remarks>
public sealed record Planet : ProvenanceAware
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Content milestone the winning definition came from, e.g. <c>VS2_3</c>. Display and diagnostics only.</summary>
    public string? Milestone { get; init; }

    /// <summary>Surface gravity in m/s².</summary>
    /// <remarks>
    /// Always <see cref="Provenance.Assumed"/> or <see cref="Provenance.Unknown"/> — never measured.
    /// Planet radius is world-instance data rather than definition data, so surface gravity cannot
    /// be read from the shipped files (Research.md §5.3).
    /// <para>
    /// A newly discovered planet with no entry gets <c>null</c>. It must still be listed, with an
    /// empty editable field — that is what makes future and custom planets usable on day one.
    /// </para>
    /// </remarks>
    public double? SurfaceGravity { get; init; }

    /// <summary>Extent of the gravity well, as a multiple of planet radius.</summary>
    public double? GravityAffectDistance { get; init; }

    /// <summary><c>null</c> for an airless body — atmospheric thrusters produce nothing there.</summary>
    public Atmosphere? Atmosphere { get; init; }
}

/// <summary>
/// Atmosphere geometry, in multiples of planet radius (Research.md §5.2).
/// </summary>
public sealed record Atmosphere
{
    /// <summary>Air density reaches zero at this distance. Typically ~1.15.</summary>
    public required double AffectDistance { get; init; }

    /// <summary>Air density is full (1.0) out to this distance. Typically ~1.08.</summary>
    public required double ConstantAffectDistance { get; init; }
}
