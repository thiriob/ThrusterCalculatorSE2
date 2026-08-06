using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Calculations;

/// <summary>
/// Air density as a function of distance from a planet's centre (Research.md §5.2).
/// </summary>
public static class AtmosphereDensity
{
    /// <summary>Model kind implemented here.</summary>
    public const string LinearRampAltitudeKind = "linearRampAltitude";

    /// <summary>Distance from the planet's centre at its surface, in planet radii.</summary>
    public const double SurfaceDistanceInRadii = 1.0;

    /// <summary>
    /// Density in [0, 1] at <paramref name="distanceInRadii"/> (1.0 being the surface).
    /// </summary>
    /// <remarks>
    /// <see cref="Atmosphere.Density"/> out to <see cref="Atmosphere.ConstantAffectDistance"/>
    /// (~1.08), then a linear ramp to zero at <see cref="Atmosphere.AffectDistance"/> (~1.15). All
    /// three come from the planet's own definitions, so future and custom planets carry their own
    /// atmosphere.
    /// <para>
    /// This is the engine's own expression, from
    /// <c>AtmosphereGeneratorComponent.AirDataOperations.AccumulateGeneratorEffect</c>:
    /// <c>d &lt;= constant ? Density : Density / (affect - constant) * (affect - d)</c>. The engine
    /// has no zero clamp on the far side because it never evaluates the ramp beyond
    /// <c>AffectDistance</c> — the generator stops affecting entities there. Evaluating it anyway,
    /// as a pure function must, needs the clamp the engine gets from culling.
    /// </para>
    /// <para>
    /// A <c>null</c> atmosphere means an airless body: density is zero everywhere. So does a stated
    /// <see cref="Atmosphere.Density"/> of zero, which is how Palatine is airless despite carrying
    /// a full set of atmosphere distances.
    /// </para>
    /// </remarks>
    public static double LinearRampAltitude(Atmosphere? atmosphere, double distanceInRadii)
    {
        if (atmosphere is null)
        {
            return 0.0;
        }

        var full = atmosphere.ConstantAffectDistance;
        var edge = atmosphere.AffectDistance;

        if (distanceInRadii <= full)
        {
            return atmosphere.Density;
        }

        if (distanceInRadii >= edge)
        {
            return 0.0;
        }

        // Guarded above: full < distance < edge implies edge > full, so this cannot divide by zero.
        return atmosphere.Density * (edge - distanceInRadii) / (edge - full);
    }
}
