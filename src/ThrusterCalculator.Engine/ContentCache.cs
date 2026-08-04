using System.Reflection;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Resolves asset GUIDs to the files that hold them, from the game's <c>contentcache.vrb</c>.
/// </summary>
/// <remarks>
/// Block definitions reference their model as <c>"Model": "{G}ba876647-…"</c>, and that GUID is
/// declared nowhere in the 17,000 <c>.def</c> files. The mapping lives here instead:
/// <c>ContentBlobData.Mapping</c> is an array of resource-handle/file-handle pairs.
///
/// Lookup goes through the engine's own <c>ResourceHandle(Guid)</c> constructor rather than
/// converting the <c>UInt128</c> key back to a GUID ourselves, so nothing depends on guessing
/// the key's byte layout.
/// </remarks>
public sealed class ContentCache
{
    public const string ContentCacheFileName = "contentcache.vrb";
    private const string BlobDataType = "Keen.VRage.Library.Filesystem.ContentCache.ContentBlobData";
    private const string ResourceHandleType = "Keen.VRage.Library.Utils.ResourceHandle";

    private readonly Dictionary<UInt128, string> _pathsByKey;
    private readonly ConstructorInfo _handleFromGuid;
    private readonly FieldInfo _keyField;
    private readonly Dictionary<UInt128, object?> _generatedByKey = [];
    private readonly object? _generatedBlockData;

    private ContentCache(
        Dictionary<UInt128, string> pathsByKey,
        ConstructorInfo handleFromGuid,
        FieldInfo keyField,
        Dictionary<UInt128, object?>? generated = null)
    {
        _pathsByKey = pathsByKey;
        _handleFromGuid = handleFromGuid;
        _keyField = keyField;
        _generatedByKey = generated ?? [];
        _generatedBlockData = generated;
    }

    public int Count => _pathsByKey.Count;

    /// <summary>
    /// Names of the blob types the cache carries, with how many entries each holds.
    /// </summary>
    /// <remarks>
    /// <c>ContentBlobData.BlobData</c> is keyed by CLR type — types such as
    /// <c>ModelOccupancyData</c> are empty marker structs used purely as keys. Listing them
    /// shows what precomputed data the game ships, which is how block cell occupancy was found.
    /// </remarks>
    public IReadOnlyList<string> BlobTypes { get; private init; } = [];

    /// <summary>Reads the content cache that ships alongside the game's definitions.</summary>
    public static ContentCache ReadForContent(Se2Runtime runtime, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        var path = Path.Combine(contentPath, ContentCacheFileName);

        if (!File.Exists(path))
            throw new Se2EngineException($"Content cache not found at '{path}'.");

        return ReadFrom(runtime, path);
    }

    public static ContentCache ReadFrom(Se2Runtime runtime, string contentCachePath)
    {
        var serializer = new VrbSerializer(runtime);
        var blobType = runtime.RequireType("VRage.Library", BlobDataType);

        var blob = serializer.ReadChunkAs(contentCachePath, blobType);

        var mapping = (blobType.GetProperty("Mapping")?.GetValue(blob) as Array)
            ?? throw new Se2EngineException("ContentBlobData.Mapping was not an array.");

        var handleType = runtime.RequireType("VRage.Library", ResourceHandleType);

        var keyField = handleType.GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new Se2EngineException("ResourceHandle._key not found.");

        var fromGuid = handleType.GetConstructors()
            .FirstOrDefault(c =>
                c.GetParameters().Length == 1
                && c.GetParameters()[0].ParameterType.GetElementType() == typeof(Guid))
            ?? throw new Se2EngineException("ResourceHandle(Guid) constructor not found.");

        var paths = new Dictionary<UInt128, string>(mapping.Length);

        foreach (var pair in mapping)
        {
            if (pair is null)
                continue;

            var pairType = pair.GetType();
            var handle = pairType.GetField("ResourceHandle")?.GetValue(pair);
            var file = pairType.GetField("FileHandle")?.GetValue(pair);
            if (handle is null || file is null)
                continue;

            if (keyField.GetValue(handle) is not UInt128 key)
                continue;

            if (file.GetType().GetField("Path")?.GetValue(file) is string filePath
                && filePath.Length > 0)
            {
                paths[key] = filePath;
            }
        }

        var blobTypes = new List<string>();
        if (blobType.GetProperty("BlobData")?.GetValue(blob) is System.Collections.IDictionary map)
        {
            foreach (System.Collections.DictionaryEntry entry in map)
            {
                var name = (entry.Key as Type)?.FullName ?? entry.Key?.ToString() ?? "?";
                var info = entry.Value;

                if (info is null)
                {
                    blobTypes.Add(name);
                    continue;
                }

                // BlobTypeInfo is nested inside ContentBlobData, so describe it by reflection
                // rather than by name.
                var shape = string.Join(
                    ", ",
                    info.GetType()
                        .GetFields(BindingFlags.Public | BindingFlags.Instance)
                        .Select(f => $"{f.Name}:{Describe(f.GetValue(info))}"));

                blobTypes.Add($"{name}  [{shape}]");
            }
        }

        return new ContentCache(paths, fromGuid, keyField, ReadGeneratedBlockData(blobType, blob))
        {
            BlobTypes = blobTypes,
        };
    }

