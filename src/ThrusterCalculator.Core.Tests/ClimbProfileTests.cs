using ThrusterCalculator.Core.Calculations;
using ThrusterCalculator.Core.Climb;
using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

public class GravityFalloffTests
{
    // Every shipped planet: surface gravity out to 1.05 R, linear to zero at 1.35 R.
    private const double Accel = 1.05;
    private const double Affect = 1.35;
    private const double G = 9.80665;

    private static double Verdure(double distanceInRadii) =>
        GravityFalloff.PowerOrLinearRamp(G, Accel, Affect, -1.0, distanceInRadii);

    [Theory]
    [InlineData(1.00, 1.0)]     // ground
    [InlineData(1.05, 1.0)]     // top of the constant shell
    [InlineData(1.20, 0.5)]     // halfway down the ramp
    [InlineData(1.35, 0.0)]     // edge of the well
    [InlineData(2.00, 0.0)]     // beyond
    public void LinearRampMatchesTheEngine(double distanceInRadii, double expectedFraction)
    {
        Assert.Equal(G * expectedFraction, Verdure(distanceInRadii), 8);
    }

    [Fact]
    public void PredictsTwoThirdsOfAGravityAtVerduresAtmosphereEdge()
    {
        // The check to run in game (Research §5.3.1). A power-law fit to one reading previously
        // suggested 0.33 g here; the extracted model says 0.67, and they cannot both be right.
        Assert.Equal(0.667, Verdure(1.15) / G, 3);
    }

    [Fact]
    public void GravityHoldsBelowTheAccelerationDistanceRatherThanRising()
    {
        // The linear branch is clamped at the top by the engine, so descending below 1.05 R does
        // not push gravity above its surface value.
        Assert.Equal(G, Verdure(0.5), 8);
    }

    [Fact]
    public void PowerBranchIsTranscribedWithoutAClampTheEngineDoesNotHave()
    {
        // No shipped planet takes this branch, but if one ever does it must behave as the engine
        // does: an inverse square that exceeds surface gravity below the acceleration distance and
        // never quite reaches zero. Inventing a clamp would model a game that does not exist.
        Assert.Equal(G, GravityFalloff.PowerOrLinearRamp(G, 1.0, 1.35, 2.0, 1.0), 8);
        Assert.Equal(G / 4, GravityFalloff.PowerOrLinearRamp(G, 1.0, 1.35, 2.0, 2.0), 8);
        Assert.True(GravityFalloff.PowerOrLinearRamp(G, 1.0, 1.35, 2.0, 0.5) > G);
    }

    [Fact]
    public void DegenerateWellBecomesAStepRatherThanDividingByZero()
    {
        Assert.Equal(G, GravityFalloff.PowerOrLinearRamp(G, 1.2, 1.2, -1.0, 1.1), 8);
        Assert.Equal(0.0, GravityFalloff.PowerOrLinearRamp(G, 1.2, 1.2, -1.0, 1.3), 8);
    }

    [Fact]
    public void PlanetWithoutACompleteModelDeclinesRatherThanGuessing()
    {
        var partial = new Planet
        {
            Id = "partial", Name = "Partial", SurfaceGravity = G, GravityAffectDistance = 1.35,
        };

        Assert.Null(GravityFalloff.ForPlanet(partial, 1.1));
    }

    [Fact]
    public void OverridingSurfaceGravityKeepsThePlanetsFalloffShape()
    {
        var planet = PlanetFor(gravity: G);

        // Half the gravity at the surface should still be half of it halfway up the ramp — the
        // override scales the magnitude, it does not move where the well ends.
        Assert.Equal(2.0, GravityFalloff.ForPlanet(planet, 1.20, gravityOverride: 4.0)!.Value, 8);
        Assert.Equal(0.0, GravityFalloff.ForPlanet(planet, 1.35, gravityOverride: 4.0)!.Value, 8);
    }

    internal static Planet PlanetFor(double gravity = G, Atmosphere? atmosphere = null) => new()
    {
        Id = "verdure",
        Name = "Verdure",
        SurfaceGravity = gravity,
        GravityAffectDistance = Affect,
        GravityAccelerationDistance = Accel,
        GravityFallOffPower = -1.0,
        GravityShape = "Spherical",
        Atmosphere = atmosphere,
    };
}

public class ClimbProfilerTests
{
    private static readonly Atmosphere VerdureAir =
        new() { ConstantAffectDistance = 1.0, AffectDistance = 1.15, Density = 1.0 };

    private static ClimbProfile Profile(string thrusterId, int count, double shipMassKg) =>
        ProfileOver(VerdureAir, thrusterId, count, shipMassKg);

