using ThrusterCalculator.Engine;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>
/// Dumps a block's occupancy boxes, and what they add up to two different ways.
/// </summary>
/// <remarks>
/// Built for Backlog B2: two large blocks disagree with their in-game mass by about 2%, in opposite
/// directions, while every smaller block matches to within a kilogram. The leading hypothesis was
/// that the generator's cell groups overlap — <c>ComputeMassAndHP</c> sums their volumes, so an
/// overlap would be counted twice and inflate the mass.
/// <para>
/// Rather than eyeball the boxes, this counts the cells <b>both</b> ways: the naive sum the engine
/// (and we) use, and the true union, obtained by enumerating every cell into a set. If they differ,
/// the boxes overlap and by exactly how much. If they agree, the hypothesis is dead and the error
/// is somewhere else — which is worth knowing just as much.
/// </para>
/// </remarks>
internal static class BlockCommand
{
    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var wanted = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        var definitions = DefinitionScanner.Scan(installation);
        var compositions = BlockCompositionIndex.Build(definitions);

        ContentCache cache;
        IDefinitionInheritance inheritance;
        try
        {
            var runtime = Se2Runtime.Attach(installation.RootPath);
            cache = ContentCache.ReadForContent(runtime, installation.ContentPath);

            // Density is almost always inherited, so without the BaseGuid graph every block
            // reports "unresolved" and the command cannot state a mass — which is the number
            // anyone comparing against the game actually wants.
            inheritance = DefinitionSetInheritance.Open(runtime, installation.ContentPath);
        }
        catch (Exception ex) when (ex is Se2EngineException or BadImageFormatException
                                      or FileLoadException or TypeLoadException)
        {
            Console.Error.WriteLine($"tc: could not host the game's assemblies ({ex.Message}).");
            return 1;
        }

        var models = definitions.OfType("BlockModelComponentDefinitionObjectBuilder")
            .Where(d => wanted is null || BlockNaming.BlockNameOf(d.RelativePath)
                .Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.RelativePath, StringComparer.Ordinal)
            .ToList();

        if (models.Count == 0)
        {
            Console.Error.WriteLine($"tc: no block matching '{wanted}'.");
            return 1;
        }

        // Named a block: the full picture. Named nothing: the survey, which is what says whether a
        // pattern seen in two blocks is real or a coincidence of two.
        if (wanted is not null)
        {
            foreach (var model in models) Describe(model, cache, compositions, definitions, inheritance);
            return 0;
        }

