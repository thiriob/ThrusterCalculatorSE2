using System.IO;
using System.Numerics;
using System.Reflection;
using System.Text;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Converts VRage binary archives (<c>.vrb</c>) to and from the engine's own JSON form.
/// </summary>
/// <remarks>
/// The engine does all the real work; this only reaches the right methods by reflection.
/// Archives are chunked and self-describing: <see cref="ListChunks"/> shows the names, and
/// reading with the wrong type produces an error naming the correct one.
/// </remarks>
public sealed class VrbSerializer
{
    /// <summary>The single chunk a blueprint grid archive contains.</summary>
    public const string MainChunk = "[Main]";

    /// <summary>
    /// Root type stored in a blueprint's <c>grid.json.vrb</c>. It is a plain EntityBundle —
    /// the same type a savegame uses. BlueprintEntityBundle is only the in-memory wrapper the
    /// game authors with and is never what gets written.
    /// </summary>
    public const string GridRootType = "Keen.VRage.Core.Game.Systems.EntityBundle";

    public const string GridRootAssembly = "VRage.Core.Game";

    private readonly Se2Runtime _runtime;
    private readonly Assembly _vrage;

    public VrbSerializer(Se2Runtime runtime)
    {
        _runtime = runtime;
        _vrage = runtime.RequireAssembly("VRage.Library");
    }

    /// <summary>Chunk names present in an archive. Useful when a file is an unknown shape.</summary>
    public IReadOnlyList<string> ListChunks(string vrbPath)
    {
        _runtime.PrepareCurrentThread();

        using var stream = File.OpenRead(vrbPath);
        using var reader = CreateReader(stream, Path.GetFileName(vrbPath));

        var chunks = (System.Collections.IEnumerable)ReaderType
            .GetMethod("GetChunks")!
            .Invoke(reader, null)!;

        return [.. chunks.Cast<object>().Select(c => c.ToString() ?? string.Empty)];
    }

    /// <summary>Reads a blueprint's grid archive and returns the engine's JSON representation.</summary>
    public string ReadGridAsJson(string vrbPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vrbPath);

        if (!File.Exists(vrbPath))
            throw new Se2EngineException($"Archive not found: '{vrbPath}'.");

        _runtime.PrepareCurrentThread();

        var targetType = _runtime.RequireType(GridRootAssembly, GridRootType);

        object graph;
        using (var stream = File.OpenRead(vrbPath))
        using (var reader = CreateReader(stream, Path.GetFileName(vrbPath)))
        {
            var readChunk = ReaderType.GetMethod("ReadChunk")
                ?? throw new Se2EngineException("BinaryArchiveReader.ReadChunk not found.");

            try
            {
                graph = readChunk.MakeGenericMethod(targetType).Invoke(reader, [MainChunk])
                    ?? throw new Se2EngineException($"Chunk '{MainChunk}' was empty.");
            }
            catch (TargetInvocationException ex)
            {
                throw new Se2EngineException(
                    $"Could not read '{Path.GetFileName(vrbPath)}': {ex.InnerException?.Message}",
                    ex.InnerException);
            }
        }

