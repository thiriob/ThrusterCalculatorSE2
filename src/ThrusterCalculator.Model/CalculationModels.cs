namespace ThrusterCalculator.Model;

/// <summary>
/// Named models plus their parameters (Schema.md §3, Technic.md §3.2).
/// </summary>
/// <remarks>
/// The config selects behaviour by name and supplies parameters; Core implements a closed set of
/// kinds. It deliberately does not embed formula strings — a changed formula <em>shape</em> needs
/// producer changes anyway, so an expression evaluator would buy nothing while costing an eval
/// surface in a file users hand-edit.
/// <para>
/// An unrecognised <c>Kind</c> must be a hard, actionable error in Core — never a silent fallback.
/// </para>
/// </remarks>
public sealed record CalculationModels
{
    public required BlockMassModel BlockMass { get; init; }

    public required ThrustEffectivenessModel ThrustEffectiveness { get; init; }

    public required AtmosphereDensityModel AtmosphereDensity { get; init; }

    /// <summary>
    /// How gravity falls off with altitude. Added in schema 1.2.
    /// </summary>
    /// <remarks>
    /// Optional, unlike its siblings, purely so 1.0 and 1.1 configs still load — they predate the
    /// field, and a missing model is a schema-evolution question rather than the "unrecognised kind"
    /// case that must stay fatal (Schema.md R6).
    /// </remarks>
    public GravityFalloffModel GravityFalloff { get; init; } = new() { Kind = "powerOrLinearRamp" };
}

/// <summary>
/// How a block's mass is derived from its occupied cell count.
/// </summary>
/// <remarks>
/// The shipped kind is <c>sqrtLog10CellCount</c>, transcribed from the decompiled engine
/// (Research.md §4.0):
/// <c>mass = massCurveModifier * sqrt(V) * log10(V) + minBlockMass</c>.
/// </remarks>
public sealed record BlockMassModel
{
    public required string Kind { get; init; }

    /// <summary>
    /// Floor mass in kg, from the game's <c>CubeBlockMassConfiguration</c>. Not an arbitrary clamp:
    /// because <c>log10(1) == 0</c>, a single-cell block lands on exactly this value.
    /// </summary>
    public required double MinBlockMass { get; init; }
}

/// <summary>
/// How air density maps to a thrust multiplier for a given thrust class.
/// </summary>
/// <remarks>
/// The shipped kind is <c>linearRampAirDensity</c>: ramp between the class's
/// <see cref="ThrustClass.MinThrustAirDensity"/> and <see cref="ThrustClass.MaxThrustAirDensity"/>,
/// clamped to [0,1]. Linearity is <b>confirmed</b> against
/// <c>GridMovementCollectorComponent.GetThrustEfficiency</c>, not assumed (Research.md §3.3).
/// </remarks>
public sealed record ThrustEffectivenessModel
{
    public required string Kind { get; init; }
}

/// <summary>
/// How air density falls off with altitude above a planet.
/// </summary>
/// <remarks>
/// The shipped kind is <c>linearRampAltitude</c>: density is <see cref="Atmosphere.Density"/> out to
/// <see cref="Atmosphere.ConstantAffectDistance"/> and ramps to 0 at
/// <see cref="Atmosphere.AffectDistance"/>, both expressed as multiples of planet radius. Confirmed
/// linear against <c>AtmosphereGeneratorComponent</c> (Research.md §5.2.1).
/// </remarks>
public sealed record AtmosphereDensityModel
{
    public required string Kind { get; init; }
}

/// <summary>
/// How gravitational acceleration falls off with distance from a planet's centre.
/// </summary>
/// <remarks>
/// The shipped kind is <c>powerOrLinearRamp</c>, transcribed from
/// <c>GravityGeneratorComponent.CalculateGravitationalAccelerationMagnitude</c>: surface gravity out
/// to <see cref="Planet.GravityAccelerationDistance"/>, then either a power law with exponent
/// <see cref="Planet.GravityFallOffPower"/> or — when that is <c>-1</c>, as every shipped planet
/// sets it — a linear ramp to zero at <see cref="Planet.GravityAffectDistance"/>.
/// <para>
/// One kind rather than two because the engine makes the choice per planet, from a field, not per
/// build. Splitting them would push a runtime branch into config selection.
/// </para>
/// </remarks>
public sealed record GravityFalloffModel
{
    public required string Kind { get; init; }
}
