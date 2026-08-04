using System.Reflection;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Hosts Space Engineers 2's own assemblies in this process so the engine's serializer can be
/// driven directly.
/// </summary>
/// <remarks>
/// Process-wide and one-way: game assemblies cannot be unloaded once loaded, and the engine's
/// metadata context is global, so this is a singleton established once and kept.
///
/// Three things here are non-obvious and were all found the hard way against SE2 2.3.0.2798:
///
/// 1. Editor and content-pipeline assemblies must NOT be loaded. The game's
///    MetadataHelper.GetAssembliesWithMetadataDependencies resolves dependencies by *name*
///    via Assembly.Load, and those assemblies name VRage.Plugin.Editor, which the retail build
///    does not ship. One of them in the set makes MetadataManager.PushContext throw, which
///    leaves the serialization indexers unregistered and every later call fails with a
///    confusing KeyNotFoundException for 'SerializationContextServices'. Keen's own shipped
///    VRage.ContentPipeline.Builder.exe fails this way too, so it is a retail packaging gap,
///    not something we are doing wrong.
///
/// 2. BumpAllocator is thread-local. Every thread that serializes must call InitIfRequired
///    first, hence <see cref="PrepareCurrentThread"/>.
///
/// 3. It is a class in 2.3.x. It used to be a ThreadStatic struct, so older reflection code
///    that writes the modified copy back to a static field silently does nothing here.
/// </remarks>
public sealed class Se2Runtime
{
    private static readonly object Gate = new();
    private static Se2Runtime? _current;

    private readonly List<Assembly> _assemblies;
    private readonly Type _bumpAllocator;

    private Se2Runtime(string binPath, List<Assembly> assemblies)
    {
        BinPath = binPath;
        _assemblies = assemblies;
        _bumpAllocator = RequireType("VRage.Library", "Keen.VRage.Library.Memory.BumpAllocator");
    }

    /// <summary>Directory the game assemblies were loaded from.</summary>
    public string BinPath { get; }

    /// <summary>
    /// How many abstract definition types were given a concrete stand-in, so deserialising a
    /// reference to one does not throw. Diagnostic only.
    /// </summary>
    public int SubstitutedDefinitionTypes { get; private set; }

    /// <summary>
    /// Loads the game assemblies, or returns the already-loaded runtime. Never loads twice.
    /// </summary>
    public static Se2Runtime Attach(string gameRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameRoot);

