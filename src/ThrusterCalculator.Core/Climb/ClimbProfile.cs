namespace ThrusterCalculator.Core.Climb;

/// <summary>One height on the climb, and what the ship can do there.</summary>
/// <param name="DistanceInRadii">Distance from the planet's centre; 1.0 is the surface.</param>
/// <param name="AirDensity">Air density in [0, 1] at this height.</param>
/// <param name="GravityMetresPerSecondSquared">Local gravity here.</param>
/// <param name="ThrustNewtons">Usable thrust from the whole loadout here.</param>
/// <param name="SpareAccelerationMetresPerSecondSquared">
/// <c>thrust ÷ mass − gravity</c>. Zero exactly hovers; below zero the ship is falling.
/// </param>
public sealed record ClimbPoint(
    double DistanceInRadii,
    double AirDensity,
    double GravityMetresPerSecondSquared,
    double ThrustNewtons,
    double SpareAccelerationMetresPerSecondSquared);

/// <summary>A named height, because planet radii mean nothing to a player.</summary>
/// <param name="Label">e.g. <c>Atmosphere edge</c>.</param>
/// <param name="DistanceInRadii">Where it sits.</param>
public sealed record ClimbMarker(string Label, double DistanceInRadii);

/// <summary>Why a profile could not be produced.</summary>
public enum ClimbStatus
{
    /// <summary>A profile was computed.</summary>
    Available,

    /// <summary>The planet carries no complete gravity falloff — nothing to plot against.</summary>
    NoFalloffModel,

    /// <summary>
    /// The config was written before the gravity falloff was extracted, so <em>no</em> planet in it
    /// can carry one.
    /// </summary>
    /// <remarks>
    /// Distinguished from <see cref="NoFalloffModel"/> because the two need opposite responses. A
    /// planet genuinely missing its falloff is a fact about the game; a config that predates the
    /// field is a fact about the file on disk, and the fix is to rebuild it. Reporting the second
    /// as the first blames the planet for the tooling's age — which is exactly what it did.
    /// </remarks>
    ConfigPredatesFalloff,

    /// <summary>The gravity field is not spherical, so distance from the centre is not the story.</summary>
    UnsupportedGravityShape,

    /// <summary>Nothing is placed, or the placed thrusters have no known mass or thrust.</summary>
    NothingToFly,
}

/// <summary>
/// What a fixed loadout can do at every height between the ground and the edge of the gravity well
/// (Roadmap v3).
/// </summary>
/// <remarks>
/// <para>
/// <b>Spare acceleration, not thrust-to-weight.</b> TWR is the right question beside a planet and a
/// meaningless one away from it: weight tends to zero out of the well, so every ship's ratio runs to
/// infinity and a nimble ship reads identically to a sluggish one. Subtracting gravity instead of
/// dividing by it stays finite and keeps meaning something — zero is the hard floor, a dip below it
/// is the stall, and the value it settles at up top is exactly how briskly the ship accelerates in
/// space.
/// </para>
/// <para>
/// <b>This is a static analysis, not a flight simulation.</b> Each point answers "if the ship were
/// hovering here, could it still climb?" — it carries no velocity, no fuel burn and no mass change.
/// That is the honest scope for a profile drawn from a parts list, and it is what makes the ceiling
/// meaningful: a real ship with momentum may coast past a height it cannot hover at.
/// </para>
/// </remarks>
public sealed record ClimbProfile
{
    public required ClimbStatus Status { get; init; }

    public bool IsAvailable => Status == ClimbStatus.Available;

    /// <summary>Samples from the surface outward, in increasing distance.</summary>
    public IReadOnlyList<ClimbPoint> Points { get; init; } = [];

    /// <summary>Named heights worth drawing, in increasing distance.</summary>
    public IReadOnlyList<ClimbMarker> Markers { get; init; } = [];

    /// <summary>
    /// The highest point the ship can still hover at, in planet radii, or <c>null</c> if it never
    /// runs out of climb.
    /// </summary>
    /// <remarks>
    /// Found by linear interpolation between the two samples that straddle the zero crossing, so it
    /// does not inherit the sample spacing as error. <c>null</c> when spare acceleration stays
    /// positive all the way out — the ship reaches space — and equal to the surface distance when
    /// it cannot lift off at all.
    /// </remarks>
    public double? CeilingInRadii { get; init; }

    /// <summary>True when the ship never stalls: it climbs clear of the gravity well.</summary>
    public bool ReachesSpace => IsAvailable && CeilingInRadii is null;

    /// <summary>Total mass carried, in kg — ship plus the placed thrusters.</summary>
    public double TotalMassKg { get; init; }

    /// <summary>
    /// True when some placed thruster's mass could not be determined, so the curve is optimistic.
    /// </summary>
    public bool HasUnknownMass { get; init; }

    public static ClimbProfile Unavailable(ClimbStatus status) => new() { Status = status };
}
