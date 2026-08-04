using System.Collections;
using System.Linq.Expressions;
using System.Reflection;

namespace ThrusterCalculator.Engine;

/// <summary>
/// Lets the engine build placeholders for definition types that are abstract.
/// </summary>
/// <remarks>
/// Deserializing a blueprint outside a running game means there is no definition database, so
/// every definition a block refers to is resolved through the engine's dummy context, which
/// fabricates a placeholder carrying just the GUID. It builds those with
/// <c>FastActivator</c>, which compiles <c>new T()</c> as an expression tree — and that throws
/// "Can't compile a NewExpression with a constructor declared on an abstract class" the moment
/// the reference is declared as an abstract type.
///
/// Weapons are the case that bites: <c>ProjectileWeaponBaseComponentObjectBuilder.Projectile</c>
/// is typed as <c>ProjectileDefinition</c>, which is abstract, so any blueprint carrying a gun
/// failed to load outright while everything else worked.
///
/// The fix is to seed the activator cache before deserializing: for each abstract definition
/// type, an activator that constructs a concrete subclass instead. The substitute is assignable
/// to the field being filled, which is all a placeholder has to be — nothing here reads a
/// definition's contents, and the engine overwrites the GUID immediately afterwards.
/// </remarks>
internal static class AbstractDefinitionActivators
{
    private const string DefinitionType = "Keen.VRage.Library.Definitions.Definition";
    private const string ActivatorType = "Keen.VRage.Library.Utils.FastActivator";

    private const BindingFlags AnyStatic =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;

    private const BindingFlags AnyInstance =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>
    /// Registers a substitute activator for every abstract definition type that has a usable
    /// concrete subclass. Best effort: a failure here only restores the previous behaviour.
    /// </summary>
    public static int Register(Se2Runtime runtime)
    {
        try
        {
            if (runtime.FindType(DefinitionType) is not { } definition
                || runtime.FindType(ActivatorType) is not { } fastActivator)
            {
                return 0;
            }

            if (Cache(fastActivator) is not { } cache)
                return 0;

            // One pass over the definition hierarchy, so the concrete candidates are to hand.
            var all = runtime.DerivedFrom(definition).ToList();
            var concrete = all
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition
                    && t.GetConstructor(Type.EmptyTypes) is not null)
                .ToList();

            var registered = 0;
            foreach (var type in all.Where(t => t.IsAbstract))
            {
                var substitute = concrete.FirstOrDefault(type.IsAssignableFrom);
                if (substitute is null)
                    continue;

                if (Seed(cache, definition, type, substitute))
                    registered++;
            }

            return registered;
        }
        catch (Exception)
        {
            // Reflection into engine internals is version-sensitive by nature. If the shape has
            // changed, blueprints without weapons keep working exactly as before.
            return 0;
        }
    }

    private static IDictionary? Cache(Type fastActivator)
    {
        var instance = fastActivator.GetField("Instance", AnyStatic)?.GetValue(null)
            ?? fastActivator.GetProperty("Instance", AnyStatic)?.GetValue(null);

        if (instance is null)
            return null;

        return fastActivator.GetField("_activatorCache", AnyInstance)?.GetValue(instance)
            as IDictionary;
    }

    private static bool Seed(IDictionary cache, Type definition, Type abstractType, Type substitute)
    {
        // Func<Definition> is the delegate the engine casts the cached entry back to.
        var factory = Expression
            .Lambda(
                typeof(Func<>).MakeGenericType(definition),
                Expression.Convert(Expression.New(substitute), definition))
            .Compile();

        // The key is a (Type, Type) pair and which way round is not documented, so seed both.
        // A key the engine never looks up costs one unread dictionary entry.
        cache[(definition, abstractType)] = factory;
        cache[(abstractType, definition)] = factory;

        return true;
    }
}
