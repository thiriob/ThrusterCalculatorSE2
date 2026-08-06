using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Calculations;

/// <summary>
/// Gravitational acceleration as a function of distance from a planet's centre (Research.md §5.3).
/// </summary>
public static class GravityFalloff
{
    /// <summary>Model kind implemented here.</summary>
    public const string PowerOrLinearRampKind = "powerOrLinearRamp";

    /// <summary>The <c>FallOffPower</c> value that selects a linear ramp instead of a power law.</summary>
    /// <remarks>
    /// A real sentinel, confirmed by the engine's own assert: "Currently only linear falloff is
    /// supported. Can be extended if needed". Note the contrast with
    /// <see cref="ThrustEffectiveness"/>, where an identical-looking <c>-1</c> is <em>not</em> a
    /// sentinel — the resemblance is a coincidence that has already misled this project once.
    /// </remarks>
    public const double LinearFallOffPower = -1.0;

    /// <summary>
    /// Acceleration in m/s² at <paramref name="distanceInRadii"/> from the planet's centre, where
    /// 1.0 is the surface.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Transcribed from <c>GravityGeneratorComponent.CalculateGravitationalAccelerationMagnitude</c>:
    /// </para>
    /// <code>
    /// if (fallOffPower >= 0f) num2 = Math.Pow(AccelerationDistance / r, fallOffPower);
    /// else                    num2 = Math.Clamp(1 - (r - AccelerationDistance) / (AffectDistance - AccelerationDistance), 0, 1);
    /// return GravitationalAcceleration * num2;
    /// </code>
    /// <para>
    /// <b>The power branch has no clamp in the engine and gets none here.</b> It exceeds 1 below
    /// <c>accelerationDistance</c> — gravity <em>rising</em> as you descend — and never reaches zero
    /// at <c>affectDistance</c>. That is what the engine does, and inventing a clamp would be
    /// modelling a game that does not exist. No shipped planet takes this branch.
    /// </para>
    /// <para>
    /// The linear branch is clamped at both ends by the engine itself, so surface gravity holds all
    /// the way down and gravity is exactly zero past the well's edge.
    /// </para>
    /// </remarks>
    /// <param name="surfaceGravity">Acceleration at the surface, in m/s².</param>
    /// <param name="accelerationDistance">Constant out to here, in planet radii.</param>
    /// <param name="affectDistance">Zero beyond here, in planet radii. Linear branch only.</param>
    /// <param name="fallOffPower">
    /// Exponent, or <see cref="LinearFallOffPower"/> for the linear ramp.
    /// </param>
    public static double PowerOrLinearRamp(
        double surfaceGravity,
        double accelerationDistance,
        double affectDistance,
        double fallOffPower,
        double distanceInRadii)
    {
        if (fallOffPower >= 0.0)
        {
            // The engine guards only the singularity at the centre, and so do we.
            var factor = distanceInRadii != 0.0
                ? Math.Pow(accelerationDistance / distanceInRadii, fallOffPower)
                : 1.0;

            return surfaceGravity * factor;
        }

        // Degenerate well: no interval to ramp across, so it is a step at the shared endpoint
        // rather than a divide by zero. The engine would produce ±infinity here and clamp to the
        // same answer, except exactly at the endpoint.
        if (affectDistance <= accelerationDistance)
        {
            return distanceInRadii <= accelerationDistance ? surfaceGravity : 0.0;
        }

        var ramp = 1.0 - ((distanceInRadii - accelerationDistance)
                          / (affectDistance - accelerationDistance));

        return surfaceGravity * Math.Clamp(ramp, 0.0, 1.0);
    }

    /// <summary>
    /// Acceleration in m/s² at a distance above <paramref name="planet"/>, or <c>null</c> when the
    /// planet does not carry a complete falloff model.
    /// </summary>
    /// <remarks>
    /// Returns <c>null</c> rather than falling back to surface gravity at every altitude. A planet
    /// whose falloff we cannot read is one whose climb we cannot draw, and a flat line would be a
    /// confident-looking fabrication — the exact failure the climb profile exists to avoid.
    /// </remarks>
    /// <param name="gravityOverride">
    /// User-supplied surface gravity, used in preference to the planet's own. Falloff shape is still
    /// the planet's: overriding the magnitude does not change where the well ends.
    /// </param>
    public static double? ForPlanet(Planet planet, double distanceInRadii, double? gravityOverride = null)
    {
        ArgumentNullException.ThrowIfNull(planet);

        if ((gravityOverride ?? planet.SurfaceGravity) is not { } surface
            || planet.GravityAccelerationDistance is not { } acceleration
            || planet.GravityAffectDistance is not { } affect
            || planet.GravityFallOffPower is not { } power)
        {
            return null;
        }

        return PowerOrLinearRamp(surface, acceleration, affect, power, distanceInRadii);
    }
}
