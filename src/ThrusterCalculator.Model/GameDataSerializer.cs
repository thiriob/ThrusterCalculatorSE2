using System.Text.Json;
using System.Text.Json.Serialization;

namespace ThrusterCalculator.Model;

/// <summary>
/// Reads and writes <c>gamedata.json</c>.
/// </summary>
/// <remarks>
/// Stream-based on purpose, with no filesystem access anywhere in this project: "read from disk" and
/// "fetch over HTTP" then become the same call, which is what keeps a hosted frontend possible
/// without retrofitting (Technic.md §9).
/// </remarks>
public static class GameDataSerializer
{
    /// <summary>
    /// Serializer settings that define the on-the-wire shape.
    /// </summary>
    /// <remarks>
    /// <list type="bullet">
    /// <item>camelCase, matching Schema.md.</item>
    /// <item>Enums as camelCase strings — the file is meant to be read and edited by people.</item>
    /// <item>Comments and trailing commas tolerated on read, because users hand-edit this
    /// (Schema.md R5). Note the shipped fixture uses <c>_comment</c>/<c>_case</c> string fields
    /// rather than real comments, so it stays valid under strict parsers too.</item>
    /// <item>Unknown members ignored, which is what makes additive schema changes safe (R6).</item>
    /// <item>Nulls <em>are</em> written: an explicit <c>null</c> paired with an <c>unknown</c>
    /// provenance is meaningful, and is not the same as an absent field.</item>
    /// </list>
    /// </remarks>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = null,
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    /// <summary>Deserializes and validates the schema version.</summary>
    /// <exception cref="GameDataFormatException">
    /// The JSON is malformed, empty, or written against an incompatible major schema version.
    /// </exception>
    public static GameData Read(Stream utf8Json)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);

        GameData? data;
        try
        {
            data = JsonSerializer.Deserialize<GameData>(utf8Json, Options);
        }
        catch (JsonException ex)
        {
            throw new GameDataFormatException($"gamedata.json could not be parsed: {ex.Message}", ex);
        }

        return Validate(data);
    }

    /// <inheritdoc cref="Read(Stream)"/>
    public static GameData Read(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        GameData? data;
        try
        {
            data = JsonSerializer.Deserialize<GameData>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new GameDataFormatException($"gamedata.json could not be parsed: {ex.Message}", ex);
        }

        return Validate(data);
    }

    public static void Write(Stream utf8Json, GameData data)
    {
        ArgumentNullException.ThrowIfNull(utf8Json);
        ArgumentNullException.ThrowIfNull(data);

        JsonSerializer.Serialize(utf8Json, data, Options);
    }

    public static string WriteToString(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        return JsonSerializer.Serialize(data, Options);
    }

    private static GameData Validate(GameData? data)
    {
        if (data is null)
        {
            throw new GameDataFormatException("gamedata.json is empty or contains only 'null'.");
        }

        if (!SchemaVersion.TryParse(data.SchemaVersion, out var version))
        {
            throw new GameDataFormatException(
                $"Unrecognised schemaVersion '{data.SchemaVersion}'. Expected 'major.minor', "
                + $"for example '{SchemaVersion.Current}'.");
        }

        if (!SchemaVersion.IsReadableByCurrent(version))
        {
            throw new GameDataFormatException(
                $"gamedata.json uses schema version {version}, which this build cannot read "
                + $"(it supports {SchemaVersion.Current.Major}.x). "
                + "Regenerate the config with a matching version of the tc tool.");
        }

        return data;
    }
}