    /// <summary>
    /// Pulls the per-model <c>GeneratedBlockData</c> blobs out, keyed by resource handle.
    /// </summary>
    private static Dictionary<UInt128, object?>? ReadGeneratedBlockData(Type blobType, object blob)
    {
        if (blobType.GetProperty("BlobData")?.GetValue(blob) is not System.Collections.IDictionary map)
            return null;

        foreach (System.Collections.DictionaryEntry entry in map)
        {
            if ((entry.Key as Type)?.Name != "GeneratedBlockData" || entry.Value is null)
                continue;

            if (entry.Value.GetType().GetField("Data")?.GetValue(entry.Value)
                is not System.Collections.IDictionary data)
            {
                continue;
            }

            var byKey = new Dictionary<UInt128, object?>(data.Count);
            foreach (System.Collections.DictionaryEntry item in data)
            {
                // Keys are resource handles; reach past them to the raw UInt128.
                var key = item.Key?.GetType()
                    .GetField("_key", BindingFlags.NonPublic | BindingFlags.Instance)
                    ?.GetValue(item.Key);

                if (key is UInt128 raw)
                    byKey[raw] = item.Value;
            }

            return byKey;
        }

        return null;
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        System.Collections.ICollection c => $"{value.GetType().Name}[{c.Count}]",
        _ => value.GetType().Name,
    };

    /// <summary>Resolves an asset GUID to its file path, relative to a content root.</summary>
    public bool TryGetPath(Guid resourceGuid, out string path)
    {
        var handle = _handleFromGuid.Invoke([resourceGuid]);

        if (_keyField.GetValue(handle) is UInt128 key)
            return _pathsByKey.TryGetValue(key, out path!);

        path = string.Empty;
        return false;
    }

    /// <summary>
    /// The block's occupied cell box, precomputed by the game and cached.
    /// </summary>
    /// <remarks>
    /// This is what the game itself uses for a block's footprint — the output of
    /// <c>BlockOccupancyGenerator</c>, stored per model under the <c>GeneratedBlockData</c> blob
    /// type. Far better than measuring a mesh: it is integer, in cells, and already accounts for
    /// whatever the visual geometry does.
    /// </remarks>
    /// <summary>
    /// The block's occupied cell count — the <c>V</c> of the mass formula.
    /// </summary>
    /// <remarks>
    /// Sums <c>Occupancy.CellGroups</c>, mirroring <c>ComputeMassAndHP</c>, which adds
    /// <c>GetSizeIncludingMax().Volume()</c> over the groups.
    /// <para>
    /// <b>Not the bounding box.</b> The two agree only for a block that is a single box. A 5 m
    /// hydrogen tank occupies 1,820 cells inside a 20×10×10 = 2,000 bounding box, and using the
    /// box overstates its mass by about 6%. That discrepancy is exactly how this was caught: the
    /// box disagreed with the value recovered independently from the tank's published mass, and
    /// the recovered value was right.
    /// </para>
    /// </remarks>
    public bool TryGetOccupiedCellCount(Guid modelGuid, out int cells)
    {
        cells = 0;

        if (!TryGetGenerated(modelGuid, out var generated)) return false;

        var occupancy = generated!.GetType().GetField("Occupancy")?.GetValue(generated);
        if (occupancy?.GetType().GetField("CellGroups")?.GetValue(occupancy)
            is not System.Collections.IEnumerable groups)
        {
            return false;
        }

        var total = 0;
        var any = false;

        foreach (var group in groups)
        {
            if (group is null) continue;

            var min = ReadCell(group, "Min");
            var max = ReadCell(group, "Max");

            // Inclusive of the maximum cell, matching GetSizeIncludingMax().
            var x = max.X - min.X + 1;
            var y = max.Y - min.Y + 1;
            var z = max.Z - min.Z + 1;

            if (x <= 0 || y <= 0 || z <= 0) continue;

            total += x * y * z;
            any = true;
        }

        cells = total;
        return any && total > 0;
    }

    private bool TryGetGenerated(Guid modelGuid, out object? generated)
    {
        generated = null;

        if (_generatedBlockData is null) return false;

        var handle = _handleFromGuid.Invoke([modelGuid]);
        if (_keyField.GetValue(handle) is not UInt128 key) return false;

        return _generatedByKey.TryGetValue(key, out generated) && generated is not null;
    }

    public bool TryGetOccupancy(Guid modelGuid, out (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) cells)
    {
        cells = default;

        if (_generatedBlockData is null)
            return false;

        var handle = _handleFromGuid.Invoke([modelGuid]);
        if (_keyField.GetValue(handle) is not UInt128 key)
            return false;

        if (!_generatedByKey.TryGetValue(key, out var generated) || generated is null)
            return false;

        var occupancy = generated.GetType().GetField("Occupancy")?.GetValue(generated);
        var bounds = occupancy?.GetType().GetField("Bounds")?.GetValue(occupancy);
        if (bounds is null)
            return false;

        var min = ReadCell(bounds, "Min");
        var max = ReadCell(bounds, "Max");
        cells = (min.X, min.Y, min.Z, max.X, max.Y, max.Z);
        return true;
    }

    private static (int X, int Y, int Z) ReadCell(object box, string member)
    {
        var value = box.GetType().GetField(member)?.GetValue(box)
            ?? box.GetType().GetProperty(member)?.GetValue(box);

        if (value is null)
            return (0, 0, 0);

        var type = value.GetType();

        int Component(string name) =>
            Convert.ToInt32(
                type.GetField(name)?.GetValue(value)
                ?? type.GetProperty(name)?.GetValue(value)
                ?? 0);

        return (Component("X"), Component("Y"), Component("Z"));
    }
}