        return ToJson(graph);
    }

    /// <summary>
    /// Writes engine JSON back out as a <c>.vrb</c> archive.
    /// </summary>
    /// <param name="json">Engine JSON, normally an edited version of what came out of a read.</param>
    /// <param name="outputPath">File to create.</param>
    /// <param name="compression">Archive compression: <c>None</c>, <c>ZLib</c> or <c>Brotli</c>.</param>
    public void WriteGridFromJson(string json, string outputPath, string compression = "Brotli")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        _runtime.PrepareCurrentThread();

        var targetType = _runtime.RequireType(GridRootAssembly, GridRootType);
        var graph = FromJson(json, targetType);

        var writerType = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.Binary.BinaryArchiveWriter");
        var compressionType = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.Binary.Archive.CompressionType");

        if (!Enum.TryParse(compressionType, compression, ignoreCase: true, out var compressionValue))
        {
            throw new Se2EngineException(
                $"Unknown compression '{compression}'. Valid: "
                + string.Join(", ", Enum.GetNames(compressionType)));
        }

        var directory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        using (var stream = File.Create(outputPath))
        {
            var context = CreateContext(stream, Path.GetFileName(outputPath), forJson: false);
            using var writer = (IDisposable)Activator.CreateInstance(writerType, [context, false])!;

            // AddMainChunk<T>(ref T value, CompressionType) — the by-ref parameter is fine
            // through reflection, the argument array carries it.
            var addMainChunk = writerType.GetMethod("AddMainChunk")
                ?? throw new Se2EngineException("BinaryArchiveWriter.AddMainChunk not found.");

            try
            {
                addMainChunk
                    .MakeGenericMethod(targetType)
                    .Invoke(writer, [graph, compressionValue!]);
            }
            catch (TargetInvocationException ex)
            {
                throw new Se2EngineException(
                    $"Could not write '{outputPath}': {ex.InnerException?.Message}",
                    ex.InnerException);
            }
        }
    }

    private object FromJson(string json, Type targetType)
    {
        var helper = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.SerializationHelper");
        var formatType = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.SerializerFormat");

        using var buffer = new MemoryStream(Encoding.UTF8.GetBytes(json));
        var context = CreateContext(buffer, "json", forJson: true);

        var deserialize = helper.GetMethods()
            .First(m => m.Name == "Deserialize"
                && m.IsGenericMethod
                && m.GetParameters().Length == 2
                && m.GetParameters()[1].ParameterType == formatType);

        try
        {
            return deserialize
                .MakeGenericMethod(targetType)
                .Invoke(null, [context, Enum.Parse(formatType, "Json")])
                ?? throw new Se2EngineException("Deserializing the JSON produced nothing.");
        }
        catch (TargetInvocationException ex)
        {
            throw new Se2EngineException(
                $"Could not read the edited JSON back: {ex.InnerException?.Message}",
                ex.InnerException);
        }
        finally
        {
            (context as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Reads the archive's main chunk as an arbitrary engine type.
    /// </summary>
    /// <remarks>
    /// Grids, armour models and the content cache all live in the same container and differ only
    /// by chunk type, so callers that know the type can reach it without a bespoke method.
    /// </remarks>
    public object ReadChunkAs(string archivePath, Type targetType, string? chunkName = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(archivePath);
        ArgumentNullException.ThrowIfNull(targetType);

        chunkName ??= MainChunk;

        if (!File.Exists(archivePath))
            throw new Se2EngineException($"Archive not found: '{archivePath}'.");

        _runtime.PrepareCurrentThread();

        using var stream = File.OpenRead(archivePath);
        using var reader = CreateReader(stream, Path.GetFileName(archivePath));

        var readChunk = ReaderType.GetMethod("ReadChunk")!.MakeGenericMethod(targetType);

        try
        {
            return readChunk.Invoke(reader, [chunkName])
                ?? throw new Se2EngineException($"Chunk '{chunkName}' was empty.");
        }
        catch (TargetInvocationException ex)
        {
            throw new Se2EngineException(
                $"Could not read '{Path.GetFileName(archivePath)}': {ex.InnerException?.Message}",
                ex.InnerException);
        }
    }

    /// <summary>Root type stored in an <c>.armblock</c> armour model archive.</summary>
    public const string ArmorModelType = "Keen.VRage.Game.Armor.Data.ArmorBlockModel";

    public const string ArmorModelAssembly = "VRage.Game";

    /// <summary>
    /// Reads the integer bounding box out of an <c>.armblock</c> model.
    /// </summary>
    /// <remarks>
    /// The model carries <c>BoundingBoxI</c> directly, so block extents come out without
    /// touching vertex data — which is what makes real block sizes cheap rather than a mesh
    /// decoding project.
    /// </remarks>
    public (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) ReadArmorBlockBounds(
        string armblockPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(armblockPath);

        _runtime.PrepareCurrentThread();

        var targetType = _runtime.RequireType(ArmorModelAssembly, ArmorModelType);

        using var stream = File.OpenRead(armblockPath);
        using var reader = CreateReader(stream, Path.GetFileName(armblockPath));

        var readChunk = ReaderType.GetMethod("ReadChunk")!.MakeGenericMethod(targetType);

        object model;
        try
        {
            model = readChunk.Invoke(reader, [MainChunk])
                ?? throw new Se2EngineException("Armour model chunk was empty.");
        }
        catch (TargetInvocationException ex)
        {
            throw new Se2EngineException(
                $"Could not read '{Path.GetFileName(armblockPath)}': {ex.InnerException?.Message}",
                ex.InnerException);
        }

        var box = targetType.GetField("BoundingBox")?.GetValue(model)
            ?? throw new Se2EngineException("ArmorBlockModel.BoundingBox not found.");

        var min = ReadIntVector(box, "Min");
        var max = ReadIntVector(box, "Max");

        return (min.X, min.Y, min.Z, max.X, max.Y, max.Z);
    }

    /// <summary>Root type stored in a <c>.vrm</c> model archive's main chunk.</summary>
    public const string ModelDataType = "Keen.VRage.Core.Model.Data.ModelData";

    public const string ModelDataAssembly = "VRage.Core";

    /// <summary>
    /// Reads the float bounding box out of a <c>.vrm</c> model, used by non-armour blocks.
    /// </summary>
    /// <remarks>
    /// Only the <c>[Main]</c> chunk is touched. Vertex and mesh data live in their own chunks —
    /// a single model can be a megabyte — so the header alone is cheap to read.
    /// </remarks>
    public (Vector3 Min, Vector3 Max) ReadModelBounds(string modelPath)
    {
        var targetType = _runtime.RequireType(ModelDataAssembly, ModelDataType);
        var model = ReadChunkAs(modelPath, targetType);

        var box = targetType.GetField("BoundingBox")?.GetValue(model)
            ?? throw new Se2EngineException("ModelData.BoundingBox not found.");

        return (ReadFloatVector(box, "Min"), ReadFloatVector(box, "Max"));
    }

    /// <summary>Triangle geometry for one level of detail of a model.</summary>
    public sealed record ModelGeometry(Vector3[] Vertices, int[] TriangleIndices)
    {
        public int TriangleCount => TriangleIndices.Length / 3;
    }

    /// <summary>
    /// Reads one LOD's triangle geometry out of a <c>.vrm</c> model.
    /// </summary>
    /// <remarks>
    /// The engine decodes this for us — <c>VertexData</c> exposes <c>Vector3[] Vertices</c> and
    /// <c>Int32[] TriangleIndices</c> outright, so no vertex format has to be reverse
    /// engineered. LODs live in numbered chunks, <c>Vertex_0</c> being the most detailed.
    /// </remarks>
    public ModelGeometry ReadModelGeometry(string modelPath, int lod = 0)
    {
        var targetType = _runtime.RequireType(
            ModelDataAssembly, "Keen.VRage.Core.Model.Data.VertexData");

        var data = ReadChunkAs(modelPath, targetType, $"Vertex_{lod}");

        // The array elements are the engine's own Vector3, not System.Numerics.Vector3, so a
        // direct cast silently yields nothing. Convert element by element instead.
        var raw = targetType.GetField("Vertices")?.GetValue(data) as Array;
        var vertices = new Vector3[raw?.Length ?? 0];

        for (var i = 0; i < vertices.Length; i++)
        {
            var value = raw!.GetValue(i);
            if (value is null)
                continue;

            var type = value.GetType();
            vertices[i] = new Vector3(
                Convert.ToSingle(type.GetField("X")?.GetValue(value) ?? 0f),
                Convert.ToSingle(type.GetField("Y")?.GetValue(value) ?? 0f),
                Convert.ToSingle(type.GetField("Z")?.GetValue(value) ?? 0f));
        }

        var indices = targetType.GetField("TriangleIndices")?.GetValue(data) as int[] ?? [];

        return new ModelGeometry(vertices, indices);
    }

    /// <summary>Number of LOD levels present, found by probing the chunk names.</summary>
    public int CountLods(string modelPath)
    {
        var chunks = ListChunks(modelPath);
        return chunks.Count(c => c.StartsWith("Vertex_", StringComparison.Ordinal));
    }

    private static Vector3 ReadFloatVector(object box, string memberName)
    {
        var boxType = box.GetType();
        var value = boxType.GetField(memberName)?.GetValue(box)
            ?? boxType.GetProperty(memberName)?.GetValue(box)
            ?? throw new Se2EngineException($"BoundingBox has no '{memberName}'.");

        var type = value.GetType();

        float Component(string name) =>
            Convert.ToSingle(
                type.GetField(name)?.GetValue(value)
                ?? type.GetProperty(name)?.GetValue(value)
                ?? 0f);

        return new Vector3(Component("X"), Component("Y"), Component("Z"));
    }

    /// <summary>One entry from an armour model's shape catalogue.</summary>
    public sealed record ArmorShapeInfo(
        string Topology,
        (int MinX, int MinY, int MinZ, int MaxX, int MaxY, int MaxZ) Bounds,
        int VertexCount,
        int[] VertexCounts,
        Vector3[] Vertices);

    /// <summary>
    /// Describes the shapes inside an <c>.armblock</c> model.
    /// </summary>
    /// <remarks>
    /// Armour geometry is procedural rather than a mesh: each shape is either a plain cuboid or
    /// a set of convex pieces given as vertices plus per-group counts. Reading the catalogue is
    /// how we find out which, and how the groups are meant to be interpreted.
    /// </remarks>
    public IReadOnlyList<ArmorShapeInfo> ReadArmorShapes(string armblockPath)
    {
        _runtime.PrepareCurrentThread();

        var modelType = _runtime.RequireType(ArmorModelAssembly, ArmorModelType);
        var model = ReadChunkAs(armblockPath, modelType);

        var catalogue = modelType.GetField("ShapeCatalog")?.GetValue(model)
            as System.Collections.IDictionary;

        if (catalogue is null)
            return [];

        // The catalogue holds every cut and damage variant; Shape names the intact one.
        var baseShape = modelType.GetField("Shape")?.GetValue(model);

        var shapes = new List<ArmorShapeInfo>();

        foreach (System.Collections.DictionaryEntry entry in catalogue)
        {
            if (entry.Value is not { } shape)
                continue;

            if (baseShape is not null && !Equals(entry.Key, baseShape))
                continue;

            var shapeType = shape.GetType();
            var topology = shapeType.GetField("Topology")?.GetValue(shape)?.ToString() ?? "?";

            var box = shapeType.GetField("BoundingBox")?.GetValue(shape);
            var bounds = box is null
                ? default
                : (ReadIntVector(box, "Min"), ReadIntVector(box, "Max")) is var (min, max)
                    ? (min.X, min.Y, min.Z, max.X, max.Y, max.Z)
                    : default;

            var convex = shapeType.GetField("CustomConvexShapes")?.GetValue(shape);
            var (vertices, counts) = ReadConvexShapes(convex);

            shapes.Add(new ArmorShapeInfo(topology, bounds, vertices.Length, counts, vertices));
        }

        return shapes;
    }

    /// <summary>
    /// Reads a shape's convex pieces: the vertex cloud plus how many vertices form each piece.
    /// </summary>
    private static (Vector3[] Vertices, int[] Counts) ReadConvexShapes(object? convexShapes)
    {
        if (convexShapes is null)
            return ([], []);

        // Boxing a Nullable<ConvexShapes> already unwraps it, so the runtime type is the value.
        var type = convexShapes.GetType();

        var rawVertices = type.GetField("Vertices")?.GetValue(convexShapes)
            as System.Collections.IEnumerable;
        var rawCounts = type.GetField("VertexCounts")?.GetValue(convexShapes)
            as System.Collections.IEnumerable;

        var vertices = rawVertices?
            .Cast<object>()
            .Select(v =>
            {
                var t = v.GetType();
                return new Vector3(
                    Convert.ToSingle(t.GetField("X")?.GetValue(v) ?? 0f),
                    Convert.ToSingle(t.GetField("Y")?.GetValue(v) ?? 0f),
                    Convert.ToSingle(t.GetField("Z")?.GetValue(v) ?? 0f));
            })
            .ToArray() ?? [];

        var counts = rawCounts?.Cast<object>().Select(Convert.ToInt32).ToArray() ?? [];

        return (vertices, counts);
    }

    private static (int X, int Y, int Z) ReadIntVector(object box, string memberName)
    {
        var boxType = box.GetType();
        var value = boxType.GetField(memberName)?.GetValue(box)
            ?? boxType.GetProperty(memberName)?.GetValue(box)
            ?? throw new Se2EngineException($"BoundingBoxI has no '{memberName}'.");

        var type = value.GetType();

        int Component(string name) =>
            Convert.ToInt32(
                type.GetField(name)?.GetValue(value)
                ?? type.GetProperty(name)?.GetValue(value)
                ?? 0);

        return (Component("X"), Component("Y"), Component("Z"));
    }

    private Type ReaderType =>
        _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.Binary.BinaryArchiveReader");

    private IDisposable CreateReader(Stream stream, string debugName)
    {
        // The Stream-only constructor builds a bare context, but EntityBundle needs
        // IEntityProxySerializationContext registered, so the context is supplied explicitly.
        var context = CreateContext(stream, debugName, forJson: false);

        return (IDisposable)Activator.CreateInstance(ReaderType, [context, false])!;
    }

    private string ToJson(object graph)
    {
        var helper = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.SerializationHelper");
        var formatType = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.SerializerFormat");

        using var buffer = new MemoryStream();
        var context = CreateContext(buffer, "json", forJson: true);

        var serialize = helper.GetMethods()
            .First(m => m.Name == "Serialize" && m.IsGenericMethod && m.GetParameters().Length == 3);

        serialize
            .MakeGenericMethod(graph.GetType())
            .Invoke(null, [context, graph, Enum.Parse(formatType, "Json")]);

        // Disposing flushes the engine's buffered writer into our stream.
        (context as IDisposable)?.Dispose();

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private object CreateContext(Stream stream, string debugName, bool forJson)
    {
        var contextType = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.SerializationContext");
        var customType = _runtime.RequireType("VRage.Library",
            "Keen.VRage.Library.Serialization.CustomSerializationContext");

        var customs = new List<object>();

        if (forJson)
        {
            // 'true' selects the archive format, which emits $Type/$Bundles envelopes.
            var jsonParams = _runtime.RequireType("VRage.Library",
                "Keen.VRage.Library.Serialization.Json.JsonSerializationParameters");
            customs.Add(Activator.CreateInstance(jsonParams, [true])!);
        }

        var proxy = _runtime.RequireAssembly("VRage.DCS")
            .GetType("Keen.VRage.DCS.Serialization.EntityProxySerializationContext");
        if (proxy is not null)
            customs.Add(Activator.CreateInstance(proxy)!);

        // Definitions are not loaded, so entity definitions stay as raw GUIDs. That is fine —
        // the blueprint records definition GUIDs and DebugNames, which is enough to identify
        // blocks without resolving the full definition graph.
        var dummyDefinitions = _vrage.GetType(
            "Keen.VRage.Library.Definitions.Internal.DummyDefinitionSerializationContext");
        if (dummyDefinitions is not null)
            customs.Add(Activator.CreateInstance(dummyDefinitions)!);

        var typed = Array.CreateInstance(customType, customs.Count);
        for (var i = 0; i < customs.Count; i++)
            typed.SetValue(customs[i], i);

        return Activator.CreateInstance(contextType, [stream, debugName, typed])!;
    }
}
