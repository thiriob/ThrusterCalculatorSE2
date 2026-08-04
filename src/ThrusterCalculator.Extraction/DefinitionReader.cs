using System.Text.Json;

namespace ThrusterCalculator.Extraction;

/// <summary>Parses a single <c>.def</c> document.</summary>
public static class DefinitionReader
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Parses one definition, or returns <c>null</c> with a reason.
    /// </summary>
    /// <remarks>
    /// Never throws on bad input. Across 17k files authored by a dozen different game builds, one
    /// unreadable document must degrade to a warning rather than aborting the run — the failure
    /// worth designing against is a silently incomplete extraction, and that is what
    /// <paramref name="failure"/> plus the per-type counts exist to expose (Technic.md §7.2).
    /// </remarks>
    public static DefinitionFile? TryRead(string relativePath, string json, out string? failure)
    {
        failure = null;

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, DocumentOptions);
        }
        catch (JsonException ex)
        {
            failure = $"malformed JSON: {ex.Message}";
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                failure = $"expected a JSON object at the root, found {root.ValueKind}";
                return null;
            }

            if (!root.TryGetProperty("$Type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String
                || typeElement.GetString() is not { Length: > 0 } type)
            {
                failure = "missing or empty $Type";
                return null;
            }

            if (!root.TryGetProperty("$Value", out var value))
            {
                failure = "missing $Value";
                return null;
            }

            var (bundle, typeName) = SplitType(type);

            return new DefinitionFile
            {
                RelativePath = relativePath,
                Type = type,
                Bundle = bundle,
                TypeName = typeName,
                Guid = value.ValueKind == JsonValueKind.Object
                       && value.TryGetProperty("Guid", out var guid)
                       && guid.ValueKind == JsonValueKind.String
                    ? guid.GetString()
                    : null,
                // Clone detaches the element from the JsonDocument being disposed.
                Value = value.Clone(),
                Bundles = ReadBundles(root),
            };
        }
    }

    /// <summary>
    /// Splits <c>Bundle:Namespace.Qualified.TypeName</c> into its bundle and trailing type name.
    /// </summary>
    public static (string Bundle, string TypeName) SplitType(string type)
    {
        ArgumentNullException.ThrowIfNull(type);

        var colon = type.IndexOf(':', StringComparison.Ordinal);
        var bundle = colon >= 0 ? type[..colon] : string.Empty;
        var qualified = colon >= 0 ? type[(colon + 1)..] : type;

        var lastDot = qualified.LastIndexOf('.');
        var typeName = lastDot >= 0 && lastDot < qualified.Length - 1
            ? qualified[(lastDot + 1)..]
            : qualified;

        return (bundle, typeName);
    }

    private static Dictionary<string, string> ReadBundles(JsonElement root)
    {
        var bundles = new Dictionary<string, string>(StringComparer.Ordinal);

        if (!root.TryGetProperty("$Bundles", out var element)
            || element.ValueKind != JsonValueKind.Object)
        {
            return bundles;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                bundles[property.Name] = property.Value.GetString()!;
            }
        }

        return bundles;
    }
}
