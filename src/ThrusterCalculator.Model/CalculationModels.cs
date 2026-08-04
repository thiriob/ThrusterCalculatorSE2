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
/// clamped to [0,1]. Linearity is assumed pending in-game verification (Research.md §8).
/// </remarks>
public sealed record ThrustEffectivenessModel
{
    public required string Kind { get; init; }
}

/// <summary>
/// How air density falls off with altitude above a planet.
/// </summary>
/// <remarks>
/// The shipped kind is <c>linearRampAltitude</c>: density is 1.0 out to
/// <see cref="Atmosphere.ConstantAffectDistance"/> and ramps to 0 at
/// <see cref="Atmosphere.AffectDistance"/>, both expressed as multiples of planet radius.
/// </remarks>
public sealed record AtmosphereDensityModel
{
    public required string Kind { get; init; }
}
