namespace ThrusterCalculator.Extraction.Tests;

public class BlockCompositionTests
{
    private const string ThrusterGuid = "aaaaaaaa-0000-0000-0000-000000000001";
    private const string NoClassThrusterGuid = "aaaaaaaa-0000-0000-0000-000000000002";
    private const string PowerableGuid = "bbbbbbbb-0000-0000-0000-000000000001";
    private const string DensityGuid = "cccccccc-0000-0000-0000-000000000001";

    private const string PowerableType = "PowerableBlockDefinitionObjectBuilder";

    private static BlockCompositionIndex Index() => BlockCompositionIndex.Build(Fixtures.Scan());

    [Fact]
    public void ReadsDeltaEncodedComponentReferences()
    {
        // Composites are delta-encoded, but the GUIDs sit inline in Components.Changed[].Value,
        // so a shallow read gets them without reimplementing engine inheritance.
        var composition = Index().All.Single(c => c.RelativePath.EndsWith(
            "TestThruster_ServerComposition.def", StringComparison.Ordinal));

        Assert.Contains(ThrusterGuid, composition.ReferencedGuids);
        Assert.Contains(PowerableGuid, composition.ReferencedGuids);
    }

    [Fact]
    public void IgnoresComponentEntriesWithNoDefinition()
    {
        // One entry in the fixture has "Definition": null — a component slot with no definition
        // attached. It must not become a dangling reference.
        var composition = Index().All.Single(c => c.RelativePath.EndsWith(
            "TestThruster_ServerComposition.def", StringComparison.Ordinal));

        Assert.Equal(2, composition.ReferencedGuids.Count);
    }

    [Fact]
    public void ReadsPlainArrayComponents()
    {
        // Nothing guarantees every composite is delta-encoded, so the plain shape is handled too.
        var composition = Index().All.Single(c => c.RelativePath.EndsWith(
            "TestThrusterPlain_ClientComposition.def", StringComparison.Ordinal));

        Assert.Contains(NoClassThrusterGuid, composition.ReferencedGuids);
        Assert.Contains(DensityGuid, composition.ReferencedGuids);
    }

    [Fact]
    public void ResolvesReferencedComponentsThroughTheGuidIndex()
    {
        var composition = Index().All.Single(c => c.RelativePath.EndsWith(
            "TestThruster_ServerComposition.def", StringComparison.Ordinal));

        Assert.Equal(2, composition.Components.Count);
        Assert.NotNull(composition.ComponentOfType(Fixtures.ThrusterType));
        Assert.NotNull(composition.ComponentOfType(PowerableType));
        Assert.Null(composition.ComponentOfType("NotPresentObjectBuilder"));
    }

    // ── the join itself ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void PairsAThrusterWithItsBlockDefinition()
    {
        // The join the whole projection depends on: thrust lives in one file, mass category and
        // name in another, and only the composite says they belong together.
        var set = Fixtures.Scan();
        var thruster = set.Resolve(ThrusterGuid)!;

        var block = BlockCompositionIndex.Build(set).FindSibling(thruster, PowerableType);

        Assert.NotNull(block);
        Assert.Equal(PowerableGuid, block!.Guid);
        Assert.Equal(DensityGuid, block.GetString("Density"));
    }

    [Fact]
    public void PairingIsSymmetric()
    {
        var set = Fixtures.Scan();
        var index = BlockCompositionIndex.Build(set);

        var block = set.Resolve(PowerableGuid)!;
        var thruster = index.FindSibling(block, Fixtures.ThrusterType);

        Assert.NotNull(thruster);
        Assert.Equal(ThrusterGuid, thruster!.Guid);
    }

    [Fact]
    public void MissingSiblingReturnsNullRatherThanGuessing()
    {
        // There is deliberately no fallback to same-directory matching: a weaker method silently
        // standing in for the real one would hide exactly the breakage worth knowing about.
        var set = Fixtures.Scan();

        // This thruster's composite references a density, not a powerable block definition.
        var thruster = set.Resolve(NoClassThrusterGuid)!;

        Assert.Null(BlockCompositionIndex.Build(set).FindSibling(thruster, PowerableType));
    }

    [Fact]
    public void DefinitionWithNoGuidCannotBePaired()
    {
        var definition = DefinitionReader.TryRead(
            "x.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": { "Thing": 1 } }""", out _)!;

        Assert.Null(Index().FindSibling(definition, PowerableType));
    }

    [Fact]
    public void ComponentLookupFindsEveryCompositeUsingIt()
    {
        var composites = Index().ContainingComponent(ThrusterGuid);

        Assert.Single(composites);
        Assert.Empty(Index().ContainingComponent("no-such-guid"));
        Assert.Empty(Index().ContainingComponent(null));
    }

    [Fact]
    public void CompositesWithNoComponentsAreSkipped()
    {
        // Every composite in the index carries at least one reference; empty ones are noise.
        Assert.All(Index().All, c => Assert.NotEmpty(c.ReferencedGuids));
    }
}
