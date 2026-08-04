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
    /// Full density out to <see cref="Atmosphere.ConstantAffectDistance"/> (~1.08), then a linear
    /// ramp to zero at <see cref="Atmosphere.AffectDistance"/> (~1.15). Both come from the planet's
    /// own definition, so future and custom planets carry their own atmosphere shape.
    /// <para>
    /// A <c>null</c> atmosphere means an airless body: density is zero everywhere, so atmospheric
    /// thrusters produce nothing there.
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
            return 1.0;
        }

        if (distanceInRadii >= edge)
        {
            return 0.0;
        }

        // Guarded above: full < distance < edge implies edge > full, so this cannot divide by zero.
        return (edge - distanceInRadii) / (edge - full);
    }
}
