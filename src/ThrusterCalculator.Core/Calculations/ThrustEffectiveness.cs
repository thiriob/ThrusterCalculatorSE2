using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Calculations;

/// <summary>
/// How much of a thruster's rated thrust it actually produces in a given atmosphere
/// (Research.md §3.3).
/// </summary>
public static class ThrustEffectiveness
{
    /// <summary>Model kind implemented here.</summary>
    public const string LinearRampAirDensityKind = "linearRampAirDensity";

    /// <summary>
    /// Multiplier in [0, 1] for <paramref name="airDensity"/>, itself in [0, 1].
    /// </summary>
    /// <remarks>
    /// <para>
    /// Ramps linearly from zero thrust at <see cref="ThrustClass.MinThrustAirDensity"/> to full
    /// thrust at <see cref="ThrustClass.MaxThrustAirDensity"/>. Confirmed linear against the
    /// engine's <c>GridMovementCollectorComponent.GetThrustEfficiency</c>, which this mirrors
    /// branch for branch.
    /// </para>
    /// <para>
    /// <b>The endpoints are not ordered.</b> Ion thrusters express "full thrust in vacuum" as
    /// <c>min = 0.8, max = 0.2</c>, so <c>min &gt; max</c>. The game splits on which way round they
    /// are and mirrors the ramp; that is algebraically the same as interpolating the signed
    /// interval, so the two agree. Normalising the pair, or assuming <c>min &lt; max</c>, silently
    /// inverts every ion thruster.
    /// </para>
    /// <para>
    /// <b>A negative <c>min</c> is not a sentinel</b>, though it is easy to read as one. Hydrogen
    /// declares <c>min = -1, max = 0</c>, and an earlier version of this code special-cased
    /// <c>min &lt; 0</c> as "no falloff". The engine has no such branch: it takes the ordered path
    /// and computes <c>clamp((d + 1) / 1, 0, 1)</c>, which is 1 for every density a planet can
    /// actually have. Same answer, different reason — and the invented rule would diverge the
    /// moment a class shipped with <c>min &lt; 0</c> and a <c>max</c> above the physical range.
    /// </para>
    /// </remarks>
    public static double LinearRampAirDensity(ThrustClass thrustClass, double airDensity)
    {
        ArgumentNullException.ThrowIfNull(thrustClass);

        var min = thrustClass.MinThrustAirDensity;
        var max = thrustClass.MaxThrustAirDensity;

        // Degenerate ramp: a step at the shared endpoint. The engine reaches this through its
        // ordered branch and divides by zero, getting ±infinity that clamps to the same 1 or 0 —
        // except exactly at the endpoint, where 0/0 gives it a NaN. No shipped class has
        // min == max, so this differs from the engine only where the engine is already broken.
        if (min == max)
        {
            return airDensity >= max ? 1.0 : 0.0;
        }

        var t = (airDensity - min) / (max - min);
        return Math.Clamp(t, 0.0, 1.0);
    }
}
