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

    /// <summary>Extent of the gravity well, as a multiple of planet radius. Gravity is zero beyond.</summary>
    public double? GravityAffectDistance { get; init; }

    /// <summary>
    /// Distance out to which gravity is still at its surface value, as a multiple of planet radius.
    /// </summary>
    /// <remarks>
    /// The inner end of the falloff, and typically just above the surface (1.05). Gravity does not
    /// begin dropping at the ground.
    /// </remarks>
    public double? GravityAccelerationDistance { get; init; }

    /// <summary>
    /// Falloff exponent, or <c>-1</c> for a linear ramp.
    /// </summary>
    /// <remarks>
    /// A non-negative value is a genuine exponent on
    /// <c>GravityAccelerationDistance / distance</c> — 2 would be Newtonian. <c>-1</c> selects a
    /// linear ramp instead, and the engine asserts that no other negative value is supported.
    /// <para>
    /// <b>Unlike the identical-looking <c>-1</c> in a thrust class, this one really is a sentinel</b>
    /// (Research.md §5.3). Every shipped planet uses it: <c>DefaultGravityGenerator</c> pins both
    /// <c>MinFallOffPower</c> and <c>MaxFallOffPower</c> to <c>-1</c>.
    /// </para>
    /// </remarks>
    public double? GravityFallOffPower { get; init; }

    /// <summary>
    /// Shape of the gravity field, e.g. <c>Spherical</c>.
    /// </summary>
    /// <remarks>
    /// Carried as a guard rather than a parameter. The climb model treats gravity as a function of
    /// distance from the planet's centre, which is only true for a spherical field; a planet with
    /// any other shape would be quietly mismodelled, so the producer warns rather than the consumer
    /// assuming.
    /// </remarks>
    public string? GravityShape { get; init; }

    /// <summary><c>null</c> for an airless body — atmospheric thrusters produce nothing there.</summary>
    public Atmosphere? Atmosphere { get; init; }
}

/// <summary>
/// Atmosphere geometry and strength (Research.md §5.2). Distances are multiples of planet radius.
/// </summary>
public sealed record Atmosphere
{
    /// <summary>Air density reaches zero at this distance. Typically ~1.15.</summary>
    public required double AffectDistance { get; init; }

    /// <summary>Air density is <see cref="Density"/> out to this distance. Typically ~1.08.</summary>
    public required double ConstantAffectDistance { get; init; }

    /// <summary>
    /// Air density inside <see cref="ConstantAffectDistance"/> — the plateau the ramp falls from.
    /// </summary>
    /// <remarks>
    /// Not always 1.0, and not a formality: <b>Palatine states 0</b>, so it has an atmosphere's
    /// geometry and no air in it, and atmospheric thrusters produce nothing there. Every other
    /// planet in the game inherits 1.0.
    /// <para>
    /// The two halves live apart in the game's data — the distances on the planet's generator
    /// <em>component</em>, this on the generator <em>definition</em> it points at — which is why an
    /// earlier reader picked up the shape and silently assumed the strength
    /// (<c>AtmosphereGeneratorComponent</c>: <c>Density = _definition.Density</c>).
    /// </para>
    /// <para>
    /// Defaults to 1.0 so schema 1.0 configs, written before this was extracted, keep loading with
    /// the behaviour they were generated under.
    /// </para>
    /// </remarks>
    public double Density { get; init; } = 1.0;
}
