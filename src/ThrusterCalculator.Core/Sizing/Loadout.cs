using System.Collections;

namespace ThrusterCalculator.Core.Sizing;

/// <summary>A quantity of one thruster type the user has already committed to.</summary>
public sealed record PlacedThruster(string ThrusterId, int Count);

/// <summary>
/// Thrusters already on the ship, which the solver sizes <em>around</em> rather than replacing.
/// </summary>
/// <remarks>
/// This is what turns the calculator into a configurator. v1 answered "if you used only this
/// thruster, how many?"; a loadout lets it answer "given what I have placed, what finishes the
/// job?" — and because a loadout may hold several types, that is the same question as "what mixes
/// with what". The single-family and mixed cases are not two features (Roadmap, v2).
/// </remarks>
public sealed class Loadout : IReadOnlyCollection<PlacedThruster>
{
    private readonly List<PlacedThruster> _placed;

    public Loadout() => _placed = [];

    public Loadout(IEnumerable<PlacedThruster> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);

        // Zero and negative counts are dropped rather than rejected: a configurator naturally
        // produces them as the user winds a row down, and they mean "not placed".
        _placed = [.. placed.Where(p => p.Count > 0)];
    }

    public static Loadout Empty { get; } = new();

    public int Count => _placed.Count;

    /// <summary>Total number of individual thrusters, across every type.</summary>
    public int TotalThrusters => _placed.Sum(p => p.Count);

    public bool IsEmpty => _placed.Count == 0;

    public IEnumerator<PlacedThruster> GetEnumerator() => _placed.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Returns a loadout with <paramref name="thrusterId"/> set to a new count.</summary>
    public Loadout With(string thrusterId, int count)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(thrusterId);

        var others = _placed.Where(p => !string.Equals(p.ThrusterId, thrusterId, StringComparison.Ordinal));

        return new Loadout(others.Append(new PlacedThruster(thrusterId, count)));
    }

    public int CountOf(string thrusterId) =>
        _placed.FirstOrDefault(p => string.Equals(p.ThrusterId, thrusterId, StringComparison.Ordinal))
            ?.Count ?? 0;
}

/// <summary>What a placed loadout contributes, and what is still missing.</summary>
public sealed record LoadoutTotals
{
    /// <summary>Thrusters placed, across every type.</summary>
    public int ThrusterCount { get; init; }

    /// <summary>Thrust they actually deliver here, after environmental effectiveness.</summary>
    public double EffectiveThrustN { get; init; }

    /// <summary>Mass they add to the ship.</summary>
    public double AddedMassKg { get; init; }

    /// <summary>
    /// Thrust needed for the ship <em>including</em> the placed thrusters' own weight.
    /// </summary>
    /// <remarks>
    /// This is why a budget cannot be computed once and counted down: every thruster added raises
    /// the target it is helping to meet (Design.md §4.2).
    /// </remarks>
    public double RequiredThrustN { get; init; }

    /// <summary>Whether any placed thruster's mass could not be resolved.</summary>
    public bool HasUnknownMass { get; init; }

    /// <summary>How much thrust is still missing. Zero once the loadout is sufficient.</summary>
    public double RemainingThrustN => Math.Max(0, RequiredThrustN - EffectiveThrustN);

    public bool IsSatisfied => EffectiveThrustN >= RequiredThrustN;

    /// <summary>Fraction of the requirement met, for a progress indicator. Not clamped above 1.</summary>
    public double Fraction => RequiredThrustN > 0 ? EffectiveThrustN / RequiredThrustN : 1.0;
}
