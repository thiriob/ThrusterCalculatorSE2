using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

/// <summary>
/// Drives the whole consumer stack — deserialize a config, build a sizer, size a ship — against the
/// committed synthetic fixture, and checks each thruster produces the outcome the fixture's own
/// <c>_case</c> notes say it should.
/// </summary>
public class SyntheticConfigIntegrationTests
{
    private static GameData LoadFixture()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-gamedata.json");
        Assert.True(File.Exists(path), $"Expected the synthetic fixture at '{path}'.");

        return GameDataSerializer.Read(File.ReadAllText(path));
    }

    private static (ThrusterSizer Sizer, SizingRequest Request) Setup(string planetId = "testPlanetOne")
    {
        var data = LoadFixture();
        var index = new GameDataIndex(data);
        var engine = CalculationEngine.Create(data.Models);

        var planet = index.Planet(planetId);
        Assert.NotNull(planet);

        var environment = FlightEnvironment.ForPlanet(planet!, engine);
        Assert.NotNull(environment);

        return (new ThrusterSizer(index, engine),
            new SizingRequest { ShipMassKg = 100_000, Environment = environment! });
    }

    [Fact]
    public void SizesEveryThrusterInTheFixture()
    {
        var (sizer, request) = Setup();

        var results = sizer.SizeAll(request);

        Assert.Equal(5, results.Count);
    }

    [Theory]
    [InlineData("testThrusterBaseline", SizingStatus.Feasible)]
    [InlineData("testThrusterNullClass", SizingStatus.Feasible)]
    [InlineData("testThrusterSingleCell", SizingStatus.Feasible)]
    [InlineData("testThrusterTooHeavy", SizingStatus.CannotLiftOwnWeight)]
    [InlineData("testThrusterUnimplemented", SizingStatus.NotImplemented)]
    public void EachFixtureCaseProducesItsDocumentedOutcome(string thrusterId, SizingStatus expected)
    {
        var (sizer, request) = Setup();

        var result = sizer.SizeAll(request).Single(r => r.ThrusterId == thrusterId);

        Assert.Equal(expected, result.Status);
    }

    [Fact]
    public void BaselineThrusterMassMatchesTheFixtureComment()
    {
        // The fixture documents: mass = 10*sqrt(100)*log10(100)+5 = 205 kg.
        var (sizer, request) = Setup();

        var result = sizer.SizeAll(request).Single(r => r.ThrusterId == "testThrusterBaseline");

        Assert.Equal(205.0, result.ThrusterMassKgEach, 6);
    }

    [Fact]
    public void SingleCellThrusterWeighsExactlyTheFloor()
    {
        var (sizer, request) = Setup();

        var result = sizer.SizeAll(request).Single(r => r.ThrusterId == "testThrusterSingleCell");

        Assert.Equal(5.0, result.ThrusterMassKgEach);
    }

    [Fact]
    public void AirlessPlanetKillsAtmosphericThrusters()
    {
        var (sizer, request) = Setup("testPlanetAirless");

        var result = sizer.SizeAll(request).Single(r => r.ThrusterId == "testThrusterBaseline");

        Assert.Equal(SizingStatus.NoThrustInEnvironment, result.Status);
    }

    [Fact]
    public void AirlessPlanetLeavesTheNoFalloffThrusterWorking()
    {
        // The class-less thruster has no falloff, so it is unaffected by the missing atmosphere.
        var (sizer, request) = Setup("testPlanetAirless");

        var result = sizer.SizeAll(request).Single(r => r.ThrusterId == "testThrusterNullClass");

        Assert.Equal(SizingStatus.Feasible, result.Status);
        Assert.Equal(1.0, result.Effectiveness);
    }

    [Fact]
    public void PlanetWithUnknownGravityCannotBuildAnEnvironment()
    {
        var data = LoadFixture();
        var index = new GameDataIndex(data);
        var engine = CalculationEngine.Create(data.Models);

        var planet = index.Planet("testPlanetNoGravity");
        Assert.NotNull(planet);

        Assert.Null(FlightEnvironment.ForPlanet(planet!, engine));

        // ...but a user-supplied value makes it usable, which is the point.
        Assert.NotNull(FlightEnvironment.ForPlanet(planet!, engine, gravityOverride: 8.0));
    }

    [Fact]
    public void FeasibleResultsCarryTheWeakestProvenance()
    {
        // Fixture gravity is 'assumed' and cell counts are 'derived', so results are assumed.
        var (sizer, request) = Setup();

        var result = sizer.SizeAll(request).Single(r => r.ThrusterId == "testThrusterBaseline");

        Assert.Equal(Provenance.Assumed, result.Provenance);
    }
}
