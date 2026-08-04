using ThrusterCalculator.Model;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Reads block occupancy from the game's own <c>contentcache.vrb</c>.
/// </summary>
/// <remarks>
/// The authoritative answer: this is the output of the engine's <c>BlockOccupancyGenerator</c>,
/// the very data <c>ComputeMassAndHP</c> consumes. It covers roughly 1,450 blocks, against the 16
/// whose masses happened to be published, so containers and tanks come along for free.
/// <para>
/// Validated against the independently recovered table: AtmosphericThruster250 → 288 and
/// CargoContainer150 → 216, both exact. Two unrelated derivations agreeing is what makes either
/// trustworthy.
/// </para>
/// </remarks>
public sealed class ContentCacheOccupancySource : IOccupancySource
{
    private readonly ContentCache _cache;
    private readonly IOccupancySource? _fallback;

    private ContentCacheOccupancySource(ContentCache cache, IOccupancySource? fallback)
    {
        _cache = cache;
        _fallback = fallback;
    }

    public string Name => "content-cache";

    /// <summary>Blocks the cache holds occupancy for.</summary>
    public int Coverage => _cache.Count;

    /// <summary>
    /// Hosts the game's assemblies and opens its content cache.
    /// </summary>
    /// <param name="gameRoot">Install root, e.g. <c>…\steamapps\common\SpaceEngineers2</c>.</param>
    /// <param name="contentPath">The <c>GameData\Vanilla\Content</c> directory.</param>
    /// <param name="fallback">Consulted when the cache has no entry for a block.</param>
    /// <exception cref="Se2EngineException">The assemblies or cache could not be loaded.</exception>
    /// <remarks>
    /// Loading game assemblies is process-wide and irreversible, so this must not happen inside a
    /// GUI: SE2 ships its own Avalonia and the versions collide (Technic.md §4). Producer only.
    /// </remarks>
    public static ContentCacheOccupancySource Open(
        string gameRoot, string contentPath, IOccupancySource? fallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        var runtime = Se2Runtime.Attach(gameRoot);
        runtime.PrepareCurrentThread();

        return new ContentCacheOccupancySource(
            ContentCache.ReadForContent(runtime, contentPath), fallback);
    }

    public OccupancyResult OccupiedCells(string blockName, Guid? modelGuid) =>
        modelGuid is { } guid && _cache.TryGetOccupiedCellCount(guid, out var cells)
            // Read straight from the game's own generated data — genuinely measured.
            ? new OccupancyResult(cells, Provenance.Measured)
            : _fallback?.OccupiedCells(blockName, modelGuid) ?? OccupancyResult.Unknown;
}
