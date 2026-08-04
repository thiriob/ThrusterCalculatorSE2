using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Sizing;

/// <summary>
/// Works out how many of a given thruster a ship needs (Technic.md §5.1).
/// </summary>
/// <remarks>
/// The non-obvious part is that thrusters have mass of their own, so adding them raises the
/// requirement that made you add them. That is a fixed point, but it has a closed form — no
/// iteration, no convergence loop:
/// <code>
///     n · T · E  ≥  R · g · (M + n · m)
///     n · (T·E − R·g·m)  ≥  R · g · M
///
///              R · g · M
///     n  ≥  ─────────────────        n = ⌈ … ⌉
///            T·E − R·g·m
/// </code>
/// Everything hinges on the denominator. If <c>T·E − R·g·m ≤ 0</c> the thruster cannot carry its
/// own weight here and <b>no</b> quantity is a solution — a naive implementation returns a
/// confident positive by dividing by a negative, or spins forever solving iteratively.
/// </remarks>
public sealed class ThrusterSizer
{
    private readonly GameDataIndex _index;
    private readonly CalculationEngine _engine;

    public ThrusterSizer(GameDataIndex index, CalculationEngine engine)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(engine);

        _index = index;
        _engine = engine;
    }

    /// <summary>Builds a sizer for a whole config.</summary>
    /// <exception cref="UnknownCalculationModelException">A model kind is not implemented.</exception>
    public static ThrusterSizer For(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return new ThrusterSizer(new GameDataIndex(data), CalculationEngine.Create(data.Models));
    }

    /// <summary>
    /// Mass of a block in kg, or <c>null</c> when it cannot be computed.
    /// </summary>
    /// <remarks>
    /// Both an unresolvable density and an unknown cell count yield <c>null</c>. Neither may fall
    /// back to the floor mass: an earlier version returned <see cref="CalculationEngine.MinBlockMassKg"/>
    /// for a missing density on the grounds that the engine does so for a block genuinely declaring
    /// none — but in a config a missing reference means <em>we failed to resolve it</em>, which is a
    /// different thing. The result was eight-tonne thrusters silently reported as weighing 5 kg,
    /// and sizing answers built on them. An unknown that surfaces beats a plausible wrong number.
    /// </remarks>
    public double? BlockMassKg(string? densityId, int? occupiedCells)
    {
        if (occupiedCells is null) return null;

        var density = _index.Density(densityId);

        return density is null
            ? null
            : _engine.BlockMassKg(occupiedCells.Value, density.MassCurveModifier);
    }

    /// <summary>Sizes every thruster in the config, in declaration order.</summary>
    public IReadOnlyList<ThrusterSizing> SizeAll(SizingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var results = new List<ThrusterSizing>(_index.Data.Thrusters.Count);
        foreach (var thruster in _index.Data.Thrusters)
        {
            results.Add(Size(thruster, request));
        }

        return results;
    }

    /// <summary>Sizes one thruster type.</summary>
    public ThrusterSizing Size(Thruster thruster, SizingRequest request)
    {
        ArgumentNullException.ThrowIfNull(thruster);
        ArgumentNullException.ThrowIfNull(request);

        if (!thruster.Implemented)
        {
            return ThrusterSizing.Rejected(thruster, SizingStatus.NotImplemented);
        }

        if (thruster.ThrustNewtons is not { } ratedThrust)
        {
            return ThrusterSizing.Rejected(thruster, SizingStatus.ThrustUnknown);
        }

        var (effectiveness, classProvenance) = EffectivenessFor(thruster, request.Environment);
        if (effectiveness <= 0)
        {
            return ThrusterSizing.Rejected(thruster, SizingStatus.NoThrustInEnvironment);
        }

        if (BlockMassKg(thruster.Density, thruster.OccupiedCells) is not { } thrusterMass)
        {
            return ThrusterSizing.Rejected(thruster, SizingStatus.MassUnknown, effectiveness);
        }

        var provenance = ProvenanceExtensions.Weakest(
            thruster.ProvenanceOf("thrustNewtons"),
            thruster.ProvenanceOf("occupiedCells"),
            request.Environment.GravityProvenance,
            classProvenance);

        var gravity = request.Environment.GravityMetresPerSecondSquared;
        var ratio = request.TargetThrustToWeight;
        var effectiveThrust = ratedThrust * effectiveness;

        // Weight per kilogram of ship at the target ratio. Zero in free fall, which makes
        // thrust-to-weight sizing degenerate — see the zero-gravity branch below.
        var weightPerKg = ratio * gravity;

        if (weightPerKg <= 0)
        {
            return new ThrusterSizing
            {
                ThrusterId = thruster.Id,
                ThrusterName = thruster.Name,
                Status = SizingStatus.Feasible,
                Count = 0,
                ThrusterMassKgEach = thrusterMass,
                TotalMassKg = request.ShipMassKg,
                Effectiveness = effectiveness,
                EffectiveThrustNEach = effectiveThrust,
                AchievedThrustToWeight = double.PositiveInfinity,
                MaxSupportedShipMassKg = double.PositiveInfinity,
                ResourceId = thruster.ConsumedResource?.Resource,
                ResourceRateTotal = 0,
                Provenance = provenance,
            };
        }

        var denominator = effectiveThrust - (weightPerKg * thrusterMass);
        if (denominator <= 0)
        {
            return ThrusterSizing.Rejected(thruster, SizingStatus.CannotLiftOwnWeight, effectiveness);
        }

        var count = (int)Math.Ceiling(weightPerKg * request.ShipMassKg / denominator);
        var addedMass = count * thrusterMass;
        var totalMass = request.ShipMassKg + addedMass;
        var totalThrust = count * effectiveThrust;

        return new ThrusterSizing
        {
            ThrusterId = thruster.Id,
            ThrusterName = thruster.Name,
            Status = SizingStatus.Feasible,
            Count = count,
            ThrusterMassKgEach = thrusterMass,
            AddedMassKg = addedMass,
            TotalMassKg = totalMass,
            Effectiveness = effectiveness,
            EffectiveThrustNEach = effectiveThrust,
            TotalThrustN = totalThrust,
            AchievedThrustToWeight = totalMass > 0 ? totalThrust / (totalMass * gravity) : double.PositiveInfinity,
            MaxSupportedShipMassKg = count * denominator / weightPerKg,
            ResourceId = thruster.ConsumedResource?.Resource,
            ResourceRateTotal = thruster.ConsumedResource is { } r ? count * r.RatePerThrust : null,
            Provenance = provenance,
        };
    }

    /// <summary>
    /// Environmental thrust multiplier, plus how much to trust it.
    /// </summary>
    /// <remarks>
    /// A thruster with no class is legitimate — hydrogen thrusters omit it in the game data — but
    /// the engine's default for that case is inferred rather than confirmed (Research.md §8). We
    /// assume no falloff, which matches hydrogen's documented behaviour, and downgrade the result's
    /// provenance to <see cref="Provenance.Assumed"/> so the UI says so rather than presenting a
    /// guess as a measurement. A dangling class reference gets the same treatment.
    /// </remarks>
    private (double Effectiveness, Provenance Provenance) EffectivenessFor(
        Thruster thruster, FlightEnvironment environment)
    {
        var thrustClass = _index.ThrustClass(thruster.ThrustClass);
        if (thrustClass is null)
        {
            return (1.0, Provenance.Assumed);
        }

        // Submersion is not modelled — water is an unshipped milestone — so water-only thrusters
        // never produce thrust.
        if (thrustClass.WaterOnly)
        {
            return (0.0, Provenance.Measured);
        }

        return (_engine.ThrustEffectivenessAt(thrustClass, environment.AirDensity), Provenance.Measured);
    }
}