    /// <summary>
    /// Explicit about the atmosphere, because <c>null</c> is a meaningful value here — an airless
    /// body — and must not be confused with "unspecified".
    /// </summary>
    private static ClimbProfile ProfileOver(
        Atmosphere? atmosphere, string thrusterId, int count, double shipMassKg)
    {
        var data = TestData.Config();
        var planet = GravityFalloffTests.PlanetFor(atmosphere: atmosphere);

        return ClimbProfiler.For(data)
            .Profile(planet, new Loadout([new PlacedThruster(thrusterId, count)]), shipMassKg);
    }

    [Fact]
    public void ProducesASampleAtTheGroundAndOneAtTheTopOfTheWell()
    {
        var profile = Profile(TestData.AtmosphericThrusterId, 20, 5_000);

        Assert.True(profile.IsAvailable);
        Assert.Equal(ClimbProfiler.SampleCount, profile.Points.Count);
        Assert.Equal(1.0, profile.Points[0].DistanceInRadii, 8);
        Assert.Equal(1.35, profile.Points[^1].DistanceInRadii, 8);
    }

    [Fact]
    public void GravityIsZeroAtTheTopSoSpareAccelerationIsPlainThrustOverMass()
    {
        var profile = Profile(TestData.HydrogenThrusterId, 20, 5_000);
        var top = profile.Points[^1];

        Assert.Equal(0.0, top.GravityMetresPerSecondSquared, 8);
        Assert.Equal(
            top.ThrustNewtons / profile.TotalMassKg,
            top.SpareAccelerationMetresPerSecondSquared,
            8);
    }

    [Fact]
    public void AtmosphericThrustersStallBeforeSpaceEvenWhenTheyLiftOffComfortably()
    {
        // The failure this whole feature exists to catch (Roadmap v3): on Verdure the air starts
        // thinning at ground level, so a ship can leave the pad on pure atmospheric thrust and be
        // unable to leave the atmosphere. Lifting off is not the same question as getting out.
        var profile = Profile(TestData.AtmosphericThrusterId, 40, 5_000);

        Assert.True(profile.IsAvailable);
        Assert.True(profile.Points[0].SpareAccelerationMetresPerSecondSquared > 0,
            "should lift off the ground");
        Assert.False(profile.ReachesSpace, "atmospheric thrust must run out before space");

        var ceiling = Assert.IsType<double>(profile.HoverCeilingInRadii);
        Assert.InRange(ceiling, 1.0, 1.15);
    }

    [Fact]
    public void HydrogenReachesSpaceBecauseItsThrustDoesNotDependOnAir()
    {
        var profile = Profile(TestData.HydrogenThrusterId, 40, 5_000);

        Assert.True(profile.ReachesSpace);
        Assert.Null(profile.HoverCeilingInRadii);
    }

    [Fact]
    public void AMixedShipCoastsThroughTheHandoverDipInsteadOfStalling()
    {
        // The case that shipped wrong. Atmospheric fades out exactly as ion comes in — the two
        // ramps are complementary — but ion is far weaker per block, so total thrust collapses
        // mid-handover and the ship briefly cannot hold itself up. It arrives there moving, and
        // carries straight through. Reporting the hover ceiling as the stopping point told a ship
        // that comfortably reaches space that it was stuck inside the atmosphere.
        var data = TestData.Config();
        var planet = GravityFalloffTests.PlanetFor(atmosphere: VerdureAir);

        // Ion carrying roughly a fifth of the atmospheric thrust, which is about the ratio a real
        // mixed ship ends up with — enough to make the dip shallow, not enough to remove it.
        var profile = ClimbProfiler.For(data).Profile(planet, new Loadout([
            new PlacedThruster(TestData.AtmosphericThrusterId, 100),
            new PlacedThruster(TestData.IonThrusterId, 20),
        ]), 5_000);

        Assert.True(profile.IsAvailable);

        // It really does lose lift on the way — this is not a case with no dip at all.
        Assert.NotNull(profile.HoverCeilingInRadii);
        Assert.True(profile.Points.Any(p => p.SpareAccelerationMetresPerSecondSquared < 0),
            "the loadout should have a stretch it cannot hover in");

        // And it gets to space anyway.
        Assert.True(profile.CoastsThroughADip);
        Assert.Null(profile.CoastCeilingInRadii);
        Assert.True(profile.ReachesSpace);
    }

    [Fact]
    public void TheCoastCeilingIsNeverBelowTheHoverCeiling()
    {
        // Momentum can only ever carry the ship further than hovering would. If these ever invert,
        // the integration has the wrong sign somewhere.
        foreach (var count in new[] { 2, 5, 10, 20, 40, 80 })
        {
            var profile = Profile(TestData.AtmosphericThrusterId, count, 5_000);
            if (profile.HoverCeilingInRadii is not { } hover) continue;

            var coast = profile.CoastCeilingInRadii ?? double.PositiveInfinity;
            Assert.True(coast >= hover - 1e-9,
                $"{count} thrusters: coast {coast} below hover {hover}");
        }
    }

