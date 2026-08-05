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

        // The block states the game key "TestAtmo"; the config carries the slugged id, and it must
        // match the entry in thrustClasses or the consumer silently loses the falloff model.
        Assert.Equal("testAtmo", thruster.ThrustClass);
    }

    [Fact]
    public void ResolvesDensityThroughTheCompositionJoin()
    {
        // Density lives on the block definition, which is a different file with no reference back
        // to the thruster — only the composite links them.
        var data = Extract();
        var thruster = data.Thrusters.Single(t => t.Id == "testThruster");

        Assert.Equal("testDensity", thruster.Density);

        // The reference must actually land in the densities table. Asserting the id alone would
        // pass just as happily on a dangling reference, which is the failure that matters.
        Assert.Contains(data.Densities, d => d.Id == thruster.Density);
    }

    [Fact]
    public void EmitsReadableIdsRatherThanGuids()
    {
        // Schema.md R1: no GUIDs in the config. R5: a user must be able to hand-edit it. Both fail
        // the moment a 36-character reference leaks into a block.
        var data = Extract();

        Assert.All(data.Densities, d => Assert.False(Guid.TryParse(d.Id, out _)));
        Assert.All(data.Resources, r => Assert.False(Guid.TryParse(r.Id, out _)));
        Assert.All(data.Thrusters, t =>
        {
            Assert.False(Guid.TryParse(t.Density ?? string.Empty, out _));
            Assert.False(Guid.TryParse(t.ConsumedResource?.Resource ?? string.Empty, out _));
        });
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

        var resource = Assert.Single(data.Resources);
        Assert.Equal("testPower", resource.Id);

        // Name keeps the game's own string; only the id is slugged. Schema.md §4.2.
        Assert.Equal("ResourceTestPower", resource.Name);
    }

    [Fact]
    public void ExtractsThrustClassesIncludingTheInvertedAndSentinelForms()
    {
        // The two shapes that break a naive reader (Schema.md §4.3): ion expresses full thrust at
        // *low* density, so min > max; hydrogen uses -1 to mean "no falloff at all".
        var classes = Extract().ThrustClasses;

        var ion = classes.Single(c => c.Id == "testIon");
        Assert.True(ion.MinThrustAirDensity > ion.MaxThrustAirDensity);

        var hydrogen = classes.Single(c => c.Id == "testHydrogen");
        Assert.Equal(-1, hydrogen.MinThrustAirDensity);
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
        // Scanned from Blocks/ alone, so the configuration under System/ is out of scope — the
        // degradation path, which must warn rather than quietly treat every thruster as falloff-free.
        var data = new GameDataExtractor(Fixtures.ScanSubtree("Blocks"))
            .Extract("0.0.0-test", "sha256:test");

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

        // Inherited as the game key "TestHydrogen", then slugged to the config id like any other.
        Assert.Equal("testHydrogen", thruster.ThrustClass);
        Assert.Contains(data.ThrustClasses, c => c.Id == thruster.ThrustClass);
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

    /// <summary>The block whose ConsumedResource states only Amount, inheriting its Type.</summary>
    private const string ThrusterBlock = "bbbbbbbb-0000-0000-0000-000000000001";

    private const string TemplateBlock = "bbbbbbbb-0000-0000-0000-000000000009";

    [Fact]
    public void ConsumedResourceTypeIsInheritedThroughAPartiallyRestatedObject()
    {
        // The real shape, and the reason a field-level walk is not enough: the block restates
        // ConsumedResource carrying only Amount, so the object is present while the Type inside it
        // is not. Reading the block's own file alone resolved 4 of 12 real thrusters — silently.
        var data = ExtractWith(new FakeInheritance((ThrusterBlock, TemplateBlock)));

        var thruster = data.Thrusters.Single(t => t.Id == "testThruster");

        Assert.NotNull(thruster.ConsumedResource);
        Assert.Equal("testPower", thruster.ConsumedResource.Resource);
        Assert.Equal(42, thruster.ConsumedResource.RatePerThrust);

        // And it must point at something really in the table, not just look plausible.
        Assert.Contains(data.Resources, r => r.Id == thruster.ConsumedResource.Resource);
    }

    [Fact]
    public void AnUnresolvableConsumedResourceIsWarnedRatherThanDroppedSilently()
    {
        // Without the base link the Type cannot be found. The old code returned null with no
        // warning, so two whole thruster families lost their fuel figure and nothing said so.
        var data = ExtractWith(new NoDefinitionInheritance());

        var thruster = data.Thrusters.Single(t => t.Id == "testThruster");

        Assert.Null(thruster.ConsumedResource);
        Assert.Contains(data.Warnings, w => w.Code == "unresolvedConsumedResource");
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
