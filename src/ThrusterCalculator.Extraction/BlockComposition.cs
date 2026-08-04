using System.Text.Json;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// A block's <c>EntityCompositeDefinition</c> and the component definitions it pulls together.
/// </summary>
/// <remarks>
/// This is what makes a "block" a thing at all. The game has no single definition per block: a
/// thruster's thrust, its mass category, its recipe and its model each live in a separate file, and
/// the composite is the only thing that says they belong to one another.
/// </remarks>
public sealed record BlockComposition
{
    public required string CompositeGuid { get; init; }

    public required string RelativePath { get; init; }

    /// <summary>Component definitions the composite references, resolved through the GUID index.</summary>
    public required IReadOnlyList<DefinitionFile> Components { get; init; }

    /// <summary>GUIDs referenced by the composite, including any that failed to resolve.</summary>
    public required IReadOnlyList<string> ReferencedGuids { get; init; }

    /// <summary>
    /// The composite's component slot GUIDs, as an order-independent set.
    /// </summary>
    /// <remarks>
    /// Descriptive only. It was once used to guess a block's template by slot overlap; that guess
    /// produced silently wrong densities and was replaced by the game's own <c>BaseGuid</c> pointer
    /// (see <see cref="IDefinitionInheritance"/>).
    /// </remarks>
    public required IReadOnlySet<string> SlotSignature { get; init; }

    public bool IsTemplate =>
        RelativePath.StartsWith("Templates/", StringComparison.OrdinalIgnoreCase);

    /// <summary>The first component of a given type, or <c>null</c>.</summary>
    public DefinitionFile? ComponentOfType(string typeName) =>
        Components.FirstOrDefault(c => string.Equals(c.TypeName, typeName, StringComparison.Ordinal));
}

/// <summary>
/// Indexes every block composite so components can be traced back to the block they belong to.
/// </summary>
public sealed class BlockCompositionIndex
{
    public const string CompositeTypeName = "EntityCompositeDefinitionObjectBuilder";

    private readonly Dictionary<string, List<BlockComposition>> _byComponentGuid;

    private BlockCompositionIndex(
        IReadOnlyList<BlockComposition> compositions,
        Dictionary<string, List<BlockComposition>> byComponentGuid)
    {
        All = compositions;
        _byComponentGuid = byComponentGuid;
    }

    public IReadOnlyList<BlockComposition> All { get; }

    public static BlockCompositionIndex Build(DefinitionSet definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        var compositions = new List<BlockComposition>();
        var byComponentGuid = new Dictionary<string, List<BlockComposition>>(StringComparer.OrdinalIgnoreCase);

        foreach (var composite in definitions.OfType(CompositeTypeName))
        {
            var guids = ReadComponentGuids(composite);
            if (guids.Count == 0) continue;

            var composition = new BlockComposition
            {
                CompositeGuid = composite.Guid ?? composite.RelativePath,
                RelativePath = composite.RelativePath,
                ReferencedGuids = guids,
                SlotSignature = ReadSlotSignature(composite),
                Components = guids
                    .Select(definitions.Resolve)
                    .Where(d => d is not null)
                    .Select(d => d!)
                    .ToList(),
            };

            compositions.Add(composition);

            foreach (var guid in guids)
            {
                if (!byComponentGuid.TryGetValue(guid, out var list))
                {
                    byComponentGuid[guid] = list = [];
                }

                list.Add(composition);
            }
        }

        return new BlockCompositionIndex(compositions, byComponentGuid);
    }

    /// <summary>
    /// Every composite referencing a component definition.
    /// </summary>
    /// <remarks>
    /// Normally two — a block has a client and a server composite, both listing the same component
    /// definitions.
    /// </remarks>
    public IReadOnlyList<BlockComposition> ContainingComponent(string? guid) =>
        guid is not null && _byComponentGuid.TryGetValue(guid, out var list) ? list : [];

    /// <summary>
    /// The definition of <paramref name="siblingTypeName"/> belonging to the same block as
    /// <paramref name="anchor"/>, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// This is the join between a block's scattered definitions — thrust in one file, mass category
    /// and display name in another, with neither naming the other.
    /// <para>
    /// It uses the engine's own mechanism rather than a heuristic: the composite is how the game
    /// itself decides which components make up an entity, so this is as durable as the data format.
    /// Verified against the shipped data — all 14 thruster definitions resolve to exactly one
    /// <c>PowerableBlockDefinitionObjectBuilder</c>.
    /// </para>
    /// <para>
    /// There is deliberately <b>no fallback</b> to matching by directory. It would work on today's
    /// layout, but a weaker method silently substituting for this one would mask the very breakage
    /// worth knowing about, and could mispair outright if two blocks ever shared a folder. A miss
    /// returns <c>null</c> and becomes a recorded warning instead.
    /// </para>
    /// </remarks>
    public DefinitionFile? FindSibling(DefinitionFile anchor, string siblingTypeName)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (anchor.Guid is null) return null;

        // A block normally has two composites (client and server) listing the same components,
        // so the first match is as good as any.
        foreach (var composition in ContainingComponent(anchor.Guid))
        {
            if (composition.ComponentOfType(siblingTypeName) is { } sibling)
            {
                return sibling;
            }
        }

        return null;
    }

    private static HashSet<string> ReadSlotSignature(DefinitionFile composite)
    {
        var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (composite.GetElement("Components") is { ValueKind: JsonValueKind.Object } components
            && components.TryGetProperty("Keys", out var keys)
            && keys.ValueKind == JsonValueKind.Array)
        {
            foreach (var key in keys.EnumerateArray())
            {
                if (key.ValueKind == JsonValueKind.String && key.GetString() is { Length: > 0 } guid)
                {
                    slots.Add(guid);
                }
            }
        }

        return slots;
    }

    /// <summary>
    /// Pulls component definition GUIDs out of a composite.
    /// </summary>
    /// <remarks>
    /// Composites are delta-encoded, but deliberately only shallowly decoded here: the GUIDs sit
    /// inline in <c>Components.Changed[].Value.Definition</c>, so scanning that array gets what we
    /// need without reimplementing the engine's inheritance semantics. A plain (non-delta) array is
    /// also handled, since nothing guarantees every composite is encoded the same way.
    /// </remarks>
    private static List<string> ReadComponentGuids(DefinitionFile composite)
    {
        var guids = new List<string>();

        if (composite.GetElement("Components") is not { } components)
        {
            return guids;
        }

        switch (components.ValueKind)
        {
            case JsonValueKind.Object:
                if (components.TryGetProperty("Changed", out var changed)
                    && changed.ValueKind == JsonValueKind.Array)
                {
                    foreach (var entry in changed.EnumerateArray())
                    {
                        AddDefinition(entry.ValueKind == JsonValueKind.Object
                                      && entry.TryGetProperty("Value", out var value)
                            ? value
                            : entry);
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var entry in components.EnumerateArray())
                {
                    AddDefinition(entry);
                }

                break;
        }

        return guids;

        void AddDefinition(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object
                && element.TryGetProperty("Definition", out var definition)
                && definition.ValueKind == JsonValueKind.String
                && definition.GetString() is { Length: > 0 } guid)
            {
                guids.Add(guid);
            }
        }
    }
}
