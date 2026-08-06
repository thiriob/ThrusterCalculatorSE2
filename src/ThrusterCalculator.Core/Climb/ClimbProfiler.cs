using ThrusterCalculator.Core.Calculations;
using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Climb;

/// <summary>
/// Walks a loadout up through a planet's atmosphere and gravity well (Roadmap v3).
/// </summary>
/// <remarks>
/// Separate from <see cref="ThrusterSizer"/> because it answers a different question. The sizer asks
/// "how many, at one height"; this asks "what happens to a fixed set, at every height". They share
/// the environment model rather than the arithmetic.
/// </remarks>
public sealed class ClimbProfiler
{
    /// <summary>
    /// Samples taken between the surface and the top of the climb.
    /// </summary>
    /// <remarks>
    /// Enough that the drawn curve is smooth at any sane window size, and cheap enough to recompute
    /// on every keystroke — each sample is a handful of multiplications per placed thruster type.
    /// The ceiling is interpolated rather than snapped to a sample, so this count sets the
    /// smoothness of the line, not the accuracy of the number beside it.
    /// </remarks>
    public const int SampleCount = 240;

    /// <summary>The gravity shape the model is valid for.</summary>
    private const string SphericalShape = "Spherical";

    private readonly GameDataIndex _index;
    private readonly CalculationEngine _engine;
    private readonly bool _configPredatesFalloff;
    private readonly double? _maxSpeedMetresPerSecond;

    private ClimbProfiler(
        GameDataIndex index, CalculationEngine engine, bool configPredatesFalloff,
        double? maxSpeedMetresPerSecond)
    {
        _index = index;
        _engine = engine;
        _configPredatesFalloff = configPredatesFalloff;
        _maxSpeedMetresPerSecond = maxSpeedMetresPerSecond;
    }

    /// <exception cref="UnknownCalculationModelException">A model kind is not implemented.</exception>
    public static ClimbProfiler For(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        // A config older than 1.2 cannot carry a falloff for any planet, which is a different
        // problem from a planet that lacks one — and one the user can fix by rebuilding.
        var predates = !SchemaVersion.TryParse(data.SchemaVersion, out var version)
                       || version.CompareTo(SchemaVersion.GravityFalloffIntroduced) < 0;

        return new ClimbProfiler(
            new GameDataIndex(data), CalculationEngine.Create(data.Models), predates,
            data.Limits?.MaxSpeedMetresPerSecond);
    }

    /// <summary>
    /// Profiles <paramref name="placed"/> lifting <paramref name="shipMassKg"/> off
    /// <paramref name="planet"/>.
    /// </summary>
    /// <param name="gravityOverride">
    /// User-supplied surface gravity, used in preference to the planet's own. The falloff shape
    /// stays the planet's: overriding the magnitude does not move where the well ends.
    /// </param>
    public ClimbProfile Profile(
        Planet planet, Loadout placed, double shipMassKg, double? gravityOverride = null)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(placed);

        // Gravity is modelled as a function of distance from the centre, which is only the story for
        // a spherical field. Refusing is the point: a cylindrical or directional generator would be
        // plotted as a plausible-looking curve that is simply about the wrong geometry.
        if (planet.GravityShape is { Length: > 0 } shape
            && !string.Equals(shape, SphericalShape, StringComparison.OrdinalIgnoreCase))
        {
            return ClimbProfile.Unavailable(ClimbStatus.UnsupportedGravityShape);
        }

        if (planet.GravityAffectDistance is not { } top
            || GravityFalloff.ForPlanet(
                planet,
                AtmosphereDensity.SurfaceDistanceInRadii + (planet.GroundOffsetInRadii ?? 0.0),
                gravityOverride) is null)
        {
            return ClimbProfile.Unavailable(_configPredatesFalloff
                ? ClimbStatus.ConfigPredatesFalloff
                : ClimbStatus.NoFalloffModel);
        }

        var (thrusters, addedMassKg, hasUnknownMass) = Resolve(placed);
        if (thrusters.Count == 0)
        {
            return ClimbProfile.Unavailable(ClimbStatus.NothingToFly);
        }

        var totalMassKg = shipMassKg + addedMassKg;
        if (totalMassKg <= 0)
        {
            return ClimbProfile.Unavailable(ClimbStatus.NothingToFly);
        }

        // The terrain's sea level sits above the reference sphere, so the climb starts there and not
        // at r = 1. It changes no surface answer — both ramps are still clamped 900 m up on Verdure
        // — but every height above it would otherwise be offset by that much.
        var ground = AtmosphereDensity.SurfaceDistanceInRadii + (planet.GroundOffsetInRadii ?? 0.0);

        // Stop at the top of the gravity well rather than an arbitrary height: past it gravity is
        // zero and the curve is a vertical line carrying no information. Guarded so a planet whose
        // well somehow ends at or below the surface still produces a drawable single point.
        var ceilingOfPlot = Math.Max(top, ground);

        var points = new List<ClimbPoint>(SampleCount);

