using System.Text.Json;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// One parsed <c>.def</c> file — the engine-wide <c>$Bundles</c>/<c>$Type</c>/<c>$Value</c> envelope.
/// </summary>
public sealed record DefinitionFile
{
    /// <summary>Path relative to the content root, using forward slashes.</summary>
    public required string RelativePath { get; init; }

    /// <summary>The raw <c>$Type</c>, e.g. <c>Game2:Keen.Game2.…ThrusterDefinitionObjectBuilder</c>.</summary>
    public required string Type { get; init; }

    /// <summary>Bundle prefix of <see cref="Type"/>, e.g. <c>Game2</c> or <c>VRage</c>.</summary>
    public required string Bundle { get; init; }

    /// <summary>
    /// Trailing type name of <see cref="Type"/>, e.g. <c>ThrusterDefinitionObjectBuilder</c>.
    /// </summary>
    /// <remarks>
    /// This — never the filename — is what dispatch keys off. Hydrogen thrusters live in files
    /// called <c>*_HydrogenThrusterDefinition.def</c> yet carry the ordinary
    /// <c>ThrusterDefinitionObjectBuilder</c> type; filename-based dispatch silently drops a third
    /// of the thruster catalogue (Research.md §3).
    /// </remarks>
    public required string TypeName { get; init; }

    /// <summary>The <c>Guid</c> inside <c>$Value</c>, when present.</summary>
    public string? Guid { get; init; }

    /// <summary>The <c>$Value</c> payload.</summary>
    public required JsonElement Value { get; init; }

    /// <summary>Bundle version stamps, e.g. <c>Game2 -> 2.3.0.2798</c>.</summary>
    public IReadOnlyDictionary<string, string> Bundles { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    /// True for base definitions under <c>Templates/</c>, which concrete blocks inherit from rather
    /// than being placeable blocks themselves.
    /// </summary>
    /// <remarks>
    /// The one place path <em>does</em> carry meaning. Everything else in this data is a GUID graph,
    /// but templates are distinguished only by living under <c>Templates/</c>, and telling them
    /// apart matters: <c>HydrogenThrusterDefinition.def</c> is a template with
    /// <c>ThrustPower: 0</c>, and counting it as a real thruster would both inflate the catalogue
    /// and appear to be a thruster that produces no thrust.
    /// <para>
    /// Templates are also where inherited values live. Hydrogen thrusters omit <c>ThrustClass</c> in
    /// their own definitions and pick up <c>"Hydrogen"</c> from their template — which is how that
    /// question got settled rather than assumed.
    /// </para>
    /// </remarks>
    public bool IsTemplate =>
        RelativePath.StartsWith("Templates/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Reads a string field from <c>$Value</c>, or <c>null</c> if absent or not a string.</summary>
    public string? GetString(string field) =>
        Value.ValueKind == JsonValueKind.Object
        && Value.TryGetProperty(field, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    /// <summary>Reads a numeric field from <c>$Value</c>, or <c>null</c> if absent or not a number.</summary>
    public double? GetDouble(string field) =>
        Value.ValueKind == JsonValueKind.Object
        && Value.TryGetProperty(field, out var property)
        && property.ValueKind == JsonValueKind.Number
            ? property.GetDouble()
            : null;

    /// <summary>Reads a boolean field from <c>$Value</c>, or <c>null</c> if absent or not a boolean.</summary>
    public bool? GetBoolean(string field)
    {
        if (Value.ValueKind != JsonValueKind.Object
            || !Value.TryGetProperty(field, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null,
        };
    }

    /// <summary>Reads an object or array field from <c>$Value</c>, or <c>null</c> if absent.</summary>
    public JsonElement? GetElement(string field) =>
        Value.ValueKind == JsonValueKind.Object
        && Value.TryGetProperty(field, out var property)
            ? property
            : null;
}
