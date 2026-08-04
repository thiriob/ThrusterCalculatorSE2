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
    /// Identifies which family of block this is. A concrete block and the template it derives from
    /// carry the same slots — in a different order, hence a set — while a different family differs.
    /// That is the link used to recover values a concrete definition inherits rather than restates
    /// (see <see cref="BlockCompositionIndex.InheritedFrom"/>).
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

    /// <summary>
    /// Template composites of the same block family as <paramref name="anchor"/>'s block.
    /// </summary>
    /// <remarks>
    /// Recovers values a concrete definition inherits instead of restating. Hydrogen thrusters, for
    /// instance, omit <c>ThrustClass</c> entirely, yet the engine looks it up with a direct indexer
    /// — so the value must exist, and it comes from the template.
    /// <para>
    /// Matching is by component slot signature, not by name or folder: a template's slots are a
    /// <em>subset</em> of those of every block built from it. Subset rather than equality because
    /// concrete blocks add components of their own — the 5 m and 10 m atmospheric thrusters carry
    /// extras the 2.5 m does not, and exact matching silently lost their density.
    /// </para>
    /// <para>
    /// A loose match is made safe by <see cref="InheritedString"/> requiring unanimity: if several
    /// templates match and disagree, nothing is returned rather than one being picked arbitrarily.
    /// </para>
    /// </remarks>
    public IReadOnlyList<BlockComposition> InheritedFrom(DefinitionFile anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (anchor.Guid is null) return [];

        var signatures = ContainingComponent(anchor.Guid)
            .Where(c => !c.IsTemplate)
            .Select(c => c.SlotSignature)
            .Where(s => s.Count > 0)
            .ToList();

        if (signatures.Count == 0) return [];

        return All
            .Where(c => c.IsTemplate
                        && c.SlotSignature.Count > 0
                        && signatures.Any(c.SlotSignature.IsSubsetOf))
            .OrderByDescending(c => c.SlotSignature.Count)
            .ToList();
    }

    /// <summary>
    /// Reads a field from the template a block inherits it from.
    /// </summary>
    /// <remarks>
    /// Several templates can match, since a generic one's slots are a subset of a specific one's.
    /// Resolution is <b>most specific wins</b> — the template with the most slots — which is the
    /// same rule any inheritance scheme uses, and it beats both alternatives tried first: exact
    /// slot equality missed blocks carrying extra components, while treating all subset matches as
    /// equals produced disagreement and resolved nothing.
    /// <para>
    /// Within the most specific tier, ties must still agree; disagreement there means the match was
    /// genuinely ambiguous and nothing is returned rather than one being picked arbitrarily.
    /// </para>
    /// </remarks>
    public string? InheritedString(DefinitionFile anchor, string componentTypeName, string field)
    {
        var candidates = InheritedFrom(anchor)
            .Select(c => (c.SlotSignature.Count, Value: c.ComponentOfType(componentTypeName)?.GetString(field)))
            .Where(c => c.Value is not null)
            .ToList();

        if (candidates.Count == 0) return null;

        var mostSpecific = candidates.Max(c => c.Count);
        var values = candidates
            .Where(c => c.Count == mostSpecific)
            .Select(c => c.Value!)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return values.Count == 1 ? values[0] : null;
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
