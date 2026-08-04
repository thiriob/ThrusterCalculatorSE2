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
/// <b>No longer the primary source.</b> <c>ContentCacheOccupancySource</c> reads these counts
/// straight from the game's own generated data, covering ~1,450 blocks against the 16 here. This
/// table is the fallback for when the game's assemblies cannot be hosted.
/// </para>
/// <para>
/// It also served as the independent check that caught a real bug: the first cache implementation
/// read the occupancy <em>bounding box</em> rather than summing its cell groups, which overstated
/// the 5 m hydrogen tank by 10% (2,000 cells against 1,820). The two derivations disagreeing is
/// what exposed it.
/// </para>
/// <para>
/// Three entries were corrected from the cache — Ion 7.5 m, Hydrogen 7.5 m and Atmospheric 10 m
/// differed by 1–2 cells. All three are the largest blocks, where solving from a mass published to
/// the whole kilogram simply cannot resolve individual cells; both values round to the same
/// displayed mass.
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
        ["IonThruster750"] = 17543,          // 6 188 kg
        ["HydrogenThruster750"] = 22030,     // 7 096 kg
        ["AtmosphericThruster1000"] = 28876, // 8 343 kg

        // Tanks — density "Mostly Hollow" (11), confirmed from the extracted config rather than
        // assumed. Reference masses are published to two decimals, and each V below reproduces its
        // mass to that precision, which is a tighter fit than the thrusters above.
        ["HydrogenTank150"] = 216,           //   382.40 kg   (= 6³, a full 1.5 m cube)
        ["OxygenTank150"] = 216,             //   382.40 kg   same shell as the hydrogen 1.5 m
        ["HydrogenTank500"] = 1820,          // 1 534.87 kg
        ["HydrogenTank1250"] = 36244,        // 9 552.79 kg
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