        Survey(models, cache);
        return 0;
    }

    /// <summary>
    /// One line per block: how many occupancy groups it has, and whether they overlap.
    /// </summary>
    /// <remarks>
    /// B2 noticed that the two blocks whose mass is wrong both have many occupancy groups while the
    /// blocks that match have one. Two cases is a coincidence until it is counted across the
    /// catalogue, which is what this does.
    /// </remarks>
    private static void Survey(IReadOnlyList<DefinitionFile> models, ContentCache cache)
    {
        Console.WriteLine($"{"block",-40}{"groups",8}{"summed",10}{"union",10}  overlap");

        var multi = 0;
        var overlapping = 0;

        foreach (var model in models)
        {
            var reference = model.GetString("Model");
            if (reference is not { Length: > 0 }) continue;

            var text = reference.StartsWith("{G}", StringComparison.Ordinal) ? reference[3..] : reference;
            if (!Guid.TryParse(text, out var modelGuid)) continue;

            var boxes = cache.CellGroupsOf(modelGuid);
            if (boxes.Count == 0) continue;

            var (summed, union) = Count(boxes);
            if (boxes.Count > 1) multi++;
            if (summed != union) overlapping++;

            Console.WriteLine(
                $"{BlockNaming.BlockNameOf(model.RelativePath),-40}{boxes.Count,8}{summed,10:N0}"
                + $"{union,10:N0}  {(summed == union ? string.Empty : $"{summed - union:N0} double-counted")}");
        }

        Console.WriteLine();
        Console.WriteLine($"{multi} block(s) with more than one group; {overlapping} with overlapping groups.");
    }

    /// <summary>Cells counted the engine's way, and counted as a true union.</summary>
    private static (int Summed, int Union) Count(
        IReadOnlyList<(int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ)> boxes)
    {
        var summed = 0;
        var cells = new HashSet<(int, int, int)>();

        foreach (var (minX, minY, minZ, maxX, maxY, maxZ) in boxes)
        {
            summed += (maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);

            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            for (var z = minZ; z <= maxZ; z++)
            {
                cells.Add((x, y, z));
            }
        }

        return (summed, cells.Count);
    }

    private static void Describe(
        DefinitionFile model, ContentCache cache, BlockCompositionIndex compositions,
        DefinitionSet definitions, IDefinitionInheritance inheritance)
    {
        var blockName = BlockNaming.BlockNameOf(model.RelativePath);
        var reference = model.GetString("Model");

        Console.WriteLine();
        Console.WriteLine($"=== {blockName}");

        if (reference is not { Length: > 0 }
            || !Guid.TryParse(reference.StartsWith("{G}", StringComparison.Ordinal)
                ? reference[3..] : reference, out var modelGuid))
        {
            Console.WriteLine("    no model reference");
            return;
        }

        var boxes = cache.CellGroupsOf(modelGuid);
        if (boxes.Count == 0)
        {
            Console.WriteLine("    no occupancy in the content cache");
            return;
        }

        var summed = 0;
        var cells = new HashSet<(int, int, int)>();

        foreach (var (minX, minY, minZ, maxX, maxY, maxZ) in boxes)
        {
            summed += (maxX - minX + 1) * (maxY - minY + 1) * (maxZ - minZ + 1);

            for (var x = minX; x <= maxX; x++)
            for (var y = minY; y <= maxY; y++)
            for (var z = minZ; z <= maxZ; z++)
            {
                cells.Add((x, y, z));
            }
        }

        if (CommandContext.Flag(Environment.GetCommandLineArgs(), "--raw"))
        {
            Console.WriteLine("    raw record:");
            foreach (var line in cache.DescribeGenerated(modelGuid)) Console.WriteLine("    " + line);
            Console.WriteLine();
        }

        Console.WriteLine($"    groups        {boxes.Count}");
        Console.WriteLine($"    summed cells  {summed:N0}   <- what ComputeMassAndHP counts");
        Console.WriteLine($"    unique cells  {cells.Count:N0}   <- true union");
        Console.WriteLine(summed == cells.Count
            ? "    overlap       none"
            : $"    overlap       {summed - cells.Count:N0} cells counted twice");

        // Mass both ways, so the size of the discrepancy is in kilograms rather than cells.
        var block = FindBlock(model, compositions);
        var density = InheritedDensity(block, definitions, inheritance);

        if (density is null)
        {
            Console.WriteLine("    density       unresolved");
            return;
        }

        var modifier = definitions.Resolve(density)?.GetDouble("MassCurveModifier");
        if (modifier is null)
        {
            Console.WriteLine("    density       no MassCurveModifier");
            return;
        }

        Console.WriteLine($"    modifier      {modifier}");
        Console.WriteLine($"    mass (summed) {Mass(summed, modifier.Value):N2} kg");
        Console.WriteLine($"    mass (union)  {Mass(cells.Count, modifier.Value):N2} kg");
    }

    private static double Mass(int cells, double modifier) =>
        cells <= 0 ? 5.0 : (float)((modifier * Math.Sqrt(cells) * Math.Log10(cells)) + 5.0);

    private static DefinitionFile? FindBlock(DefinitionFile anchor, BlockCompositionIndex compositions)
    {
        string[] types =
        [
            "PowerableBlockDefinitionObjectBuilder",
            "FunctionalBlockDefinitionObjectBuilder",
            "CubeBlockDefinitionObjectBuilder",
            "ArmorBlockDefinitionObjectBuilder",
        ];

        return types.Select(t => compositions.FindSibling(anchor, t)).FirstOrDefault(b => b is not null);
    }

    /// <summary>Walks <c>BaseGuid</c> for the density, exactly as the extractor does.</summary>
    private static string? InheritedDensity(
        DefinitionFile? block, DefinitionSet definitions, IDefinitionInheritance inheritance)
    {
        var current = block;
        var guid = block?.Guid;

        for (var depth = 0; depth < 16 && current is not null; depth++)
        {
            if (current.GetString("Density") is { Length: > 0 } density) return density;

            guid = guid is null ? null : inheritance.BaseOf(guid);
            if (guid is null) return null;

            current = definitions.Resolve(guid);
        }

        return null;
    }
}
