using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Sizing;

/// <summary>Why a thruster type can or cannot meet a requirement.</summary>
public enum SizingStatus
{
    /// <summary>A whole number of these thrusters meets the requirement.</summary>
    Feasible,

    /// <summary>The block ships art but no definition — not in this build of the game.</summary>
    NotImplemented,

    /// <summary>
    /// The class produces no thrust in this environment at all: atmospheric thrusters in vacuum,
    /// ion thrusters at sea level, underwater thrusters anywhere dry.
    /// </summary>
    NoThrustInEnvironment,

    /// <summary>
    /// The thruster cannot lift its own weight here, so <em>no</em> quantity is ever enough. The
    /// sizing denominator is zero or negative and the closed form has no positive solution.
    /// </summary>
    CannotLiftOwnWeight,

    /// <summary>Rated thrust is unknown, so nothing can be computed.</summary>
    ThrustUnknown,

    /// <summary>Block mass is unknown, so its self-weight cannot be accounted for.</summary>
    MassUnknown,
}

/// <summary>What it would take to lift a ship with one type of thruster.</summary>
public sealed record ThrusterSizing
{
    public required string ThrusterId { get; init; }

    public required string ThrusterName { get; init; }

    public required SizingStatus Status { get; init; }

    public bool IsFeasible => Status == SizingStatus.Feasible;

    /// <summary>Thrusters required, rounded up. Zero unless <see cref="IsFeasible"/>.</summary>
    public int Count { get; init; }

    /// <summary>Mass of one thruster, in kg.</summary>
    public double ThrusterMassKgEach { get; init; }

    /// <summary>Mass the thrusters themselves add, in kg.</summary>
    public double AddedMassKg { get; init; }

    /// <summary>Ship plus thrusters, in kg.</summary>
    public double TotalMassKg { get; init; }

    /// <summary>Environmental multiplier applied to rated thrust, in [0, 1].</summary>
    public double Effectiveness { get; init; }

    /// <summary>Usable thrust from one thruster here, in newtons.</summary>
    public double EffectiveThrustNEach { get; init; }

    /// <summary>
    /// What one more of these actually buys, in newtons.
    /// </summary>
    /// <remarks>
    /// Its effective thrust <em>less</em> the extra requirement its own weight creates:
    /// <c>T·E − R·g·m</c>. This is the number a configurator's arithmetic is built from, and the
    /// one that must be on screen — a user who adds a 100 kN thruster and sees the shortfall drop
    /// by 95 kN will otherwise read it as a bug rather than as physics.
    /// <para>
    /// It is also the sign test for feasibility: at or below zero, no quantity of this thruster
    /// ever lifts the ship, and adding one makes the shortfall <em>worse</em>. Rejected loadouts
    /// report <see cref="SizingStatus.CannotLiftOwnWeight"/> rather than a negative figure.
    /// </para>
    /// </remarks>
    public double NetContributionNEach { get; init; }

    /// <summary>Usable thrust from all of them, in newtons.</summary>
    public double TotalThrustN { get; init; }

    /// <summary>
    /// Achieved thrust-to-weight, which meets or slightly exceeds the target because
    /// <see cref="Count"/> is a whole number.
    /// </summary>
    public double AchievedThrustToWeight { get; init; }

    /// <summary>
    /// The heaviest ship (excluding thrusters) this exact configuration still lifts at the target
    /// ratio. Together with the requested mass this gives the range a configuration covers, which
    /// tells the player how much room they have to grow (Design.md §4.1).
    /// </summary>
    public double MaxSupportedShipMassKg { get; init; }

    /// <summary>Total draw at full thrust, in the resource's own flow units.</summary>
    /// <remarks>
    /// Not comparable across thrust classes: electricity and hydrogen thrusters report in different
    /// units (Research.md §3).
    /// </remarks>
    public double? ResourceRateTotal { get; init; }

    public string? ResourceId { get; init; }

    /// <summary>Weakest provenance among every input, per Design.md P2.</summary>
    public Provenance Provenance { get; init; } = Provenance.Measured;

    internal static ThrusterSizing Rejected(Thruster thruster, SizingStatus status, double effectiveness = 0) =>
        new()
        {
            ThrusterId = thruster.Id,
            ThrusterName = thruster.Name,
            Status = status,
            Effectiveness = effectiveness,
        };
}
