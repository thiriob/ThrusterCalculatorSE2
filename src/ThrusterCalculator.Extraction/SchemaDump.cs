using System.Text.Json;

namespace ThrusterCalculator.Extraction;

/// <summary>A field observed on a <c>$Type</c>, and how consistently it appears.</summary>
public sealed record FieldSummary
{
    public required string Name { get; init; }

    /// <summary>Definitions of this type that carry the field.</summary>
    public required int Occurrences { get; init; }

    /// <summary>JSON kinds seen, e.g. <c>Number</c>, or <c>String, Null</c> for a nullable field.</summary>
    public required IReadOnlyList<string> Kinds { get; init; }

    /// <summary>True when every definition of the type has it. Optional fields are the interesting ones.</summary>
    public required bool AlwaysPresent { get; init; }

    /// <summary>A sample value, to make the dump readable at a glance.</summary>
    public string? Example { get; init; }
}

/// <summary>Every field seen across all definitions sharing a <c>$Type</c>.</summary>
public sealed record TypeSchema
{
    public required string TypeName { get; init; }

    public required string FullType { get; init; }

    public required int Count { get; init; }

    public required IReadOnlyList<FieldSummary> Fields { get; init; }

    /// <summary>One example file, for going and looking at the real thing.</summary>
    public required string ExampleFile { get; init; }
}

/// <summary>
/// Summarises the shape of every definition type in an installation.
/// </summary>
/// <remarks>
/// This is the project's first tool for a reason. Run against a real install it answers, in one
/// pass, which types exist, which fields are optional, and which are new — so it doubles as the
/// patch-diffing tool: dump before and after a game update and diff the output (Technic.md §8).
/// </remarks>
public static class SchemaDump
{
    private const int MaxExampleLength = 60;

    /// <summary>Describes each type, ordered by descending frequency then name.</summary>
    public static IReadOnlyList<TypeSchema> Describe(DefinitionSet definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        return definitions.All
            .GroupBy(d => d.TypeName, StringComparer.Ordinal)
            .Select(DescribeGroup)
            .OrderByDescending(schema => schema.Count)
            .ThenBy(schema => schema.TypeName, StringComparer.Ordinal)
            .ToList();
    }

    private static TypeSchema DescribeGroup(IGrouping<string, DefinitionFile> group)
    {
        var members = group.ToList();
        var fields = new Dictionary<string, FieldAccumulator>(StringComparer.Ordinal);

        foreach (var definition in members)
        {
            if (definition.Value.ValueKind != JsonValueKind.Object) continue;

            foreach (var property in definition.Value.EnumerateObject())
            {
                if (!fields.TryGetValue(property.Name, out var accumulator))
                {
                    fields[property.Name] = accumulator = new FieldAccumulator();
                }

                accumulator.Add(property.Value);
            }
        }

        return new TypeSchema
        {
            TypeName = group.Key,
            FullType = members[0].Type,
            Count = members.Count,
            ExampleFile = members[0].RelativePath,
            Fields = fields
                .OrderByDescending(pair => pair.Value.Occurrences)
                .ThenBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new FieldSummary
                {
                    Name = pair.Key,
                    Occurrences = pair.Value.Occurrences,
                    Kinds = pair.Value.KindNames(),
                    AlwaysPresent = pair.Value.Occurrences == members.Count,
                    Example = pair.Value.Example,
                })
                .ToList(),
        };
    }

    private sealed class FieldAccumulator
    {
        private readonly SortedSet<string> _kinds = new(StringComparer.Ordinal);

        public int Occurrences { get; private set; }

        public string? Example { get; private set; }

        public void Add(JsonElement value)
        {
            Occurrences++;
            _kinds.Add(value.ValueKind.ToString());

            if (Example is not null || value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                return;
            }

            var text = value.ToString();
            Example = text.Length > MaxExampleLength
                ? string.Concat(text.AsSpan(0, MaxExampleLength), "…")
                : text;
        }

        public IReadOnlyList<string> KindNames() => [.. _kinds];
    }
}
