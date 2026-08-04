using ThrusterCalculator.Model;

namespace ThrusterCalculator.Extraction.Tests;

public class GameDataExtractorTests
{
    private static GameData Extract() =>
        new GameDataExtractor(Fixtures.Scan()).Extract("0.0.0-test", "sha256:test");

    [Fact]
    public void ProducesAConfigThatRoundTripsThroughTheContract()
    {
        // The strongest single check: whatever the extractor builds must be readable by the same
        // serializer every consumer uses.
        var json = GameDataSerializer.WriteToString(Extract());

        var reloaded = GameDataSerializer.Read(json);

        Assert.Equal(SchemaVersion.Current.ToString(), reloaded.SchemaVersion);
        Assert.Equal("tc", reloaded.Generator.Tool);
        Assert.Equal("sha256:test", reloaded.Source.Fingerprint);
    }

    [Fact]
    public void ExtractsConcreteThrustersAndSkipsTemplates()
    {
        var data = Extract();

        // Two concrete thrusters in the fixtures; the one under Templates/ is not a placeable block.
        Assert.Equal(2, data.Thrusters.Count);
        Assert.DoesNotContain(data.Thrusters, t => t.Name.Contains("Template", StringComparison.Ordinal));
    }

    [Fact]
    public void CarriesThrustAndIdentity()
    {
        var thruster = Extract().Thrusters.Single(t => t.Id == "testThruster");

        Assert.Equal(12345.5, thruster.ThrustNewtons);
        Assert.Equal("TestAtmo", thruster.ThrustClass);
    }

    [Fact]
    public void ResolvesDensityThroughTheCompositionJoin()
    {
        // Density lives on the block definition, which is a different file with no reference back
        // to the thruster — only the composite links them.
        var thruster = Extract().Thrusters.Single(t => t.Id == "testThruster");

        Assert.Equal("cccccccc-0000-0000-0000-000000000001", thruster.Density);
    }

    [Fact]
    public void RecordsDefinitionCounts()
    {
        var counts = Extract().Source.DefinitionCounts;

        Assert.Equal(3, counts[Fixtures.ThrusterType]);
    }

    [Fact]
    public void CarriesForwardScanWarnings()
    {
        // The deliberately broken fixtures must still be reported in the produced config, not lost
        // between scanning and projection.
        var warnings = Extract().Warnings;

        Assert.Contains(warnings, w => w.Code == "unparsableDefinition");
    }

    [Fact]
    public void ReportsBlocksWithNoRecoveredCellCount()
    {
        // The fixtures' invented blocks are not in the recovered table, so their mass is unknown —
        // and that must be visible rather than defaulted.
        var data = Extract();

        Assert.Contains(data.Warnings, w => w.Code == "unknownOccupiedCells");
        Assert.All(data.Thrusters, t =>
        {
            Assert.Null(t.OccupiedCells);
            Assert.Equal(Provenance.Unknown, t.ProvenanceOf("occupiedCells"));
        });
    }

    [Fact]
    public void ExtractsDensitiesAndResources()
    {
        var data = Extract();

        Assert.Single(data.Densities);
        Assert.Equal(10, data.Densities[0].MassCurveModifier);
    }

    [Fact]
    public void FallsBackToAStandardMassFloorWhenTheConfigurationIsMissing()
    {
        // The fixtures carry no CubeBlockMassConfiguration, so the floor is assumed — and said so.
        var data = Extract();

        Assert.Equal(5.0, data.Models.BlockMass.MinBlockMass);
        Assert.Contains(data.Warnings, w => w.Code == "missingMassConfiguration");
    }

    [Fact]
    public void ReportsMissingThrustClassConfiguration()
    {
        var data = Extract();

        Assert.Empty(data.ThrustClasses);
        Assert.Contains(data.Warnings, w => w.Code == "missingThrustClasses");
    }

    [Fact]
    public void ModelKindsMatchWhatCoreImplements()
    {
        var models = Extract().Models;

        Assert.Equal("sqrtLog10CellCount", models.BlockMass.Kind);
        Assert.Equal("linearRampAirDensity", models.ThrustEffectiveness.Kind);
        Assert.Equal("linearRampAltitude", models.AtmosphereDensity.Kind);
    }
}

public class TemplateInheritanceTests
{
    [Fact]
    public void ConcreteBlockInheritsAFieldItDoesNotRestate()
    {
        // The fixture's second thruster omits ThrustClass, exactly as hydrogen thrusters do in the
        // real data, and must pick it up from the template.
        var set = Fixtures.Scan();
        var index = BlockCompositionIndex.Build(set);
        var noClass = set.Resolve("aaaaaaaa-0000-0000-0000-000000000002")!;

        Assert.Null(noClass.GetString("ThrustClass"));

        var inherited = index.InheritedString(noClass, Fixtures.ThrusterType, "ThrustClass");

        Assert.Equal("TestHydrogen", inherited);
    }

    [Fact]
    public void InheritanceIsNullWhenNothingMatches()
    {
        var set = Fixtures.Scan();
        var index = BlockCompositionIndex.Build(set);
        var thruster = set.Resolve("aaaaaaaa-0000-0000-0000-000000000001")!;

        Assert.Null(index.InheritedString(thruster, Fixtures.ThrusterType, "NoSuchField"));
    }
}
