using System.Text.Json;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>
/// Prints a planet's inheritance chain and every component payload reachable from it.
/// </summary>
/// <remarks>
/// The planet data is the most indirect thing in the game files — an info definition points at a
/// prefab, which points at a composite, and any of those may inherit from a base that supplies the
/// fields the concrete file omits. When a value is missing the useful question is not "is it
/// there?" but "where did the walk stop?", and no amount of grepping answers that, because the
/// parent pointers live in <c>definitionsets.vrb</c> rather than the JSON.
/// <para>
/// Built for exactly one question — why do Verdure and Kemik report no surface gravity when
/// Caligo and Palatine state theirs? — in the same spirit as <c>dump-schemas</c>: a reusable
/// instrument beats a throwaway script, because the question recurs every patch.
/// </para>
/// </remarks>
internal static class PlanetCommand
{
    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var wanted = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal));

        var definitions = DefinitionScanner.Scan(installation);
        var (_, inheritance) = CommandContext.OpenEngineSources(
            installation, CommandContext.Flag(args, "--no-engine"));

        var infos = definitions.OfType("PlanetInfoDefinitionObjectBuilder")
            .Where(i => wanted is null
                        || (i.GetString("Name") ?? string.Empty)
                            .Contains(wanted, StringComparison.OrdinalIgnoreCase))
            .OrderBy(i => i.RelativePath, StringComparer.Ordinal)
            .ToList();

        if (infos.Count == 0)
        {
            Console.Error.WriteLine($"tc: no planet matching '{wanted}'.");
            return 1;
        }

        foreach (var info in infos)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {info.GetString("Name") ?? "?"}   ({info.RelativePath})");

            var prefab = definitions.Resolve(info.GetString("Spawn"));
            if (prefab is null)
            {
                Console.WriteLine("    no prefab reachable via Spawn");
                continue;
            }

            Describe("prefab", prefab, definitions, inheritance);

            var composite = definitions.Resolve(CompositeGuidOf(prefab));
            if (composite is not null) Describe("composite", composite, definitions, inheritance);
        }

        return 0;
    }

    /// <summary>Walks one chain, printing each link and the components it contributes.</summary>
    private static void Describe(
        string label, DefinitionFile start, DefinitionSet definitions, IDefinitionInheritance inheritance)
    {
        var current = start;
        var guid = start.Guid;

        for (var depth = 0; depth < 16 && current is not null; depth++)
        {
            Console.WriteLine($"  {label}[{depth}]  {Path.GetFileName(current.RelativePath)}");

            foreach (var (type, payload) in ComponentsOf(current))
            {
                if (!type.Contains("Gravity", StringComparison.Ordinal)
                    && !type.Contains("Atmosphere", StringComparison.Ordinal))
                {
                    continue;
                }

                Console.WriteLine($"        {ShortType(type),-32} {payload}");
            }

            guid = guid is null ? null : inheritance.BaseOf(guid);
            if (guid is null)
            {
                Console.WriteLine($"  {label}[{depth}]  ^ chain ends here");
                break;
            }

            current = definitions.Resolve(guid);
        }
    }

    private static string? CompositeGuidOf(DefinitionFile prefab) =>
        prefab.GetElement("_entity") is { ValueKind: JsonValueKind.Object } entity
        && entity.TryGetProperty("Definition", out var definition)
        && definition.ValueKind == JsonValueKind.String
            ? definition.GetString()
            : null;

    /// <summary>Inline component payloads in a delta-encoded container.</summary>
    private static IEnumerable<(string Type, string Payload)> ComponentsOf(DefinitionFile file)
    {
        foreach (var field in new[] { "Components", "ObjectBuilders" })
        {
            var container = file.GetElement("_entity") is { ValueKind: JsonValueKind.Object } entity
                            && entity.TryGetProperty(field, out var nested)
                ? nested
                : file.GetElement(field);

            // Delta-encoded object, or a plain array of the same entries — both occur.
            var entries = container switch
            {
                { ValueKind: JsonValueKind.Object } o
                    when o.TryGetProperty("Changed", out var changed)
                         && changed.ValueKind == JsonValueKind.Array => changed,
                { ValueKind: JsonValueKind.Array } a => a,
                _ => default,
            };

            if (entries.ValueKind != JsonValueKind.Array) continue;

            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object) continue;

                var payload = entry.TryGetProperty("Value", out var value)
                              && value.ValueKind == JsonValueKind.Object
                    ? value
                    : entry;

                if (payload.TryGetProperty("$Type", out var type)
                    && type.GetString() is { } typeName)
                {
                    yield return (typeName, payload.GetRawText());
                }
            }
        }
    }

    private static string ShortType(string type)
    {
        var at = type.LastIndexOf('.');
        return at >= 0 ? type[(at + 1)..] : type;
    }
}
