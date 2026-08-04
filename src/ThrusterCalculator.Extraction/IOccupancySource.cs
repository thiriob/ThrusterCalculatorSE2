using ThrusterCalculator.Model;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// Supplies a block's occupied 25 cm cell count — the <c>V</c> of the mass formula.
/// </summary>
/// <remarks>
/// Behind an interface because there are two very different ways to get it, with different costs:
/// a small hand-maintained table that needs nothing, and the game's own precomputed cache, which
/// needs its assemblies hosted. Extraction depends on neither directly, so it stays
/// platform-neutral and testable, and a missing engine degrades one field instead of failing a run.
/// </remarks>
public interface IOccupancySource
{
    /// <summary>Short name for diagnostics, recorded so it is clear which source answered.</summary>
    string Name { get; }

    /// <summary>
    /// Cells occupied by a block, with how the answer was obtained.
    /// </summary>
    /// <param name="blockName">Block name as derived from its definition filename.</param>
    /// <param name="modelGuid">
    /// The block's model asset, when known. The engine-backed source keys on this; the table
    /// ignores it.
    /// </param>
    /// <remarks>
    /// A <c>null</c> count must propagate to the config as an unknown. A zero would silently
    /// corrupt the sizing denominator and produce a confident under-count.
    /// <para>
    /// Provenance is returned per call rather than per source, because a cache-backed source that
    /// falls through to the table for one block should say so for that block only.
    /// </para>
    /// </remarks>
    OccupancyResult OccupiedCells(string blockName, Guid? modelGuid);
}

/// <summary>An occupancy lookup and its confidence.</summary>
public readonly record struct OccupancyResult(int? Cells, Provenance Provenance)
{
    /// <summary>Nothing known — the caller must treat the block's mass as unknown.</summary>
    public static OccupancyResult Unknown { get; } = new(null, Provenance.Unknown);
}

/// <summary>
/// The built-in table of cell counts recovered by solving the mass formula (§4.0).
/// </summary>
/// <remarks>
/// Covers only the blocks whose in-game masses were published. Kept as the fallback for when the
/// game's assemblies cannot be hosted — and as an independent cross-check on the engine-backed
/// source, since the two were derived by completely different routes and agree.
/// </remarks>
public sealed class TableOccupancySource : IOccupancySource
{
    public string Name => "recovered-table";

    public OccupancyResult OccupiedCells(string blockName, Guid? modelGuid) =>
        OccupiedCellsTable.For(blockName) is { } cells
            // Derived, not measured: computed by us from a published mass rather than read from
            // the game.
            ? new OccupancyResult(cells, Provenance.Derived)
            : OccupancyResult.Unknown;
}
