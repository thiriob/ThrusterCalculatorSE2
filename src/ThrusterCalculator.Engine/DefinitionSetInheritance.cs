using System.Collections;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Reads the definition inheritance graph out of the game's <c>definitionsets.vrb</c>.
/// </summary>
/// <remarks>
/// Each entry is a <c>DefinitionLoadingData</c> carrying, among other things, a <c>BaseGuid</c> —
/// the definition it derives from. That is the authoritative parent link, and it exists nowhere in
/// the <c>.def</c> files.
/// <para>
/// Verified on SE2 2.3.0.2798: <c>CargoContainer150</c>'s block definition states no density and
/// its base chain reaches <c>CargoContainersFunctionalBlockDefinition</c>, which states
/// <c>Hollow (7)</c> — matching the value independently derived from the container's published mass.
/// </para>
/// </remarks>
public sealed class DefinitionSetInheritance : IDefinitionInheritance
{
    private const string CollectionType = "Keen.VRage.Library.Definitions.DefinitionSetCollection";
    public const string DefinitionSetsFileName = "definitionsets.vrb";

    private readonly Dictionary<string, string> _baseByGuid;

    private DefinitionSetInheritance(Dictionary<string, string> baseByGuid) =>
        _baseByGuid = baseByGuid;

    public string Name => "definition-sets";

    /// <summary>Definitions that declare a parent.</summary>
    public int Count => _baseByGuid.Count;

    /// <summary>Definitions seen, whether or not they declare a parent.</summary>
    public int TotalDefinitions { get; private init; }

    /// <exception cref="Se2EngineException">The file is missing or cannot be read.</exception>
    public static DefinitionSetInheritance Open(Se2Runtime runtime, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        var path = Path.Combine(contentPath, DefinitionSetsFileName);
        if (!File.Exists(path))
        {
            throw new Se2EngineException($"Definition sets not found at '{path}'.");
        }

        var serializer = new VrbSerializer(runtime);
        var collectionType = runtime.RequireType("VRage.Library", CollectionType);
        var collection = serializer.ReadChunkAs(path, collectionType);

        if (collectionType.GetProperty("DefinitionSets")?.GetValue(collection) is not IDictionary sets)
        {
            throw new Se2EngineException("DefinitionSetCollection.DefinitionSets was not a dictionary.");
        }

        var bases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var total = 0;

        foreach (DictionaryEntry entry in sets)
        {
            if (entry.Value?.GetType().GetProperty("Definitions")?.GetValue(entry.Value)
                is not IEnumerable definitions)
            {
                continue;
            }

            foreach (var pair in definitions)
            {
                var data = pair?.GetType().GetProperty("Value")?.GetValue(pair);
                if (data is null) continue;

                var type = data.GetType();
                if (type.GetProperty("Guid")?.GetValue(data) is not Guid guid) continue;

                total++;

                if (type.GetProperty("BaseGuid")?.GetValue(data) is Guid baseGuid
                    && baseGuid != Guid.Empty)
                {
                    bases[guid.ToString()] = baseGuid.ToString();
                }
            }
        }

        return new DefinitionSetInheritance(bases) { TotalDefinitions = total };
    }

    public string? BaseOf(string guid) =>
        guid is not null && _baseByGuid.TryGetValue(guid, out var parent) ? parent : null;
}
