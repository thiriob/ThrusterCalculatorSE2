namespace ThrusterCalculator.Model;

/// <summary>One of the game's four shared block-density definitions.</summary>
public sealed record Density
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Ships as 7 (Hollow), 11 (Mostly Hollow), 20 (Mostly Solid) or 35 (Solid).</summary>
    public required double MassCurveModifier { get; init; }
}

/// <summary>A resource a block consumes or stores. Four ship: electricity, hydrogen, oxygen, water.</summary>
public sealed record Resource
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>e.g. <c>Kilowatts</c>. Verbatim from the game — not normalised.</summary>
    public required string FlowRateUnits { get; init; }

    /// <summary>e.g. <c>KilowattHours</c>. Verbatim from the game — not normalised.</summary>
    public required string StorageUnits { get; init; }

    public bool RequiresConveyors { get; init; }
}

/// <summary>
/// Environmental effectiveness for a family of thrusters, from the game's global
/// <c>ThrustClassesConfiguration</c> (Research.md §3.3).
/// </summary>
public sealed record ThrustClass
{
    public required string Id { get; init; }

    /// <summary>Air density at which the class produces <em>maximum</em> thrust.</summary>
    /// <remarks>
    /// ⚠ This may be numerically <em>less</em> than <see cref="MinThrustAirDensity"/> — that is how
    /// ion thrusters are expressed (full thrust at low density). The names describe which end of the
    /// ramp is which, not an ordering. Interpolate across the interval either way; assuming
    /// <c>min &lt; max</c> silently inverts ion thrusters.
    /// </remarks>
    public required double MaxThrustAirDensity { get; init; }

    /// <summary>Air density at which the class produces <em>zero</em> thrust.</summary>
    /// <remarks>
    /// <c>-1</c> is a sentinel meaning "no falloff at all" — effectiveness is always 1.0. Hydrogen
    /// thrusters use it.
    /// </remarks>
    public required double MinThrustAirDensity { get; init; }

    public double WaterSubmersionTolerance { get; init; } = 1.0;

    /// <summary>Only functions submerged. Excluded from every non-submerged calculation.</summary>
    public bool WaterOnly { get; init; }
}

/// <summary>A thruster block.</summary>
public sealed record Thruster : ProvenanceAware
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// References a <see cref="ThrustClass.Id"/>. <c>null</c> is legitimate — hydrogen thrusters
    /// omit the class in the game data (Research.md §3), so consumers must handle it rather than
    /// assuming a string.
    /// </summary>
    public string? ThrustClass { get; init; }

    /// <summary>Block size in whole centimetres (50…1000). Integer by settled decision (Schema.md §8).</summary>
    public required int SizeCm { get; init; }

    /// <summary>Maximum thrust in newtons. <c>null</c> when <see cref="Implemented"/> is false.</summary>
    public double? ThrustNewtons { get; init; }

    public ConsumedResource? ConsumedResource { get; init; }

    /// <summary>References a <see cref="Density.Id"/>.</summary>
    public string? Density { get; init; }

    /// <summary>
    /// Occupied 25 cm grid cells — the <c>V</c> of the mass formula (Research.md §4.0).
    /// </summary>
    /// <remarks>
    /// Not present in the game's JSON: it is voxelized from physics colliders and cached in a binary
    /// blob, so these are recovered by solving the mass formula against known in-game masses and
    /// carry <see cref="Provenance.Derived"/>.
    /// <para>
    /// When <c>null</c>, consumers must report mass as unknown. Substituting zero would silently
    /// corrupt the self-weight solver, whose denominator depends on thruster mass.
    /// </para>
    /// </remarks>
    public int? OccupiedCells { get; init; }

    /// <summary>
    /// False for blocks that ship art but no definition — underwater thrusters today. They stay in
    /// the config so the UI can say "not in this build" rather than pretending they do not exist.
    /// </summary>
    public bool Implemented { get; init; } = true;
}

/// <summary>What a block draws while operating.</summary>
public sealed record ConsumedResource
{
    /// <summary>References a <see cref="Model.Resource.Id"/>.</summary>
    public required string Resource { get; init; }

    /// <summary>
    /// Rate at full thrust, in the referenced resource's <see cref="Model.Resource.FlowRateUnits"/>.
    /// </summary>
    /// <remarks>
    /// Not comparable across thrust classes: electricity-burning and hydrogen-burning thrusters
    /// report in different units (Research.md §3).
    /// </remarks>
    public required double RatePerThrust { get; init; }
}

/// <summary>A cargo container block.</summary>
public sealed record Container : ProvenanceAware
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>
    /// Cargo <em>capacity</em> in kg, read straight from the game. Distinct from the block's own
    /// mass, which is computed from <see cref="OccupiedCells"/> — both are needed, since load
    /// presets scale the former while the sizing solver uses the latter.
    /// </summary>
    public required double MaxMassKg { get; init; }

    /// <summary>References a <see cref="Model.Density.Id"/>.</summary>
    public string? Density { get; init; }

    /// <inheritdoc cref="Thruster.OccupiedCells"/>
    public int? OccupiedCells { get; init; }
}

/// <summary>A gas tank block.</summary>
public sealed record Tank : ProvenanceAware
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>References a <see cref="Model.Resource.Id"/>.</summary>
    public string? Resource { get; init; }

    /// <summary>
    /// Capacity in the referenced resource's <see cref="Model.Resource.StorageUnits"/>.
    /// </summary>
    /// <remarks>
    /// Converting a full tank to kilograms needs a mass-per-unit for the gas that the game data does
    /// not yet give us, so consumers report tank contents as unknown rather than guessing
    /// (Research.md §8).
    /// </remarks>
    public required double MaxCapacity { get; init; }

    public double? MaxDischargeRate { get; init; }

    /// <summary>References a <see cref="Model.Density.Id"/>.</summary>
    public string? Density { get; init; }

    /// <inheritdoc cref="Thruster.OccupiedCells"/>
    public int? OccupiedCells { get; init; }
}
