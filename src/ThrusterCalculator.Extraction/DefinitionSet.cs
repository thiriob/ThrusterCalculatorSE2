using ThrusterCalculator.Model;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// Every definition read from an installation, indexed for lookup.
/// </summary>
/// <remarks>
/// The game's data is a GUID-keyed graph rather than a file hierarchy: nothing references anything
/// by name or path (Research.md §2.2). Folder layout is a convenience for humans, so lookups go
/// through <see cref="ByGuid"/> and <see cref="OfType"/>, not through paths.
/// </remarks>
public sealed class DefinitionSet
{
    private readonly Dictionary<string, DefinitionFile> _byGuid;
    private readonly Dictionary<string, List<DefinitionFile>> _byTypeName;

    internal DefinitionSet(
        IReadOnlyList<DefinitionFile> definitions,
        IReadOnlyList<ExtractionWarning> warnings,
        int filesSeen)
    {
        All = definitions;
        Warnings = warnings;
        FilesSeen = filesSeen;

        _byGuid = new Dictionary<string, DefinitionFile>(StringComparer.OrdinalIgnoreCase);
        _byTypeName = new Dictionary<string, List<DefinitionFile>>(StringComparer.Ordinal);

        foreach (var definition in definitions)
        {
            if (definition.Guid is { Length: > 0 } guid)
            {
                _byGuid[guid] = definition;
            }

            if (!_byTypeName.TryGetValue(definition.TypeName, out var list))
            {
                _byTypeName[definition.TypeName] = list = [];
            }

            list.Add(definition);
        }
    }

    /// <summary>Everything that parsed, in scan order.</summary>
    public IReadOnlyList<DefinitionFile> All { get; }

    /// <summary>Files that could not be read, and why.</summary>
    public IReadOnlyList<ExtractionWarning> Warnings { get; }

    /// <summary><c>.def</c> files encountered, including those that failed to parse.</summary>
    public int FilesSeen { get; }

    /// <summary>Definitions holding a GUID, keyed by it.</summary>
    public IReadOnlyDictionary<string, DefinitionFile> ByGuid => _byGuid;

    /// <summary>Resolves a GUID reference, or <c>null</c> if it dangles.</summary>
    public DefinitionFile? Resolve(string? guid) =>
        guid is not null && _byGuid.TryGetValue(guid, out var definition) ? definition : null;

    /// <summary>Every definition of a given trailing type name, e.g. <c>ThrusterDefinitionObjectBuilder</c>.</summary>
    public IReadOnlyList<DefinitionFile> OfType(string typeName) =>
        _byTypeName.TryGetValue(typeName, out var list) ? list : [];

    /// <summary>
    /// How many definitions of each <c>$Type</c> were found.
    /// </summary>
    /// <remarks>
    /// Recorded into the config so a post-patch drop — say twelve thrusters becoming eight — is
    /// visible rather than silent. This is the cheapest defence against the failure mode that
    /// actually matters: a confidently wrong answer computed from incomplete data.
    /// </remarks>
    public IReadOnlyDictionary<string, int> CountsByType() =>
        _byTypeName.ToDictionary(pair => pair.Key, pair => pair.Value.Count, StringComparer.Ordinal);

    /// <summary>
    /// Highest bundle version stamp seen, as a rough build indicator.
    /// </summary>
    /// <remarks>
    /// Only ever approximate: Keen stamps each file with whichever build last touched it, so one
    /// installation contains a dozen different versions (Research.md §2.3).
    /// </remarks>
    public string? MaxBundleVersion()
    {
        Version? best = null;
        string? bestText = null;

        foreach (var definition in All)
        {
            foreach (var (_, text) in definition.Bundles)
            {
                if (Version.TryParse(text, out var version) && (best is null || version > best))
                {
                    best = version;
                    bestText = text;
                }
            }
        }

        return bestText;
    }
}
