using ThrusterCalculator.Core.Calculations;

namespace ThrusterCalculator.Core.Tests;

public class BlockMassTests
{
    /// <summary>
    /// The transcription check that matters: every thruster the game ships, with the cell counts
    /// recovered in Research.md §4.0, must reproduce its real in-game mass.
    /// </summary>
    /// <remarks>
    /// These are the actual values — not invented test data — so a regression here means our
    /// formula has drifted from the engine's. Tolerance is 0.5 kg because the reference masses are
    /// published rounded to the kilogram.
    /// </remarks>
    [Theory]
    [InlineData("HydrogenThruster50", 8, 33)]
    [InlineData("AtmosphericThruster100", 16, 58)]
    [InlineData("IonThruster100", 16, 58)]
    [InlineData("IonThruster150", 144, 290)]
    [InlineData("AtmosphericThruster250", 288, 464)]
    [InlineData("HydrogenThruster200", 288, 464)]
    [InlineData("HydrogenThruster250", 936, 1005)]
    [InlineData("AtmosphericThruster500", 1852, 1552)]
    [InlineData("IonThruster500", 1898, 1576)]
    [InlineData("IonThruster750", 17543, 6188)]
    [InlineData("HydrogenThruster750", 22030, 7096)]
    [InlineData("AtmosphericThruster1000", 28876, 8343)]
    public void ReproducesRealThrusterMasses(string name, int occupiedCells, double expectedKg)
    {
        var mass = BlockMass.SqrtLog10CellCount(
            occupiedCells, TestData.MostlyHollow, TestData.MinBlockMass);

        Assert.True(Math.Abs(mass - expectedKg) < 0.5,
            $"{name}: expected ~{expectedKg} kg, got {mass:F2} kg");
    }

    [Fact]
    public void SingleCellBlockWeighsExactlyTheFloor()
    {
        // log10(1) == 0, so the formula collapses to minBlockMass. That is what the constant is
        // for; it is not a clamp applied afterwards.
        var mass = BlockMass.SqrtLog10CellCount(1, TestData.MostlyHollow, TestData.MinBlockMass);

        Assert.Equal(TestData.MinBlockMass, mass);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveCellCountWeighsTheFloor(int occupiedCells)
    {
        // Mirrors the engine's "no density definition" branch.
        var mass = BlockMass.SqrtLog10CellCount(
            occupiedCells, TestData.MostlyHollow, TestData.MinBlockMass);

        Assert.Equal(TestData.MinBlockMass, mass);
    }

    [Fact]
    public void ScalesLinearlyWithTheDensityModifier()
    {
        var hollow = BlockMass.SqrtLog10CellCount(1000, 7, 0);
        var solid = BlockMass.SqrtLog10CellCount(1000, 35, 0);

        // Tolerance rather than exact equality: the result is deliberately narrowed to float
        // precision to match the engine, so the ratio is not exact to double precision.
        Assert.Equal(5.0, solid / hollow, 5);
    }

    [Fact]
    public void IsNarrowedToFloatPrecision()
    {
        // The engine stores Mass as a float; matching that rounding keeps our numbers comparable
        // with what the game displays.
        var mass = BlockMass.SqrtLog10CellCount(288, TestData.MostlyHollow, TestData.MinBlockMass);

        Assert.Equal((double)(float)mass, mass);
    }

    [Fact]
    public void IncreasesMonotonicallyWithSize()
    {
        var previous = double.NegativeInfinity;
        foreach (var cells in new[] { 2, 8, 16, 144, 288, 936, 28876 })
        {
            var mass = BlockMass.SqrtLog10CellCount(cells, TestData.MostlyHollow, TestData.MinBlockMass);
            Assert.True(mass > previous, $"mass should increase with cells; broke at {cells}");
            previous = mass;
        }
    }
}
