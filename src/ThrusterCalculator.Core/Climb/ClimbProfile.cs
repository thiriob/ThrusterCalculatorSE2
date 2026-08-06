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
    public double? HoverCeilingInRadii { get; init; }

    /// <summary>
    /// Where the ship actually stops, given it starts from rest on the pad and burns continuously —
    /// or <c>null</c> if it never runs out of climb.
    /// </summary>
    /// <remarks>
    /// <b>This, not <see cref="HoverCeilingInRadii"/>, is the answer to "how high does it get".</b> A
    /// ship arrives at the hover ceiling moving, and coasts on past it; a shallow dip that it
    /// crosses in a few seconds is not a ceiling at all. Reporting the hover ceiling as the stopping
    /// point told a mixed atmospheric/ion ship it stalled inside the atmosphere when it comfortably
    /// reached space.
    /// <para>
    /// Found by integrating spare acceleration over distance, which is specific kinetic energy:
    /// <c>v² / 2 = R ∫ a dr</c>. The ship stops where that integral returns to zero.
    /// </para>
    /// <para>
    /// <b>Radius cancels out of the verdict.</b> <c>R</c> is a positive constant, so where the
    /// integral crosses zero does not depend on it — which is what lets this be computed at all,
    /// given planet radius is world-instance data we do not have. Only the <em>speed</em> scales
    /// with <c>R</c>, and we never state a speed.
    /// </para>
    /// <para>
    /// <b>What it assumes:</b> thrust straight up the whole way, no drag, and that the engine's
    /// 300 m/s speed limit is never reached. The last one is the real caveat — a ship held at the
    /// cap stops banking energy, and for a deep enough dip on a large enough planet that would
    /// matter. It cannot be checked without <c>R</c>, so the legend states it rather than the model
    /// silently assuming it away.
    /// </para>
    /// </remarks>
    public double? CoastCeilingInRadii { get; init; }

    /// <summary>
    /// True when the ship crosses a stretch it could not hover in, carried by its own momentum.
    /// </summary>
    public bool CoastsThroughADip =>
        HoverCeilingInRadii is not null
        && (CoastCeilingInRadii is null || CoastCeilingInRadii > HoverCeilingInRadii);

    /// <summary>
    /// The largest planet radius, in metres, at which the coast above still works — or <c>null</c>
    /// when no coasting is involved, or the speed limit is unknown.
    /// </summary>
    /// <remarks>
    /// <b>The one place radius refuses to cancel out.</b> Everything else here is radius-free, but
    /// the engine caps ships at a fixed speed, and a cap in m/s is a cap on banked energy — while
    /// the energy a dip costs is proportional to <c>R</c>. So a coast that works on a small planet
    /// fails on a big one, and there is no answer without knowing which.
    /// <para>
    /// Rather than assume a size, the profile reports the threshold: <c>R* = v² / 2ΔE</c>, where
    /// <c>ΔE</c> is the deepest drawdown from a running energy peak. Below <c>R*</c> the ship gets
    /// through; above it, it does not. That turns an unanswerable question into a stated condition
    /// — and gives a reason to go and measure the radius (Backlog B7).
    /// </para>
    /// </remarks>
    public double? CoastRadiusLimitMetres { get; init; }

    /// <summary>
    /// The lowest height at which a ship that <em>cannot</em> lift off would hold itself up, or
    /// <c>null</c> when it either lifts off or never manages it at any height.
    /// </summary>
    /// <remarks>
    /// The mirror of <see cref="CeilingInRadii"/>, and the whole story for an ion loadout: ion
    /// thrust is zero in thick air and full above it, so an ion-only ship is often pinned to the
    /// ground while being perfectly capable higher up. Reporting only "does not leave the ground"
    /// states the problem and hides the reason — that it is a launch problem, not a thrust problem.
    /// </remarks>
    public double? HoverFloorInRadii { get; init; }

    /// <summary>True when the ship gets clear of the gravity well, coasting included.</summary>
    public bool ReachesSpace => IsAvailable && CoastCeilingInRadii is null;

    /// <summary>Total mass carried, in kg — ship plus the placed thrusters.</summary>
    public double TotalMassKg { get; init; }

    /// <summary>
    /// True when some placed thruster's mass could not be determined, so the curve is optimistic.
    /// </summary>
    public bool HasUnknownMass { get; init; }

    public static ClimbProfile Unavailable(ClimbStatus status) => new() { Status = status };
}
