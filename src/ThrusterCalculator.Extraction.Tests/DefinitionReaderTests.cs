namespace ThrusterCalculator.Extraction.Tests;

public class DefinitionReaderTests
{
    private const string ValidThruster = """
        {
          "$Bundles": { "Game2": "9.9.9.111", "VRage": "9.9.9.111" },
          "$Type": "Game2:Keen.Test.Fake.Movement.ThrusterDefinitionObjectBuilder",
          "$Value": { "Guid": "abc", "ThrustPower": 12345.5, "ThrustClass": "TestAtmo" }
        }
        """;

    [Fact]
    public void ReadsTheEnvelope()
    {
        var definition = DefinitionReader.TryRead("x.def", ValidThruster, out var failure);

        Assert.Null(failure);
        Assert.NotNull(definition);
        Assert.Equal("Game2", definition!.Bundle);
        Assert.Equal("ThrusterDefinitionObjectBuilder", definition.TypeName);
        Assert.Equal("abc", definition.Guid);
        Assert.Equal("9.9.9.111", definition.Bundles["Game2"]);
    }

    [Fact]
    public void ReadsTypedFieldsFromTheValue()
    {
        var definition = DefinitionReader.TryRead("x.def", ValidThruster, out _)!;

        Assert.Equal(12345.5, definition.GetDouble("ThrustPower"));
        Assert.Equal("TestAtmo", definition.GetString("ThrustClass"));
        Assert.Null(definition.GetDouble("NotThere"));
        Assert.Null(definition.GetString("ThrustPower"));   // wrong kind, not a crash
        Assert.Null(definition.GetBoolean("ThrustPower"));
    }

    [Fact]
    public void ValueSurvivesDocumentDisposal()
    {
        // The reader clones $Value out of the JsonDocument it disposes; without that, reading a
        // field here would throw ObjectDisposedException.
        var definition = DefinitionReader.TryRead("x.def", ValidThruster, out _)!;

        GC.Collect();

        Assert.Equal(12345.5, definition.GetDouble("ThrustPower"));
    }

    [Theory]
    [InlineData("{ oops", "malformed")]
    [InlineData("""{ "$Value": {} }""", "$Type")]
    [InlineData("""{ "$Type": "A:B.C" }""", "$Value")]
    [InlineData("[1,2,3]", "object")]
    [InlineData("""{ "$Type": "", "$Value": {} }""", "$Type")]
    public void BadInputBecomesAReasonRatherThanAnException(string json, string expectedInFailure)
    {
        // One unreadable document out of 17k must never abort a run.
        var definition = DefinitionReader.TryRead("bad.def", json, out var failure);

        Assert.Null(definition);
        Assert.NotNull(failure);
        Assert.Contains(expectedInFailure, failure!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MissingGuidIsAllowed()
    {
        var definition = DefinitionReader.TryRead(
            "x.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": { "Thing": 1 } }""", out _);

        Assert.NotNull(definition);
        Assert.Null(definition!.Guid);
    }

    [Theory]
    [InlineData("Game2:Keen.Game2.Simulation.Movement.ThrusterDefinitionObjectBuilder", "Game2", "ThrusterDefinitionObjectBuilder")]
    [InlineData("VRage:Keen.VRage.Core.PrefabDefinitionObjectBuilder", "VRage", "PrefabDefinitionObjectBuilder")]
    [InlineData("NoNamespaceObjectBuilder", "", "NoNamespaceObjectBuilder")]
    [InlineData("Bundle:Unqualified", "Bundle", "Unqualified")]
    public void SplitsTypeIntoBundleAndName(string type, string bundle, string name)
    {
        var (actualBundle, actualName) = DefinitionReader.SplitType(type);

        Assert.Equal(bundle, actualBundle);
        Assert.Equal(name, actualName);
    }

    [Fact]
    public void DispatchKeysOffTypeNotFilename()
    {
        // The trap that would silently drop a third of the thruster catalogue: hydrogen thrusters
        // live in files called *_HydrogenThrusterDefinition.def but carry the ordinary
        // ThrusterDefinitionObjectBuilder type.
        const string json = """
            {
              "$Type": "Game2:Keen.Test.Fake.Movement.ThrusterDefinitionObjectBuilder",
              "$Value": { "Guid": "h", "ThrustPower": 999 }
            }
            """;

        var definition = DefinitionReader.TryRead(
            "Blocks/Thrusters/Hydrogen/250/X_HydrogenThrusterDefinition.def", json, out _)!;

        Assert.Equal("ThrusterDefinitionObjectBuilder", definition.TypeName);
    }
}
