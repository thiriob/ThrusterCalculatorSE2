using ThrusterCalculator.Core.Calculations;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core;

/// <summary>
/// The conditions a ship is being sized for: how hard gravity pulls, and how much air there is.
/// </summary>
public sealed record FlightEnvironment
{
    /// <summary>Surface gravity in m/s².</summary>
    public required double GravityMetresPerSecondSquared { get; init; }

    /// <summary>Air density in [0, 1]. Zero is vacuum or an airless body.</summary>
    public required double AirDensity { get; init; }

    /// <summary>
    /// Confidence in <see cref="GravityMetresPerSecondSquared"/>. Never
    /// <see cref="Provenance.Measured"/> for a planet: gravity is not in the game's definition
    /// files (Research.md §5.3).
    /// </summary>
    public Provenance GravityProvenance { get; init; } = Provenance.Assumed;

    public string? PlanetId { get; init; }

    public string? PlanetName { get; init; }

    /// <summary>Deep space: no gravity to fight, no air to breathe.</summary>
    public static FlightEnvironment Vacuum { get; } = new()
    {
        GravityMetresPerSecondSquared = 0.0,
        AirDensity = 0.0,
        GravityProvenance = Provenance.Measured,
        PlanetName = "Vacuum",
    };

    /// <summary>
    /// Conditions at a given distance above <paramref name="planet"/>, or <c>null</c> when its
    /// surface gravity is unknown and no override is supplied.
    /// </summary>
    /// <param name="gravityOverride">
    /// User-supplied gravity, used in preference to the planet's own value. This is what makes a
    /// newly discovered planet usable the day it ships: the UI offers an editable field rather than
    /// hiding the planet (Design.md §4.5).
    /// </param>
    /// <param name="distanceInRadii">
    /// Distance from the planet's centre in planet radii; 1.0 is the surface. v1 sizes for lift-off,
    /// but the parameter is here so an altitude control needs no new model.
    /// </param>
    public static FlightEnvironment? ForPlanet(
        Planet planet,
        CalculationEngine engine,
        double? gravityOverride = null,
        double distanceInRadii = AtmosphereDensity.SurfaceDistanceInRadii)
    {
        ArgumentNullException.ThrowIfNull(planet);
        ArgumentNullException.ThrowIfNull(engine);

        var gravity = gravityOverride ?? planet.SurfaceGravity;
        if (gravity is null)
        {
            return null;
        }

        return new FlightEnvironment
        {
            GravityMetresPerSecondSquared = gravity.Value,
            AirDensity = engine.AirDensityAt(planet.Atmosphere, distanceInRadii),
            GravityProvenance = gravityOverride is not null
                ? Provenance.Assumed
                : planet.ProvenanceOf("surfaceGravity"),
            PlanetId = planet.Id,
            PlanetName = planet.Name,
        };
    }
}