    [Fact]
    public void AGroundedShipStopsAtTheGroundUnderBothMeasures()
    {
        var profile = Profile(TestData.AtmosphericThrusterId, 1, 10_000_000);

        Assert.Equal(1.0, profile.HoverCeilingInRadii!.Value, 8);
        Assert.Equal(1.0, profile.CoastCeilingInRadii!.Value, 8);
        Assert.False(profile.CoastsThroughADip);
    }

    [Fact]
    public void AnIonLoadoutIsGroundedButReportsTheHeightItWouldFlyFrom()
    {
        // Ion produces nothing in thick air and everything above it, so an ion-only ship is often
        // pinned to the pad while being perfectly capable higher up. "Does not leave the ground"
        // alone states the problem and hides the reason — that it is a launch problem.
        var data = TestData.Config();
        var planet = GravityFalloffTests.PlanetFor(atmosphere: VerdureAir);

        var profile = ClimbProfiler.For(data).Profile(
            planet, new Loadout([new PlacedThruster(TestData.IonThrusterId, 40)]), 5_000);

        Assert.True(profile.IsAvailable);
        Assert.Equal(1.0, profile.HoverCeilingInRadii!.Value, 8);

        var floor = Assert.IsType<double>(profile.HoverFloorInRadii);
        Assert.InRange(floor, 1.0, 1.15);

        // Below the floor it is falling; above it, flying. That is the whole shape.
        Assert.True(profile.Points[0].SpareAccelerationMetresPerSecondSquared < 0);
        Assert.True(profile.Points[^1].SpareAccelerationMetresPerSecondSquared > 0);
    }

    [Fact]
    public void AShipThatFliesFromThePadHasNoHoverFloor()
    {
        // The floor is only meaningful for a grounded ship; otherwise the first crossing is a
        // ceiling and reporting both would describe two opposite things at once.
        var profile = Profile(TestData.AtmosphericThrusterId, 40, 5_000);

        Assert.Null(profile.HoverFloorInRadii);
        Assert.NotNull(profile.HoverCeilingInRadii);
    }

    [Fact]
    public void AHopelessLoadoutHasNeitherACeilingAboveGroundNorAFloor()
    {
        // Note what this nearly reported: at the top of the well gravity is zero and atmospheric
        // thrust is zero too, so spare acceleration is exactly zero and the ship "hovers" — by
        // being weightless, not by flying. A floor out in space for a loadout that cannot fly is
        // worse than no floor at all, so the crossing has to be strictly positive.
        var profile = Profile(TestData.AtmosphericThrusterId, 1, 10_000_000);

        Assert.Equal(1.0, profile.HoverCeilingInRadii!.Value, 8);
        Assert.Null(profile.HoverFloorInRadii);
        Assert.Equal(0.0, profile.Points[^1].SpareAccelerationMetresPerSecondSquared, 8);
    }

    [Fact]
    public void AShipThatCannotLiftOffHasItsCeilingAtTheGroundRatherThanNone()
    {
        // A single thruster under a very heavy ship never leaves the pad. Reporting "no ceiling"
        // here would read as "reaches space", which is the opposite of the truth.
        var profile = Profile(TestData.AtmosphericThrusterId, 1, 10_000_000);

        Assert.Equal(1.0, profile.HoverCeilingInRadii!.Value, 8);
        Assert.False(profile.ReachesSpace);
    }

    [Fact]
    public void CeilingIsInterpolatedRatherThanSnappedToASample()
    {
        var profile = Profile(TestData.AtmosphericThrusterId, 40, 5_000);
        var ceiling = profile.HoverCeilingInRadii!.Value;

        // The crossing should sit strictly between the straddling samples, not on one of them.
        var below = profile.Points.Last(p => p.SpareAccelerationMetresPerSecondSquared > 0);
        var above = profile.Points.First(p => p.DistanceInRadii > below.DistanceInRadii);

        Assert.InRange(ceiling, below.DistanceInRadii, above.DistanceInRadii);
    }

    [Fact]
    public void AirlessPlanetGetsNoAtmosphereMarker()
    {
        var profile = ProfileOver(null, TestData.HydrogenThrusterId, 20, 5_000);

        Assert.DoesNotContain(profile.Markers, m => m.Label == "Atmosphere edge");
        Assert.Equal(["Ground", "Space"], profile.Markers.Select(m => m.Label));
    }

    [Fact]
    public void PalatineStyleZeroDensityAtmosphereGetsNoMarkerEither()
    {
        // It has the distances but no air, so drawing an "atmosphere edge" would promise a
        // transition that does not happen (Backlog B16).
        var airless = new Atmosphere
        {
            ConstantAffectDistance = 1.0, AffectDistance = 1.15, Density = 0.0,
        };

        var profile = ProfileOver(airless, TestData.HydrogenThrusterId, 20, 5_000);

        Assert.DoesNotContain(profile.Markers, m => m.Label == "Atmosphere edge");
    }

