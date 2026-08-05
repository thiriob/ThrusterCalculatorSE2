namespace ThrusterCalculator.Core.Sizing;

/// <summary>What the player is asking for.</summary>
public sealed record SizingRequest
{
    /// <summary>
    /// Ship mass in kg, <b>excluding</b> the thrusters being proposed.
    /// </summary>
    /// <remarks>
    /// Proposals assume no thrusters are currently installed. If this figure came from a ship that
    /// already has some, their mass is counted twice — which is why the UI states the assumption
    /// permanently rather than in a dismissible dialog (Design.md §4.2).
    /// </remarks>
    public required double ShipMassKg { get; init; }

    public required FlightEnvironment Environment { get; init; }

    /// <summary>
    /// Target thrust-to-weight ratio. 1.0 exactly counteracts gravity; anything above it is
    /// acceleration to spare.
    /// </summary>
    public double TargetThrustToWeight { get; init; } = 1.0;

    /// <summary>
    /// Thrusters already committed to, which every proposal sizes around.
    /// </summary>
    /// <remarks>
    /// Empty by default, which is exactly v1's behaviour — so the configurator is a generalisation
    /// of the original question rather than a second code path beside it.
    /// </remarks>
    public Loadout Placed { get; init; } = Loadout.Empty;
}
