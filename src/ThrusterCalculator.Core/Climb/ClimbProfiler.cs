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

    private ClimbProfiler(GameDataIndex index, CalculationEngine engine, bool configPredatesFalloff)
    {
        _index = index;
        _engine = engine;
        _configPredatesFalloff = configPredatesFalloff;
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
            new GameDataIndex(data), CalculationEngine.Create(data.Models), predates);
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
            || GravityFalloff.ForPlanet(planet, AtmosphereDensity.SurfaceDistanceInRadii, gravityOverride)
                is null)
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

        var ground = AtmosphereDensity.SurfaceDistanceInRadii;

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
            CeilingInRadii = CeilingOf(points),
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
}
