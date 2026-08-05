using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

/// <summary>
/// Sizing around thrusters the user has already placed.
/// </summary>
/// <remarks>
/// The configurator's whole model: a partial loadout of one family and a partial loadout of several
/// are the same computation, so these tests cover both without a second code path.
/// </remarks>
public class LoadoutSizingTests
{
    private const double Atmo250Thrust = 287_136.3;
    private const int Atmo250Cells = 288;

    /// <summary>A small thruster, for filling in around a big one.</summary>
    private const double SmallThrust = 40_000;
    private const int SmallCells = 16;

    /// <summary>Verdure at sea level: 1 g, full air density.</summary>
    private static readonly FlightEnvironment Surface = TestData.Surface(9.81);

    private static ThrusterSizer Sizer(params Thruster[] thrusters) =>
        ThrusterSizer.For(TestData.Build(thrusters));

    private static SizingRequest Request(double shipMassKg, Loadout? placed = null) => new()
    {
        ShipMassKg = shipMassKg,
        Environment = Surface,
        TargetThrustToWeight = 1.0,
        Placed = placed ?? Loadout.Empty,
    };

    [Fact]
    public void AnEmptyLoadoutReproducesTheOriginalAnswer()
    {
        // The generalisation must not move v1's numbers. If this drifts, every figure the app has
        // ever shown was wrong in one direction or the other.
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = Sizer(thruster);

        var withoutLoadout = sizer.Size(thruster, Request(500_000));
        var withEmptyLoadout = sizer.Size(thruster, Request(500_000, Loadout.Empty));

        Assert.Equal(18, withoutLoadout.Count);
        Assert.Equal(withoutLoadout.Count, withEmptyLoadout.Count);
        Assert.Equal(withoutLoadout.MaxSupportedShipMassKg, withEmptyLoadout.MaxSupportedShipMassKg, 6);
    }

    [Fact]
    public void PlacedThrustersReduceWhatIsStillNeeded()
    {
        var big = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var small = TestData.Thruster("atmo100", SmallThrust, SmallCells);
        var sizer = Sizer(big, small);

        var alone = sizer.Size(small, Request(500_000)).Count;
        var afterOneBig = sizer.Size(small, Request(500_000, new Loadout([new PlacedThruster("atmo250", 1)]))).Count;

        Assert.True(afterOneBig < alone,
            $"placing a large thruster should reduce the small ones needed ({afterOneBig} vs {alone})");
    }

    [Fact]
    public void MixingFamiliesIsTheSameComputation()
    {
        // Nothing in the solver knows or cares that these are different families — which is why
        // "mixed types" needed no separate feature.
        var atmo = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var other = TestData.Thruster("other100", SmallThrust, SmallCells);
        var sizer = Sizer(atmo, other);

        var mixed = new Loadout([new PlacedThruster("atmo250", 10), new PlacedThruster("other100", 5)]);
        var totals = sizer.Evaluate(Request(500_000, mixed));

        Assert.Equal(15, totals.ThrusterCount);
        Assert.Equal((10 * Atmo250Thrust) + (5 * SmallThrust), totals.EffectiveThrustN, 3);
    }

    [Fact]
    public void TheRequirementRisesWithThePlacedThrustersOwnWeight()
    {
        // The reason a budget cannot be computed once and counted down.
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = Sizer(thruster);

        var bare = sizer.Evaluate(Request(500_000));
        var loaded = sizer.Evaluate(Request(500_000, new Loadout([new PlacedThruster("atmo250", 10)])));

        Assert.True(loaded.RequiredThrustN > bare.RequiredThrustN);
        Assert.True(loaded.AddedMassKg > 0);
    }

    [Fact]
    public void NetContributionIsLessThanRawThrust()
    {
        // The figure that keeps the configurator's arithmetic honest: adding a thruster does not
        // reduce the shortfall by its full thrust, because its weight raises the target.
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var result = Sizer(thruster).Size(thruster, Request(500_000));

        Assert.True(result.NetContributionNEach < result.EffectiveThrustNEach);
        Assert.True(result.NetContributionNEach > 0);

        // Exactly one shortfall's worth per unit, which is what the count is derived from.
        var expected = result.EffectiveThrustNEach
                       - (Surface.GravityMetresPerSecondSquared * result.ThrusterMassKgEach);
        Assert.Equal(expected, result.NetContributionNEach, 6);
    }

    [Fact]
    public void AnAlreadySufficientLoadoutNeedsNothingMore()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = Sizer(thruster);

        var request = Request(500_000, new Loadout([new PlacedThruster("atmo250", 50)]));

        var totals = sizer.Evaluate(request);
        Assert.True(totals.IsSatisfied);
        Assert.Equal(0, totals.RemainingThrustN);

        // And no negative counts: over-provisioned means "none needed", not "minus four".
        Assert.Equal(0, sizer.Size(thruster, request).Count);
    }

    [Fact]
    public void AnUnknownPlacedMassIsFlaggedRatherThanTreatedAsZero()
    {
        // A thruster of unknown mass understates the requirement — the direction that produces a
        // ship which does not fly.
        var known = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var unknown = TestData.Thruster("mystery", SmallThrust, occupiedCells: null);
        var sizer = Sizer(known, unknown);

        var totals = sizer.Evaluate(
            Request(500_000, new Loadout([new PlacedThruster("mystery", 3)])));

        Assert.True(totals.HasUnknownMass);
        Assert.Equal(0, totals.AddedMassKg);
    }

    [Fact]
    public void ZeroAndNegativeCountsAreDropped()
    {
        // A configurator produces these naturally as a row is wound down.
        var loadout = new Loadout(
            [new PlacedThruster("a", 0), new PlacedThruster("b", -2), new PlacedThruster("c", 3)]);

        Assert.Single(loadout);
        Assert.Equal(3, loadout.TotalThrusters);
        Assert.Equal(0, loadout.CountOf("a"));
        Assert.Equal(3, loadout.CountOf("c"));
    }

    [Fact]
    public void WithReplacesRatherThanAccumulates()
    {
        var loadout = Loadout.Empty.With("atmo250", 2).With("atmo250", 5);

        Assert.Single(loadout);
        Assert.Equal(5, loadout.CountOf("atmo250"));
    }
}
