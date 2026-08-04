using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

/// <summary>Hand-built configs for precise cases. Values are ours, not the game's.</summary>
internal static class TestData
{
    public const double MinBlockMass = 5.0;

    /// <summary>The modifier the game gives every thruster ("Mostly Hollow").</summary>
    public const double MostlyHollow = 11.0;

    public static CalculationModels Models { get; } = new()
    {
        BlockMass = new BlockMassModel { Kind = "sqrtLog10CellCount", MinBlockMass = MinBlockMass },
        ThrustEffectiveness = new ThrustEffectivenessModel { Kind = "linearRampAirDensity" },
        AtmosphereDensity = new AtmosphereDensityModel { Kind = "linearRampAltitude" },
    };

    public static ThrustClass Atmospheric { get; } = new()
    {
        Id = "atmospheric", MinThrustAirDensity = 0.2, MaxThrustAirDensity = 0.8,
    };

    /// <summary>Note the inverted endpoints: full thrust at LOW density.</summary>
    public static ThrustClass Ion { get; } = new()
    {
        Id = "ion", MinThrustAirDensity = 0.8, MaxThrustAirDensity = 0.2,
    };

    public static ThrustClass Hydrogen { get; } = new()
    {
        Id = "hydrogen", MinThrustAirDensity = -1, MaxThrustAirDensity = 0,
    };

    public static ThrustClass WaterOnly { get; } = new()
    {
        Id = "water", MinThrustAirDensity = -1, MaxThrustAirDensity = 0, WaterOnly = true,
    };

    public static GameData Build(params Thruster[] thrusters) => new()
    {
        SchemaVersion = "1.0",
        Generator = new GeneratorInfo
        {
            Tool = "test", Version = "0", ExtractedAt = DateTimeOffset.UnixEpoch,
        },
        Source = new SourceInfo { GameBuild = "test", Fingerprint = "test" },
        Models = Models,
        Densities = [new Density { Id = "mostlyHollow", Name = "Mostly Hollow", MassCurveModifier = MostlyHollow }],
        ThrustClasses = [Atmospheric, Ion, Hydrogen, WaterOnly],
        Thrusters = thrusters,
    };

    public static Thruster Thruster(
        string id,
        double? thrustNewtons,
        int? occupiedCells,
        string? thrustClass = "atmospheric",
        string? density = "mostlyHollow",
        bool implemented = true) => new()
        {
            Id = id,
            Name = id,
            ThrustClass = thrustClass,
            SizeCm = 100,
            ThrustNewtons = thrustNewtons,
            Density = density,
            OccupiedCells = occupiedCells,
            Implemented = implemented,
        };

    public static FlightEnvironment Surface(double gravity, double airDensity = 1.0) => new()
    {
        GravityMetresPerSecondSquared = gravity,
        AirDensity = airDensity,
        GravityProvenance = Provenance.Assumed,
    };
}
