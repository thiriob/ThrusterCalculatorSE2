namespace ThrusterCalculator.Extraction.Tests;

public class BlockNamingTests
{
    [Theory]
    [InlineData("Blocks/Thrusters/Atmospheric/250/AtmosphericThruster250_ThrusterDefinition.def", "AtmosphericThruster250")]
    [InlineData("Blocks/Thrusters/Hydrogen/250/HydrogenThruster250_HydrogenThrusterDefinition.def", "HydrogenThruster250")]
    [InlineData("Blocks/X/Foo.def", "Foo")]
    [InlineData("A_B_C.def", "A")]
    public void DerivesBlockNameFromFileName(string path, string expected) =>
        Assert.Equal(expected, BlockNaming.BlockNameOf(path));

    [Theory]
    [InlineData("AtmosphericThruster250", "atmosphericThruster250")]
    [InlineData("X", "x")]
    public void IdIsCamelCase(string block, string expected) =>
        Assert.Equal(expected, BlockNaming.IdOf(block));

    [Theory]
    [InlineData("AtmosphericThruster250", 250)]
    [InlineData("HydrogenThruster50", 50)]
    [InlineData("AtmosphericThruster1000", 1000)]
    [InlineData("NoSizeHere", null)]
    public void ReadsTrailingSizeInCentimetres(string block, int? expected) =>
        Assert.Equal(expected, BlockNaming.SizeCmOf(block));

    [Theory]
    [InlineData("AtmosphericThruster250", "Atmospheric Thruster 2.5 m")]
    [InlineData("AtmosphericThruster100", "Atmospheric Thruster 1 m")]
    [InlineData("HydrogenThruster50", "Hydrogen Thruster 0.5 m")]
    [InlineData("AtmosphericThruster1000", "Atmospheric Thruster 10 m")]
    [InlineData("IonThruster150", "Ion Thruster 1.5 m")]
    [InlineData("CargoContainer750", "Cargo Container 7.5 m")]
    public void BuildsAReadableDisplayName(string block, string expected)
    {
        // Names must be synthesised: the game's UIData.Name is a family key, so all four
        // atmospheric thrusters would otherwise be called "ThrusterAtmo".
        Assert.Equal(expected, BlockNaming.DisplayNameOf(block));
    }
}

public class OccupiedCellsTableTests
{
    [Theory]
    [InlineData("AtmosphericThruster100", 16)]
    [InlineData("AtmosphericThruster250", 288)]
    [InlineData("HydrogenThruster50", 8)]
    [InlineData("AtmosphericThruster1000", 28876)]
    public void KnownBlocksHaveTheirRecoveredCellCount(string block, int expected) =>
        Assert.Equal(expected, OccupiedCellsTable.For(block));

    [Fact]
    public void UnknownBlockReturnsNullNotZero()
    {
        // A zero would silently corrupt the sizing denominator; an unknown surfaces.
        Assert.Null(OccupiedCellsTable.For("NoSuchBlock"));
    }

    [Fact]
    public void CoversEveryShippedThrusterAndTank() =>
        Assert.Equal(16, OccupiedCellsTable.KnownBlocks.Count);

    [Theory]
    [InlineData("HydrogenTank150", 216, 11, 382.40)]
    [InlineData("HydrogenTank500", 1820, 11, 1534.87)]
    [InlineData("HydrogenTank1250", 36244, 11, 9552.79)]
    public void TankCellCountsReproduceTheirPublishedMass(
        string block, int expectedCells, double modifier, double expectedMass)
    {
        // Tank reference masses are published to two decimals, so this pins the values far more
        // tightly than a whole-kilogram figure would.
        Assert.Equal(expectedCells, OccupiedCellsTable.For(block));

        var computed = modifier * Math.Sqrt(expectedCells) * Math.Log10(expectedCells) + 5.0;

        Assert.True(Math.Abs(computed - expectedMass) < 0.01,
            $"{block}: V={expectedCells} gives {computed:F2} kg, expected {expectedMass:F2}");
    }

    [Fact]
    public void EveryEntryReproducesItsInGameMass()
    {
        // The table and the formula are only meaningful together, so pin them together: each V,
        // run through the mass formula with the thruster density (11) and floor (5), must give
        // the mass the game shows.
        var expected = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["HydrogenThruster50"] = 33,
            ["AtmosphericThruster100"] = 58,
            ["IonThruster100"] = 58,
            ["IonThruster150"] = 290,
            ["HydrogenThruster200"] = 464,
            ["AtmosphericThruster250"] = 464,
            ["HydrogenThruster250"] = 1005,
            ["AtmosphericThruster500"] = 1552,
            ["IonThruster500"] = 1576,
            ["IonThruster750"] = 6188,
            ["HydrogenThruster750"] = 7096,
            ["AtmosphericThruster1000"] = 8343,
        };

        foreach (var (block, mass) in expected)
        {
            var cells = OccupiedCellsTable.For(block);
            Assert.NotNull(cells);

            var computed = 11.0 * Math.Sqrt(cells!.Value) * Math.Log10(cells.Value) + 5.0;

            Assert.True(Math.Abs(computed - mass) < 0.5,
                $"{block}: V={cells} gives {computed:F1} kg, expected ~{mass}");
        }
    }
}
