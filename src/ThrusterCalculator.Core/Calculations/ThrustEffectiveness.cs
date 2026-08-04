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
    /// thrust at <see cref="ThrustClass.MaxThrustAirDensity"/>.
    /// </para>
    /// <para>
    /// <b>The endpoints are not ordered.</b> Ion thrusters express "full thrust in vacuum" as
    /// <c>min = 0.8, max = 0.2</c>, so <c>min &gt; max</c>. Interpolating across the interval
    /// signed handles both directions; normalising the pair, or assuming <c>min &lt; max</c>,
    /// silently inverts every ion thruster.
    /// </para>
    /// <para>
    /// A negative <c>min</c> is the game's sentinel for "no falloff at all" — hydrogen thrusters
    /// use <c>-1</c>. Any negative value is treated as the sentinel, since air density is never
    /// negative.
    /// </para>
    /// </remarks>
    public static double LinearRampAirDensity(ThrustClass thrustClass, double airDensity)
    {
        ArgumentNullException.ThrowIfNull(thrustClass);

        var min = thrustClass.MinThrustAirDensity;
        var max = thrustClass.MaxThrustAirDensity;

        if (min < 0)
        {
            return 1.0;
        }

        // Degenerate ramp: a step at the shared endpoint rather than a divide by zero.
        if (min == max)
        {
            return airDensity >= max ? 1.0 : 0.0;
        }

        var t = (airDensity - min) / (max - min);
        return Math.Clamp(t, 0.0, 1.0);
    }
}
