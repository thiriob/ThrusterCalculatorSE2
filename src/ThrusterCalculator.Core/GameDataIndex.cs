using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core;

/// <summary>
/// Id lookups over a <see cref="GameData"/>, so callers resolve cross-references once rather than
/// scanning lists repeatedly.
/// </summary>
/// <remarks>
/// Every lookup returns <c>null</c> for a missing id rather than throwing. A config can legitimately
/// reference nothing (a block with no density, a thruster with no class), and a dangling reference
/// should degrade that one block rather than take down the whole calculation.
/// </remarks>
public sealed class GameDataIndex
{
    private readonly Dictionary<string, Density> _densities;
    private readonly Dictionary<string, Resource> _resources;
    private readonly Dictionary<string, ThrustClass> _thrustClasses;
    private readonly Dictionary<string, Thruster> _thrusters;
    private readonly Dictionary<string, Planet> _planets;

    public GameDataIndex(GameData data)
    {
        ArgumentNullException.ThrowIfNull(data);

        Data = data;
        _densities = ById(data.Densities, d => d.Id);
        _resources = ById(data.Resources, r => r.Id);
        _thrustClasses = ById(data.ThrustClasses, c => c.Id);
        _thrusters = ById(data.Thrusters, t => t.Id);
        _planets = ById(data.Planets, p => p.Id);
    }

    public GameData Data { get; }

    public Density? Density(string? id) => Lookup(_densities, id);

    public Resource? Resource(string? id) => Lookup(_resources, id);

    public ThrustClass? ThrustClass(string? id) => Lookup(_thrustClasses, id);

    public Thruster? Thruster(string? id) => Lookup(_thrusters, id);

    public Planet? Planet(string? id) => Lookup(_planets, id);

    private static Dictionary<string, T> ById<T>(IReadOnlyList<T> items, Func<T, string> keySelector)
    {
        var map = new Dictionary<string, T>(items.Count, StringComparer.Ordinal);
        foreach (var item in items)
        {
            // Last one wins rather than throwing on a duplicate id: a malformed config should still
            // be usable, and the producer records duplicates as warnings.
            map[keySelector(item)] = item;
        }

        return map;
    }

    private static T? Lookup<T>(Dictionary<string, T> map, string? id) where T : class =>
        id is not null && map.TryGetValue(id, out var value) ? value : null;
}
