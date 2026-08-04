namespace ThrusterCalculator.Model.Tests;

/// <summary>
/// Proves the DTOs actually match Schema.md by reading the committed synthetic fixture, including
/// every edge case it was built to carry (Technic.md §7.1).
/// </summary>
public class SyntheticFixtureTests
{
    [Fact]
    public void Deserializes()
    {
        var data = Fixture.Load();

        Assert.Equal("1.0", data.SchemaVersion);
        Assert.Equal("tc", data.Generator.Tool);
        Assert.Equal("0.0.0-synthetic", data.Source.GameBuild);
    }

    [Fact]
    public void ReadsEveryCollection()
    {
        var data = Fixture.Load();

        Assert.Equal(2, data.Densities.Count);
        Assert.Equal(2, data.Resources.Count);
        Assert.Equal(4, data.ThrustClasses.Count);
        Assert.Equal(5, data.Thrusters.Count);
        Assert.Equal(2, data.Containers.Count);
        Assert.Single(data.Tanks);
        Assert.Equal(3, data.Planets.Count);
        Assert.Equal(2, data.Warnings.Count);
    }

    [Fact]
    public void ReadsCalculationModels()
    {
        var models = Fixture.Load().Models;

        Assert.Equal("sqrtLog10CellCount", models.BlockMass.Kind);
        Assert.Equal(5.0, models.BlockMass.MinBlockMass);
        Assert.Equal("linearRampAirDensity", models.ThrustEffectiveness.Kind);
        Assert.Equal("linearRampAltitude", models.AtmosphereDensity.Kind);
    }

    [Fact]
    public void UnknownFieldsAreIgnored()
    {
        // The fixture carries _comment and _case annotations that have no DTO counterpart.
        // Ignoring them is what makes additive schema changes safe (Schema.md R6).
        var data = Fixture.Load();

        Assert.NotEmpty(data.Thrusters);
    }

    // ── edge cases the fixture exists to carry ────────────────────────────────────────────────

    [Fact]
    public void ThrustClassMayBeNull()
    {
        // Hydrogen thrusters omit ThrustClass in the real game data.
        var thruster = Single(Fixture.Load().Thrusters, t => t.Id == "testThrusterNullClass");

        Assert.Null(thruster.ThrustClass);
    }

    [Fact]
    public void NoFalloffSentinelSurvives()
    {
        var thrustClass = Single(Fixture.Load().ThrustClasses, c => c.Id == "testNoFalloff");

        Assert.Equal(-1.0, thrustClass.MinThrustAirDensity);
    }

    [Fact]
    public void InvertedRampOrderingIsPreserved()
    {
        // Ion thrusters express "full thrust at LOW density" as min > max. Nothing may normalise
        // this away — doing so silently inverts them.
        var ion = Single(Fixture.Load().ThrustClasses, c => c.Id == "testIon");

        Assert.True(ion.MinThrustAirDensity > ion.MaxThrustAirDensity);
        Assert.Equal(0.8, ion.MinThrustAirDensity);
        Assert.Equal(0.2, ion.MaxThrustAirDensity);
    }

    [Fact]
    public void WaterOnlyClassIsFlagged()
    {
        var water = Single(Fixture.Load().ThrustClasses, c => c.Id == "testWaterOnly");

        Assert.True(water.WaterOnly);
    }

    [Fact]
    public void UnimplementedBlockKeepsNullsRatherThanZeros()
    {
        var thruster = Single(Fixture.Load().Thrusters, t => t.Id == "testThrusterUnimplemented");

        Assert.False(thruster.Implemented);
        Assert.Null(thruster.ThrustNewtons);
        Assert.Null(thruster.ConsumedResource);
        Assert.Null(thruster.OccupiedCells);
    }

    [Fact]
    public void ImplementedDefaultsToTrueWhenAbsent()
    {
        var container = Single(Fixture.Load().Containers, c => c.Id == "testContainerSmall");

        // Containers have no 'implemented' field at all; the thruster default must not leak oddly.
        Assert.Equal(64, container.OccupiedCells);
    }

    [Fact]
    public void PlanetMayHaveNoGravity()
    {
        var planet = Single(Fixture.Load().Planets, p => p.Id == "testPlanetNoGravity");

        Assert.Null(planet.SurfaceGravity);
        Assert.Equal(Provenance.Unknown, planet.ProvenanceOf("surfaceGravity"));
    }

    [Fact]
    public void PlanetMayHaveNoAtmosphere()
    {
        var planet = Single(Fixture.Load().Planets, p => p.Id == "testPlanetAirless");

        Assert.Null(planet.Atmosphere);
    }

    [Fact]
    public void AtmosphereGeometryIsRead()
    {
        var planet = Single(Fixture.Load().Planets, p => p.Id == "testPlanetOne");

        Assert.NotNull(planet.Atmosphere);
        Assert.Equal(1.15, planet.Atmosphere!.AffectDistance);
        Assert.Equal(1.08, planet.Atmosphere.ConstantAffectDistance);
    }

    // ── provenance ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AnnotatedFieldsReportTheirProvenance()
    {
        var thruster = Single(Fixture.Load().Thrusters, t => t.Id == "testThrusterBaseline");

        Assert.Equal(Provenance.Derived, thruster.ProvenanceOf("occupiedCells"));
    }

    [Fact]
    public void UnannotatedFieldsDefaultToMeasured()
    {
        var thruster = Single(Fixture.Load().Thrusters, t => t.Id == "testThrusterBaseline");

        Assert.Equal(Provenance.Measured, thruster.ProvenanceOf("thrustNewtons"));
        Assert.Equal(Provenance.Measured, thruster.ProvenanceOf("noSuchFieldAtAll"));
    }

    [Fact]
    public void EntitiesWithNoProvenanceMapStillDefaultToMeasured()
    {
        // An entity whose JSON carried no 'provenance' object at all has a null override map.
        var thruster = new Thruster { Id = "x", Name = "x", SizeCm = 100 };

        Assert.Null(thruster.ProvenanceOverrides);
        Assert.Equal(Provenance.Measured, thruster.ProvenanceOf("anything"));
    }

    private static T Single<T>(IReadOnlyList<T> items, Func<T, bool> predicate)
    {
        var matches = items.Where(predicate).ToList();
        Assert.Single(matches);
        return matches[0];
    }
}