    [Fact]
    public void LegacyHundredRadiiAtmosphereDrawsNoMarkerOffTheChart()
    {
        // Backlog B4: MarsLike and friends inherit AffectDistance = 100, far outside the plotted
        // range. Faithful to the data, and not something to draw a gridline for.
        var absurd = new Atmosphere
        {
            ConstantAffectDistance = 1.0, AffectDistance = 100.0, Density = 1.0,
        };

        var profile = ProfileOver(absurd, TestData.HydrogenThrusterId, 20, 5_000);

        Assert.DoesNotContain(profile.Markers, m => m.Label == "Atmosphere edge");
    }

    [Fact]
    public void PlanetWithoutAFalloffModelDeclinesToDrawAnything()
    {
        var planet = new Planet
        {
            Id = "bare", Name = "Bare", SurfaceGravity = 9.81, GravityAffectDistance = 1.35,
        };

        var profile = ClimbProfiler.For(TestData.Config()).Profile(
            planet, new Loadout([new PlacedThruster(TestData.HydrogenThrusterId, 4)]), 5_000);

        Assert.Equal(ClimbStatus.NoFalloffModel, profile.Status);
        Assert.Empty(profile.Points);
    }

    [Fact]
    public void AConfigOlderThanTheFalloffBlamesItselfRatherThanThePlanet()
    {
        // The case that actually shipped broken: a gamedata.json extracted before schema 1.2 has no
        // falloff for any planet, and the app told the user "Verdure states no gravity falloff" —
        // blaming the game for the file's age, with no hint that rebuilding would fix it.
        var stale = TestData.Config() with { SchemaVersion = "1.1" };
        var planet = GravityFalloffTests.PlanetFor() with
        {
            GravityAccelerationDistance = null, GravityFallOffPower = null, GravityShape = null,
        };

        var profile = ClimbProfiler.For(stale).Profile(
            planet, new Loadout([new PlacedThruster(TestData.HydrogenThrusterId, 4)]), 5_000);

        Assert.Equal(ClimbStatus.ConfigPredatesFalloff, profile.Status);
    }

    [Fact]
    public void ACurrentConfigStillBlamesThePlanetWhenOnlyThatPlanetLacksAFalloff()
    {
        // The distinction has to cut both ways, or it just relabels every failure.
        var planet = GravityFalloffTests.PlanetFor() with
        {
            GravityAccelerationDistance = null, GravityFallOffPower = null,
        };

        var profile = ClimbProfiler.For(TestData.Config()).Profile(
            planet, new Loadout([new PlacedThruster(TestData.HydrogenThrusterId, 4)]), 5_000);

        Assert.Equal(ClimbStatus.NoFalloffModel, profile.Status);
    }

    [Fact]
    public void NonSphericalGravityIsRefusedRatherThanPlottedWithTheWrongGeometry()
    {
        var planet = GravityFalloffTests.PlanetFor() with { GravityShape = "Cylindrical" };

        var profile = ClimbProfiler.For(TestData.Config()).Profile(
            planet, new Loadout([new PlacedThruster(TestData.HydrogenThrusterId, 4)]), 5_000);

        Assert.Equal(ClimbStatus.UnsupportedGravityShape, profile.Status);
    }

    [Fact]
    public void EmptyLoadoutHasNothingToFly()
    {
        var profile = ClimbProfiler.For(TestData.Config())
            .Profile(GravityFalloffTests.PlanetFor(), Loadout.Empty, 5_000);

        Assert.Equal(ClimbStatus.NothingToFly, profile.Status);
    }

    [Fact]
    public void SurfacePointAgreesWithTheSizersOwnEnvironment()
    {
        // The two must never disagree about the ground, or the chart contradicts the table above it.
        var data = TestData.Config();
        var planet = GravityFalloffTests.PlanetFor(atmosphere: VerdureAir);
        var loadout = new Loadout([new PlacedThruster(TestData.AtmosphericThrusterId, 12)]);

        var profile = ClimbProfiler.For(data).Profile(planet, loadout, 5_000);

        var engine = CalculationEngine.Create(data.Models);
        var environment = FlightEnvironment.ForPlanet(planet, engine)!;
        var totals = ThrusterSizer.For(data).Evaluate(new SizingRequest
        {
            ShipMassKg = 5_000,
            Environment = environment,
            Placed = loadout,
            TargetThrustToWeight = 1.0,
        });

        Assert.Equal(totals.EffectiveThrustN, profile.Points[0].ThrustNewtons, 6);
        Assert.Equal(
            environment.GravityMetresPerSecondSquared,
            profile.Points[0].GravityMetresPerSecondSquared,
            8);
    }
}
