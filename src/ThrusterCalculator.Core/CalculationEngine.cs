using ThrusterCalculator.Core.Calculations;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core;

/// <summary>
/// Thrown when a config names a calculation model this build does not implement.
/// </summary>
/// <remarks>
/// Deliberately fatal rather than a silent fallback: quietly substituting a default model would
/// produce confident numbers computed by the wrong formula, which is the exact failure mode the
/// provenance system exists to prevent.
/// </remarks>
public sealed class UnknownCalculationModelException(string modelName, string kind)
    : Exception($"Unknown {modelName} model kind '{kind}'. This build of ThrusterCalculator cannot "
                + "interpret that config — update the app, or regenerate the config with a matching "
                + "version of tc.")
{
    public string ModelName { get; } = modelName;

    public string Kind { get; } = kind;
}

/// <summary>
/// Binds the config's named calculation models to their implementations (Schema.md §3).
/// </summary>
/// <remarks>
/// Kinds are validated once, at construction, so an unsupported config fails immediately and
/// loudly rather than at the first calculation.
/// </remarks>
public sealed class CalculationEngine
{
    private readonly double _minBlockMass;

    private CalculationEngine(double minBlockMass) => _minBlockMass = minBlockMass;

    /// <exception cref="UnknownCalculationModelException">A model kind is not implemented.</exception>
    public static CalculationEngine Create(CalculationModels models)
    {
        ArgumentNullException.ThrowIfNull(models);

        if (models.BlockMass.Kind != BlockMass.SqrtLog10CellCountKind)
        {
            throw new UnknownCalculationModelException("blockMass", models.BlockMass.Kind);
        }

        if (models.ThrustEffectiveness.Kind != ThrustEffectiveness.LinearRampAirDensityKind)
        {
            throw new UnknownCalculationModelException(
                "thrustEffectiveness", models.ThrustEffectiveness.Kind);
        }

        if (models.AtmosphereDensity.Kind != AtmosphereDensity.LinearRampAltitudeKind)
        {
            throw new UnknownCalculationModelException(
                "atmosphereDensity", models.AtmosphereDensity.Kind);
        }

        if (models.GravityFalloff.Kind != GravityFalloff.PowerOrLinearRampKind)
        {
            throw new UnknownCalculationModelException(
                "gravityFalloff", models.GravityFalloff.Kind);
        }

        // Only blockMass carries a parameter today. The other two kinds are validated above and
        // then discarded — when one of them grows parameters, retain it here the same way.
        return new CalculationEngine(models.BlockMass.MinBlockMass);
    }

    /// <summary>
    /// Floor mass in kg. Also the exact mass of a block with no density definition, and of a
    /// single-cell block.
    /// </summary>
    public double MinBlockMassKg => _minBlockMass;

    /// <summary>Mass of a block in kilograms.</summary>
    public double BlockMassKg(int occupiedCells, double massCurveModifier) =>
        Calculations.BlockMass.SqrtLog10CellCount(occupiedCells, massCurveModifier, _minBlockMass);

    /// <summary>Thrust multiplier in [0, 1] for a class at a given air density.</summary>
    public double ThrustEffectivenessAt(ThrustClass thrustClass, double airDensity) =>
        Calculations.ThrustEffectiveness.LinearRampAirDensity(thrustClass, airDensity);

    /// <summary>Air density in [0, 1] at a distance from the planet's centre, in planet radii.</summary>
    public double AirDensityAt(Atmosphere? atmosphere, double distanceInRadii) =>
        Calculations.AtmosphereDensity.LinearRampAltitude(atmosphere, distanceInRadii);

    /// <summary>
    /// Gravity in m/s² at a distance from a planet's centre, or <c>null</c> when the planet carries
    /// no complete falloff model.
    /// </summary>
    public double? GravityAt(Planet planet, double distanceInRadii, double? gravityOverride = null) =>
        Calculations.GravityFalloff.ForPlanet(planet, distanceInRadii, gravityOverride);
}