        for (var i = 0; i < SampleCount; i++)
        {
            var t = SampleCount == 1 ? 0.0 : (double)i / (SampleCount - 1);
            var distance = ground + (t * (ceilingOfPlot - ground));

            points.Add(Sample(planet, thrusters, totalMassKg, distance, gravityOverride));
        }

        return new ClimbProfile
        {
            Status = ClimbStatus.Available,
            Points = points,
            Markers = MarkersFor(planet, ground, ceilingOfPlot),
            HoverCeilingInRadii = CeilingOf(points),
            CoastCeilingInRadii = CoastCeilingOf(points),
            CoastRadiusLimitMetres = CoastRadiusLimitOf(points, _maxSpeedMetresPerSecond),
            HoverFloorInRadii = HoverFloorOf(points),
            TotalMassKg = totalMassKg,
            HasUnknownMass = hasUnknownMass,
        };
    }

    private ClimbPoint Sample(
        Planet planet,
        IReadOnlyList<(Thruster Thruster, ThrustClass? Class, int Count, double Rated)> thrusters,
        double totalMassKg,
        double distanceInRadii,
        double? gravityOverride)
    {
        var airDensity = _engine.AirDensityAt(planet.Atmosphere, distanceInRadii);
        var gravity = _engine.GravityAt(planet, distanceInRadii, gravityOverride) ?? 0.0;

        var thrust = 0.0;
        foreach (var (_, thrustClass, count, rated) in thrusters)
        {
            // No class means no known falloff, so the thruster is taken at its rated thrust — the
            // same choice the sizer makes, kept identical so the two never disagree at the surface.
            var effectiveness = thrustClass is null
                ? 1.0
                : _engine.ThrustEffectivenessAt(thrustClass, airDensity);

            thrust += count * rated * effectiveness;
        }

        return new ClimbPoint(
            distanceInRadii, airDensity, gravity, thrust, (thrust / totalMassKg) - gravity);
    }

    /// <summary>Placed thrusters that can actually contribute, plus what they weigh.</summary>
    private (List<(Thruster Thruster, ThrustClass? Class, int Count, double Rated)> Thrusters,
        double AddedMassKg, bool HasUnknownMass) Resolve(Loadout placed)
    {
        var thrusters = new List<(Thruster, ThrustClass?, int, double)>();
        var addedMassKg = 0.0;
        var hasUnknownMass = false;

        foreach (var entry in placed)
        {
            var thruster = _index.Thruster(entry.ThrusterId);
            if (thruster is null || !thruster.Implemented) continue;

            var thrustClass = _index.ThrustClass(thruster.ThrustClass);

            // Water-only thrusters produce nothing anywhere dry, and submersion is not modelled.
            // They still weigh what they weigh, so they are dropped from thrust and kept in mass.
            if (thruster.ThrustNewtons is { } rated && thrustClass is not { WaterOnly: true })
            {
                thrusters.Add((thruster, thrustClass, entry.Count, rated));
            }

            if (BlockMassKg(thruster) is { } each)
            {
                addedMassKg += entry.Count * each;
            }
            else
            {
                // A thruster of unknown mass understates the load, which flatters the curve. Flagged
                // rather than guessed, for the same reason the sizer flags it.
                hasUnknownMass = true;
            }
        }

        return (thrusters, addedMassKg, hasUnknownMass);
    }

    private double? BlockMassKg(Thruster thruster)
    {
        if (thruster.OccupiedCells is not { } cells) return null;

        var density = _index.Density(thruster.Density);
        return density is null ? null : _engine.BlockMassKg(cells, density.MassCurveModifier);
    }

    /// <summary>
    /// Ground, the atmosphere edge, and the top of the gravity well — the heights a player thinks in.
    /// </summary>
    /// <remarks>
    /// The atmosphere edge is omitted when it sits outside the plotted range, which is what keeps
    /// the legacy 100-radii planets from drawing a marker far off the chart (Backlog B4).
    /// </remarks>
    private static IReadOnlyList<ClimbMarker> MarkersFor(Planet planet, double ground, double top)
    {
        var markers = new List<ClimbMarker> { new("Ground", ground) };

        if (planet.Atmosphere is { } atmosphere
            && atmosphere.Density > 0
            && atmosphere.AffectDistance > ground
            && atmosphere.AffectDistance < top)
        {
            markers.Add(new ClimbMarker("Atmosphere edge", atmosphere.AffectDistance));
        }

        markers.Add(new ClimbMarker("Space", top));

        return [.. markers.OrderBy(m => m.DistanceInRadii)];
    }

    /// <summary>
    /// The height at which spare acceleration first reaches zero, interpolated between samples.
    /// </summary>
    private static double? CeilingOf(IReadOnlyList<ClimbPoint> points)
    {
        if (points.Count == 0) return null;

        // Cannot lift off at all: the ceiling is the ground, not "no ceiling".
        if (points[0].SpareAccelerationMetresPerSecondSquared <= 0)
        {
            return points[0].DistanceInRadii;
        }

        for (var i = 1; i < points.Count; i++)
        {
            var previous = points[i - 1];
            var current = points[i];

            if (current.SpareAccelerationMetresPerSecondSquared > 0) continue;

            var drop = previous.SpareAccelerationMetresPerSecondSquared
                       - current.SpareAccelerationMetresPerSecondSquared;

            // Guarded against a pair that straddles zero with no difference between them, which
            // would otherwise divide by zero for a fraction that is meaningless anyway.
            var fraction = drop > 0
                ? previous.SpareAccelerationMetresPerSecondSquared / drop
                : 0.0;

            return previous.DistanceInRadii
                   + (fraction * (current.DistanceInRadii - previous.DistanceInRadii));
        }

        return null;
    }

    /// <summary>
    /// Where a ship starting from rest on the pad actually runs out of climb.
    /// </summary>
    /// <remarks>
    /// Integrates spare acceleration over distance — specific kinetic energy, <c>v² / 2 = R ∫ a
    /// dr</c> — and returns where that comes back to zero. Trapezoidal, which is exact for the
    /// piecewise-linear curve the ramps produce between samples.
    /// <para>
    /// The planet radius <c>R</c> is a positive constant multiplying the whole integral, so it
    /// cannot move the zero crossing. That is the only reason this is computable: the speed at any
    /// height needs <c>R</c>, but the question "does it stop, and where" does not.
    /// </para>
    /// </remarks>
    private static double? CoastCeilingOf(IReadOnlyList<ClimbPoint> points)
    {
        if (points.Count == 0) return null;

        // Never gets moving, so there is no momentum to carry it anywhere.
        if (points[0].SpareAccelerationMetresPerSecondSquared <= 0) return points[0].DistanceInRadii;

        var energy = 0.0;

        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];

            var span = to.DistanceInRadii - from.DistanceInRadii;
            var a = (from.SpareAccelerationMetresPerSecondSquared
                     + to.SpareAccelerationMetresPerSecondSquared) / 2.0;

            var next = energy + (a * span);
            if (next > 0)
            {
                energy = next;
                continue;
            }

            // Ran out somewhere inside this segment. Constant acceleration across it puts the stop
            // at energy / -a, and a guard keeps a segment that ends exactly on zero from dividing
            // by it.
            var reached = a < 0 ? energy / -a : span;

            return from.DistanceInRadii + Math.Min(reached, span);
        }

        return null;
    }

    /// <summary>
    /// The largest planet radius at which the ship's momentum still carries it across every dip.
    /// </summary>
    /// <remarks>
    /// The deepest drawdown from a running peak of the energy integral is what the ship has to pay
    /// for out of banked speed, and it scales with <c>R</c>; the speed limit caps what can be
    /// banked. Equating them gives <c>R* = v² / 2ΔE</c>.
    /// <para>
    /// <c>null</c> when nothing is paid for out of momentum — the answer holds at any size — or
    /// when the config carries no speed limit to compare against.
    /// </para>
    /// </remarks>
    private static double? CoastRadiusLimitOf(
        IReadOnlyList<ClimbPoint> points, double? maxSpeedMetresPerSecond)
    {
        if (maxSpeedMetresPerSecond is not { } speed || speed <= 0) return null;
        if (points.Count == 0 || points[0].SpareAccelerationMetresPerSecondSquared <= 0) return null;

        var energy = 0.0;
        var peak = 0.0;
        var deepest = 0.0;

        for (var i = 1; i < points.Count; i++)
        {
            var from = points[i - 1];
            var to = points[i];

            energy += (from.SpareAccelerationMetresPerSecondSquared
                       + to.SpareAccelerationMetresPerSecondSquared) / 2.0
                      * (to.DistanceInRadii - from.DistanceInRadii);

            peak = Math.Max(peak, energy);
            deepest = Math.Max(deepest, peak - energy);
        }

        return deepest > 0 ? speed * speed / (2.0 * deepest) : null;
    }

    /// <summary>
    /// The lowest height at which a ship that cannot lift off would hold itself up.
    /// </summary>
    /// <remarks>
    /// Only meaningful when spare acceleration is negative at the ground; otherwise the ship flies
    /// from the pad and the first crossing is a ceiling, not a floor.
    /// <para>
    /// <b>Strictly positive, not merely non-negative.</b> At the top of the gravity well gravity is
    /// zero, so a ship with no thrust left scores exactly zero spare acceleration and technically
    /// "hovers" — by being weightless, not by flying. Accepting that reported a floor out in space
    /// for a loadout that cannot fly anywhere, which is worse than saying nothing.
    /// </para>
    /// </remarks>
    private static double? HoverFloorOf(IReadOnlyList<ClimbPoint> points)
    {
        if (points.Count == 0 || points[0].SpareAccelerationMetresPerSecondSquared > 0) return null;

        for (var i = 1; i < points.Count; i++)
        {
            var below = points[i - 1];
            var above = points[i];

            if (above.SpareAccelerationMetresPerSecondSquared <= 0) continue;

            var rise = above.SpareAccelerationMetresPerSecondSquared
                       - below.SpareAccelerationMetresPerSecondSquared;

            var fraction = rise > 0
                ? -below.SpareAccelerationMetresPerSecondSquared / rise
                : 0.0;

            return below.DistanceInRadii
                   + (fraction * (above.DistanceInRadii - below.DistanceInRadii));
        }

        return null;
    }
}
