namespace ThrusterCalculator.Extraction;

/// <summary>
/// Occupied 25 cm cell counts (<c>V</c>) per block — the one input to block mass that the game does
/// not ship as data.
/// </summary>
/// <remarks>
/// <para>
/// Mass is <c>massCurveModifier * sqrt(V) * log10(V) + minBlockMass</c>. The modifier and floor come
/// from the game's own definitions, but <c>V</c> is voxelized from a block's physics colliders at
/// content-build time and cached in a binary blob we deliberately do not read.
/// </para>
/// <para>
/// These values were recovered by solving that formula against known in-game masses. Every one came
/// out an exact integer, which is what confirms both the formula and the numbers — a wrong formula
/// would not produce twelve integers by chance.
/// </para>
/// <para>
/// <b>This is the only hand-maintained input in an otherwise fully extracted pipeline.</b> It goes
/// stale only if Keen changes a block's <em>collision mesh</em> — far rarer than a stat retune,
/// since the modifier and floor still track automatically. And a stale entry announces itself:
/// computed mass stops matching what the game displays.
/// </para>
/// </remarks>
public static class OccupiedCellsTable
{
    private static readonly Dictionary<string, int> Cells = new(StringComparer.OrdinalIgnoreCase)
    {
        // Block name (as derived from its definition filename) -> V.
        // The comment is the in-game mass each value reproduces, for the density "Mostly Hollow"
        // (modifier 11) and a floor of 5 kg.
        ["HydrogenThruster50"] = 8,          //    33 kg
        ["AtmosphericThruster100"] = 16,     //    58 kg
        ["IonThruster100"] = 16,             //    58 kg
        ["IonThruster150"] = 144,            //   290 kg
        ["HydrogenThruster200"] = 288,       //   464 kg
        ["AtmosphericThruster250"] = 288,    //   464 kg
        ["HydrogenThruster250"] = 936,       // 1 005 kg
        ["AtmosphericThruster500"] = 1852,   // 1 552 kg
        ["IonThruster500"] = 1898,           // 1 576 kg
        ["IonThruster750"] = 17541,          // 6 188 kg
        ["HydrogenThruster750"] = 22031,     // 7 096 kg
        ["AtmosphericThruster1000"] = 28878, // 8 343 kg
    };

    /// <summary>Blocks with a known cell count.</summary>
    public static IReadOnlyCollection<string> KnownBlocks => Cells.Keys;

    /// <summary>
    /// The cell count for a block, or <c>null</c> if unknown.
    /// </summary>
    /// <remarks>
    /// A miss must stay <c>null</c> all the way to the config. Substituting a zero would silently
    /// corrupt the sizing denominator and produce a confident under-count.
    /// </remarks>
    public static int? For(string blockName) =>
        blockName is not null && Cells.TryGetValue(blockName, out var cells) ? cells : null;
}
