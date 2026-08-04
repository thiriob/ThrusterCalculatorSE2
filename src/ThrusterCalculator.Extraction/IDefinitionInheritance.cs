namespace ThrusterCalculator.Extraction;

/// <summary>
/// Resolves a definition's parent, so fields it does not restate can be inherited.
/// </summary>
/// <remarks>
/// Concrete blocks routinely omit fields: seven of twelve thrusters do not state their
/// <c>Density</c>, hydrogen thrusters do not state their <c>ThrustClass</c>, and no cargo container
/// states either.
/// <para>
/// <b>The parent pointer is not in the <c>.def</c> files.</b> It lives in <c>definitionsets.vrb</c>
/// as <c>DefinitionLoadingData.BaseGuid</c>, which is why inspecting the JSON alone never found it
/// and why two successive attempts to infer it from component-slot signatures went wrong — the
/// second silently, by producing confident but incorrect densities (Research.md §4.4).
/// </para>
/// <para>
/// Behind an interface so extraction stays engine-free and testable: without the engine, inheritance
/// simply does not resolve and the affected fields are reported unknown, rather than guessed.
/// </para>
/// </remarks>
public interface IDefinitionInheritance
{
    /// <summary>Short name for diagnostics.</summary>
    string Name { get; }

    /// <summary>The definition this one derives from, or <c>null</c> at the root.</summary>
    string? BaseOf(string guid);
}

/// <summary>
/// Resolves nothing. The honest default when the game's assemblies cannot be hosted.
/// </summary>
/// <remarks>
/// Deliberately a null object rather than a heuristic. An unresolved field announces itself as a
/// warning; a guessed one does not.
/// </remarks>
public sealed class NoDefinitionInheritance : IDefinitionInheritance
{
    public string Name => "none";

    public string? BaseOf(string guid) => null;
}
