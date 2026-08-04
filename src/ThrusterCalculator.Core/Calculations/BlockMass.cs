namespace ThrusterCalculator.Core.Calculations;

/// <summary>
/// Block mass, transcribed from the game's own <c>CubeBlockDefinition.ComputeMassAndHP()</c>
/// (Research.md §4.0).
/// </summary>
public static class BlockMass
{
    /// <summary>Model kind implemented here.</summary>
    public const string SqrtLog10CellCountKind = "sqrtLog10CellCount";

    /// <summary>
    /// <c>mass = massCurveModifier * sqrt(V) * log10(V) + minBlockMass</c>, in kilograms.
    /// </summary>
    /// <param name="occupiedCells">
    /// <c>V</c> — the block's occupied 25 cm grid cells.
    /// </param>
    /// <remarks>
    /// Two edge cases are the engine's, not ours, and are deliberately reproduced:
    /// <list type="bullet">
    /// <item>A block with no density definition gets exactly <paramref name="minBlockMass"/>.
    /// Callers signal that by passing <paramref name="occupiedCells"/> &lt;= 0.</item>
    /// <item><c>V == 1</c> also yields exactly <paramref name="minBlockMass"/>, because
    /// <c>log10(1) == 0</c>. That is what the constant is <em>for</em>; it is not an arbitrary
    /// clamp bolted on afterwards.</item>
    /// </list>
    /// The arithmetic runs in <see cref="double"/> and is narrowed to <see cref="float"/> at the
    /// end because the engine stores <c>Mass</c> as a float. Matching that rounding matters when
    /// comparing against masses shown in game.
    /// </remarks>
    public static double SqrtLog10CellCount(int occupiedCells, double massCurveModifier, double minBlockMass)
    {
        if (occupiedCells <= 0)
        {
            return minBlockMass;
        }

        var mass = massCurveModifier * Math.Sqrt(occupiedCells) * Math.Log10(occupiedCells)
                   + minBlockMass;

        return (float)mass;
    }
}
