using System.Text.Json.Serialization;

namespace ThrusterCalculator.Model;

/// <summary>
/// Root of <c>gamedata.json</c> — the whole contract between producer and consumer (Schema.md).
/// </summary>
public sealed record GameData
{
    /// <summary><c>major.minor</c>. See <see cref="SchemaVersion"/>.</summary>
    public required string SchemaVersion { get; init; }

    public required GeneratorInfo Generator { get; init; }

    public required SourceInfo Source { get; init; }

    /// <summary>Named calculation models and their parameters (Schema.md §3).</summary>
    public required CalculationModels Models { get; init; }

    public IReadOnlyList<Density> Densities { get; init; } = [];

    public IReadOnlyList<Resource> Resources { get; init; } = [];

    public IReadOnlyList<ThrustClass> ThrustClasses { get; init; } = [];

    public IReadOnlyList<Thruster> Thrusters { get; init; } = [];

    public IReadOnlyList<Container> Containers { get; init; } = [];

    public IReadOnlyList<Tank> Tanks { get; init; } = [];

    public IReadOnlyList<Planet> Planets { get; init; } = [];

    /// <summary>
    /// Non-fatal problems hit during extraction. Extraction never throws on bad input — it records
    /// and continues — so this is what makes a degraded extraction visible rather than silent
    /// (Schema.md §6).
    /// </summary>
    public IReadOnlyList<ExtractionWarning> Warnings { get; init; } = [];
}

/// <summary>What produced this file.</summary>
public sealed record GeneratorInfo
{
    public required string Tool { get; init; }

    public required string Version { get; init; }

    public required DateTimeOffset ExtractedAt { get; init; }
}

/// <summary>What it was produced from.</summary>
public sealed record SourceInfo
{
    /// <summary>
    /// Highest <c>$Bundles</c> version seen across the scanned files. A rough build indicator only:
    /// Keen stamps each definition with whichever build last touched it (Research.md §2.3).
    /// </summary>
    public required string GameBuild { get; init; }

    /// <summary>
    /// Hash over <c>(relative path, size, mtime)</c> of every scanned <c>.def</c> — metadata only,
    /// so checking it is a directory enumeration rather than 17k file reads. This is what makes the
    /// staleness banner cheap enough to be honest (Technic.md §3.3).
    /// </summary>
    public required string Fingerprint { get; init; }

    /// <summary>
    /// Definitions seen per <c>$Type</c>. The blunt defence against silent data loss: "12 thrusters
    /// found" makes a post-patch drop to 8 visible instead of invisible (Schema.md §6).
    /// </summary>
    [JsonPropertyName("definitionCounts")]
    public IReadOnlyDictionary<string, int> DefinitionCounts { get; init; } =
        new Dictionary<string, int>();
}

/// <summary>A non-fatal problem recorded during extraction.</summary>
public sealed record ExtractionWarning
{
    /// <summary>Stable machine-readable slug, e.g. <c>unknownThrustClass</c>.</summary>
    public required string Code { get; init; }

    public required string Detail { get; init; }

    /// <summary>Source file, relative to the game's content root, when one applies.</summary>
    public string? File { get; init; }
}
