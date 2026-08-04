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

/// <summary>Inheritance resolved through the game's own <c>BaseGuid</c> parent pointer.</summary>
internal sealed class FakeInheritance(params (string Child, string Parent)[] links) : IDefinitionInheritance
{
    private readonly Dictionary<string, string> _links =
        links.ToDictionary(l => l.Child, l => l.Parent, StringComparer.OrdinalIgnoreCase);

    public string Name => "fake";

    public string? BaseOf(string guid) => _links.TryGetValue(guid, out var p) ? p : null;
}

public class DefinitionInheritanceTests
{
    private const string NoClassThruster = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string TemplateThruster = "aaaaaaaa-0000-0000-0000-000000000004";

    private static GameData ExtractWith(IDefinitionInheritance inheritance) =>
        new GameDataExtractor(Fixtures.Scan(), null, inheritance).Extract("0.0.0-test", "sha256:test");

    [Fact]
    public void ConcreteBlockInheritsAFieldItDoesNotRestate()
    {
        // Exactly the real situation: hydrogen thrusters omit ThrustClass and pick it up from the
        // base definition their BaseGuid points at.
        var data = ExtractWith(new FakeInheritance((NoClassThruster, TemplateThruster)));

        var thruster = data.Thrusters.Single(t => t.Id == "testThrusterNoClass");

        Assert.Equal("TestHydrogen", thruster.ThrustClass);
    }

    [Fact]
    public void WithoutInheritanceTheFieldStaysUnresolved()
    {
        // The honest default. An earlier version guessed the parent from component-slot overlap and
        // silently produced wrong densities; unresolved-and-warned is the better failure.
        var data = ExtractWith(new NoDefinitionInheritance());

        var thruster = data.Thrusters.Single(t => t.Id == "testThrusterNoClass");

        Assert.Null(thruster.ThrustClass);
        Assert.Contains(data.Warnings, w => w.Code == "unresolvedThrustClass");
    }

    [Fact]
    public void ChainTerminatesRatherThanLoopingForever()
    {
        // A cycle in the data must not hang extraction.
        var data = ExtractWith(new FakeInheritance(
            (NoClassThruster, TemplateThruster), (TemplateThruster, NoClassThruster)));

        Assert.NotEmpty(data.Thrusters);
    }

    [Fact]
    public void SourceNameIsRecorded()
    {
        var extractor = new GameDataExtractor(Fixtures.Scan(), null, new NoDefinitionInheritance());

        Assert.Equal("none", extractor.InheritanceSourceName);
    }
}
