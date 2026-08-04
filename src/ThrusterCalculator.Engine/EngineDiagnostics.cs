using System.Reflection;
using System.Reflection.Emit;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Stops the engine's assertions from killing a read.
/// </summary>
/// <remarks>
/// VRage routes assertion failures through a handler, and the default one throws. That is right
/// for the game, which asserts about state it owns; it is wrong here, because this process drives
/// the serializer deliberately outside a running game and some of those invariants cannot hold.
///
/// The one that stops a blueprint dead is the definition placeholder cache. It is keyed by GUID
/// alone and asserts that a cached placeholder's type equals the type being asked for — which is
/// unsatisfiable once an abstract type has been stood in for, and abstract types are exactly the
/// case the engine cannot build a placeholder for at all. See
/// <see cref="AbstractDefinitionActivators"/>. Between them these two assertions mean the game's
/// own fallback path has never had to serve a weapon outside a loaded definition database.
///
/// The handler is emitted rather than borrowed: the engine ships a recording one, but it expects
/// its owner to hand it its list and dereferences a null one on the first failure. Emitting a
/// no-op keeps the count without depending on internal state we do not set up.
///
/// Assertions are counted, not hidden — <see cref="IgnoredAssertions"/> reports them. Real faults
/// still surface, because real faults here arrive as exceptions rather than assertions.
/// </remarks>
public static class EngineDiagnostics
{
    private const string AssertType = "Keen.VRage.Library.Diagnostics.Assert";
    private const string HandlerInterface = "Keen.VRage.Library.Diagnostics.IDiagnosticHandler";

    private const BindingFlags AnyStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private static int _ignored;

    /// <summary>Assertion failures passed over so far.</summary>
    public static int IgnoredAssertions => Volatile.Read(ref _ignored);

    /// <summary>Called from the emitted handler. Public because emitted IL has no friend access.</summary>
    public static void Record() => Interlocked.Increment(ref _ignored);

    /// <summary>
    /// Installs a handler that records assertion failures instead of throwing. Best effort: if
    /// the engine's shape has changed, behaviour is exactly what it was before.
    /// </summary>
    public static bool Soften(Se2Runtime runtime)
    {
        try
        {
            if (runtime.FindType(AssertType) is not { } assert
                || runtime.FindType(HandlerInterface) is not { } handlerInterface
                || assert.GetField("Handler", AnyStatic) is not { } handlerField)
            {
                return false;
            }

            var handler = Activator.CreateInstance(BuildSilentHandler(handlerInterface));
            if (handler is null)
                return false;

            handlerField.SetValue(null, handler);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Emits a type implementing the handler interface whose every method does nothing.</summary>
    private static Type BuildSilentHandler(Type handlerInterface)
    {
        var assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName("BlueprintHelper.SilentDiagnostics"),
            AssemblyBuilderAccess.Run);

        var type = assembly
            .DefineDynamicModule("Main")
            .DefineType("SilentDiagnosticHandler", TypeAttributes.Public | TypeAttributes.Class);

        type.AddInterfaceImplementation(handlerInterface);

        var record = typeof(EngineDiagnostics).GetMethod(nameof(Record), AnyStatic)!;

        foreach (var method in handlerInterface.GetMethods())
        {
            var parameters = method.GetParameters().Select(p => p.ParameterType).ToArray();

            var implementation = type.DefineMethod(
                method.Name,
                MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.Final
                    | MethodAttributes.HideBySig | MethodAttributes.NewSlot,
                method.ReturnType,
                parameters);

            var il = implementation.GetILGenerator();
            il.Emit(OpCodes.Call, record);

            // Every method on this interface returns void; anything else would need a default.
            if (method.ReturnType != typeof(void))
                il.Emit(OpCodes.Ldnull);

            il.Emit(OpCodes.Ret);

            type.DefineMethodOverride(implementation, method);
        }

        return type.CreateType();
    }
}
