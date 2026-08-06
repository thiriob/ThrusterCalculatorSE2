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
    /// <see cref="Provenance.Measured"/> for every shipped planet: the gravity generator states it
    /// outright, usually inherited from a legacy base template (Research.md §5.3). An earlier draft
    /// of this comment claimed the opposite — that it could never be read — on the strength of a
    /// reader that took one field off the component and ignored the rest.
    /// <para>
    /// A newly discovered planet with no entry gets <c>null</c>. It must still be listed, with an
    /// empty editable field — that is what makes future and custom planets usable on day one.
    /// </para>
    /// </remarks>
    public double? SurfaceGravity { get; init; }

    /// <summary>
    /// Planet radius in metres. Added in schema 1.4.
    /// </summary>
    /// <remarks>
    /// Every other distance here is a multiple of this, so it is what turns the whole model into
    /// kilometres.
    /// <para>
    /// Reached two hops off the planet's own composition —
    /// <c>PlanetGeneratorDefinition → DetailCubemap → TargetPlanetRadius</c> — which is 60 000 m for
    /// every planet and 20 000 m for every moon. An earlier pass looked only at
    /// <c>PlanetConfiguratorComponent.Radius</c>, which the spawner prefab ships as a
    /// <c>0</c> placeholder, and concluded from that alone the value was world data. It is not.
    /// </para>
    /// <para>
    /// A world may still spawn a planet at a different size, so this is the shipped default and not
    /// a promise about a particular save — the same standing as surface gravity, and overridable
    /// for the same reason.
    /// </para>
    /// </remarks>
    public double? RadiusMetres { get; init; }

    /// <summary>
    /// Height of the terrain's sea level above the reference sphere, as a fraction of the radius.
    /// </summary>
    /// <remarks>
    /// <b>Useless to omit, because altitude is measured from the ground and not from the sphere.</b>
    /// The surface sits at <c>1 + GroundOffsetInRadii</c>, so on Verdure — 0.015, or 900 m — an
    /// altitude of 4.81 km is <c>r = 1.095</c>, not <c>1.080</c>.
    /// <para>
    /// Leaving it out is what made a measured Verdure come out at 50 km against the stated 60 km,
    /// and made <see cref="RadiusMetres"/> look like a rendering parameter to be distrusted.
    /// Putting it back reconciles the measurement to within 13 m (Research.md §5.3.1.1).
    /// </para>
    /// </remarks>
    public double? GroundOffsetInRadii { get; init; }

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