        lock (Gate)
        {
            return _current ??= Create(gameRoot);
        }
    }

    /// <summary>
    /// Prepares the calling thread for serialization. Cheap and idempotent; must be called on
    /// every thread that touches the engine.
    /// </summary>
    public void PrepareCurrentThread()
    {
        const BindingFlags AnyStatic = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        const BindingFlags AnyInstance =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var allocator = _bumpAllocator.GetProperty("Instance", AnyStatic)?.GetValue(null)
            ?? _bumpAllocator.GetField("Instance", AnyStatic)?.GetValue(null)
            ?? throw new Se2EngineException("BumpAllocator.Instance not found.");

        // Initialize() asserts if called twice, so check first. This runs on every engine call,
        // and only the first one per thread actually does anything.
        var initialized = _bumpAllocator.GetProperty("Initialized", AnyInstance)?.GetValue(allocator);
        if (initialized is true)
            return;

        var init = _bumpAllocator.GetMethod("InitIfRequired", AnyInstance)
            ?? _bumpAllocator.GetMethod("Initialize", AnyInstance)
            ?? throw new Se2EngineException("BumpAllocator initialiser not found.");

        init.Invoke(allocator, null);
    }

    /// <summary>Gets a loaded game assembly by simple name.</summary>
    public Assembly RequireAssembly(string simpleName) =>
        _assemblies.FirstOrDefault(a => a.GetName().Name == simpleName)
        ?? throw new Se2EngineException($"Game assembly '{simpleName}' is not loaded.");

    /// <summary>
    /// Finds a type by full name across every loaded game assembly.
    /// </summary>
    /// <remarks>
    /// For the cases where the assembly a type lives in is not known up front — the engine moves
    /// types between assemblies across versions, and a name is the stable part.
    /// </remarks>
    public Type? FindType(string fullTypeName) =>
        _assemblies.Select(a => a.GetType(fullTypeName)).FirstOrDefault(t => t is not null);

    /// <summary>Every type in every loaded game assembly.</summary>
    public IEnumerable<Type> AllTypes()
    {
        foreach (var assembly in _assemblies)
        {
            Type[] types;
            try
            {
                types = assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                // A partially loadable assembly still yields the types that did load.
                types = [.. ex.Types.Where(t => t is not null)!];
            }

            foreach (var type in types)
                yield return type;
        }
    }

    /// <summary>Every loaded game type assignable to <paramref name="baseType"/>.</summary>
    public IEnumerable<Type> DerivedFrom(Type baseType)
    {
        ArgumentNullException.ThrowIfNull(baseType);

        return AllTypes().Where(t => baseType.IsAssignableFrom(t) && t != baseType);
    }

    /// <summary>Gets a type from a loaded game assembly.</summary>
    public Type RequireType(string assemblySimpleName, string fullTypeName) =>
        RequireAssembly(assemblySimpleName).GetType(fullTypeName)
        ?? throw new Se2EngineException(
            $"Type '{fullTypeName}' not found in '{assemblySimpleName}'. "
            + "The game version may have changed.");

    private static Se2Runtime Create(string gameRoot)
    {
        var binPath = Path.Combine(gameRoot, "Game2");
        if (!Directory.Exists(binPath))
            throw new Se2EngineException($"Game binaries not found at '{binPath}'.");

        var loaded = new List<Assembly>();

        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            var name = new AssemblyName(e.Name).Name;
            if (name is null || IsExcluded(name))
                return null;

            // Only ever answer for assemblies that actually ship with the game. This handler is
            // process-wide, so a broader rule hijacks unrelated resolution failures: matching by
            // simple name alone once handed Avalonia a mismatched Avalonia.Base in the GUI and
            // broke it with a TypeLoadException.
            var candidate = Path.Combine(binPath, name + ".dll");
            if (!File.Exists(candidate))
                return null;

            var existing = AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(a => a.GetName().Name == name);

            return existing ?? Assembly.LoadFrom(candidate);
        };

        // Load order matters: these carry the types everything else binds against.
        string[] priority =
        [
            "VRage.Library.dll", "VRage.DCS.dll", "VRage.Core.dll", "VRage.Core.Game.dll",
            "VRage.Game.dll", "Game2.Simulation.dll", "Game2.Game.dll", "Game2.Client.dll",
        ];

        foreach (var dll in priority)
            TryLoad(Path.Combine(binPath, dll), loaded);

        foreach (var dll in Directory.GetFiles(binPath, "*.dll"))
        {
            var file = Path.GetFileName(dll);
            if (priority.Contains(file)) continue;
            if (file.StartsWith("System.", StringComparison.Ordinal)) continue;
            if (file.StartsWith("Microsoft.", StringComparison.Ordinal)) continue;
            if (IsExcluded(Path.GetFileNameWithoutExtension(file))) continue;

            TryLoad(dll, loaded);
        }

        var runtime = new Se2Runtime(binPath, loaded);
        runtime.PushMetadataContext();
        runtime.PrepareCurrentThread();

        // Both must happen before anything deserializes. Without them a blueprint referencing a
        // definition whose declared type is abstract — any weapon does — fails outright.
        EngineDiagnostics.Soften(runtime);
        runtime.SubstitutedDefinitionTypes = AbstractDefinitionActivators.Register(runtime);

        return runtime;
    }

    /// <summary>See the class remarks — loading any of these breaks the metadata context.</summary>
    private static bool IsExcluded(string assemblyName) =>
        assemblyName.Contains(".Editor", StringComparison.OrdinalIgnoreCase)
        || assemblyName.Contains("ContentPipeline", StringComparison.OrdinalIgnoreCase)
        || assemblyName.Contains("ShaderBuilder", StringComparison.OrdinalIgnoreCase)
        || assemblyName.Contains("AutoTests", StringComparison.OrdinalIgnoreCase);

    private static void TryLoad(string path, List<Assembly> into)
    {
        if (!File.Exists(path))
            return;

        try
        {
            var assembly = Assembly.LoadFrom(path);
            if (into.All(a => a.FullName != assembly.FullName))
                into.Add(assembly);
        }
        catch (BadImageFormatException)
        {
            // Native or mixed-mode payloads live alongside the managed ones.
        }
        catch (FileLoadException)
        {
        }
    }

    private void PushMetadataContext()
    {
        var manager = RequireType("VRage.Library", "Keen.VRage.Library.Reflection.MetadataManager");

        var instance = manager.GetProperty(
            "Instance",
            BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)?.GetValue(null)
            ?? throw new Se2EngineException("MetadataManager.Instance not found.");

        var push = manager.GetMethod("PushContext", [typeof(IEnumerable<Assembly>)])
            ?? throw new Se2EngineException("MetadataManager.PushContext not found.");

        try
        {
            push.Invoke(instance, [_assemblies]);
        }
        catch (TargetInvocationException ex)
        {
            throw new Se2EngineException(
                "Failed to initialise the Space Engineers 2 metadata context: "
                + $"{ex.InnerException?.Message}",
                ex.InnerException);
        }
    }
}
