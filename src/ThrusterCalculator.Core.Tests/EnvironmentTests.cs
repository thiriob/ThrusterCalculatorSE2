using ThrusterCalculator.Core.Calculations;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

public class ThrustEffectivenessTests
{
    [Theory]
    [InlineData(1.0, 1.0)]   // sea level: full thrust
    [InlineData(0.8, 1.0)]   // exactly at the max endpoint
    [InlineData(0.5, 0.5)]   // halfway up the ramp
    [InlineData(0.2, 0.0)]   // exactly at the min endpoint
    [InlineData(0.0, 0.0)]   // vacuum: nothing
    public void AtmosphericRampsWithAirDensity(double airDensity, double expected)
    {
        var e = ThrustEffectiveness.LinearRampAirDensity(TestData.Atmospheric, airDensity);

        Assert.Equal(expected, e, 10);
    }

    [Theory]
    [InlineData(0.0, 1.0)]   // vacuum: full thrust
    [InlineData(0.2, 1.0)]
    [InlineData(0.5, 0.5)]
    [InlineData(0.8, 0.0)]
    [InlineData(1.0, 0.0)]   // sea level: nothing
    public void IonRampsInverted(double airDensity, double expected)
    {
        // Ion expresses "full thrust in vacuum" as min > max. Normalising that ordering, or
        // assuming min < max, silently inverts every ion thruster in the game.
        var e = ThrustEffectiveness.LinearRampAirDensity(TestData.Ion, airDensity);

        Assert.Equal(expected, e, 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void NegativeMinIsTheNoFalloffSentinel(double airDensity)
    {
        var e = ThrustEffectiveness.LinearRampAirDensity(TestData.Hydrogen, airDensity);

        Assert.Equal(1.0, e);
    }

    [Fact]
    public void AtmosphericAndIonAreComplementaryAtTheExtremes()
    {
        // The property that actually matters to a player: whatever dies in vacuum lives at sea
        // level, and vice versa.
        Assert.Equal(1.0, ThrustEffectiveness.LinearRampAirDensity(TestData.Atmospheric, 1.0), 10);
        Assert.Equal(0.0, ThrustEffectiveness.LinearRampAirDensity(TestData.Atmospheric, 0.0), 10);
        Assert.Equal(0.0, ThrustEffectiveness.LinearRampAirDensity(TestData.Ion, 1.0), 10);
        Assert.Equal(1.0, ThrustEffectiveness.LinearRampAirDensity(TestData.Ion, 0.0), 10);
    }

    [Fact]
    public void ResultIsAlwaysClamped()
    {
        // Air density should never exceed 1, but a hand-edited config could say otherwise.
        Assert.Equal(1.0, ThrustEffectiveness.LinearRampAirDensity(TestData.Atmospheric, 5.0), 10);
        Assert.Equal(0.0, ThrustEffectiveness.LinearRampAirDensity(TestData.Atmospheric, -5.0), 10);
    }

    [Fact]
    public void DegenerateRampBecomesAStepRatherThanDividingByZero()
    {
        var degenerate = new ThrustClass
        {
            Id = "degenerate", MinThrustAirDensity = 0.5, MaxThrustAirDensity = 0.5,
        };

        Assert.Equal(1.0, ThrustEffectiveness.LinearRampAirDensity(degenerate, 0.6));
        Assert.Equal(1.0, ThrustEffectiveness.LinearRampAirDensity(degenerate, 0.5));
        Assert.Equal(0.0, ThrustEffectiveness.LinearRampAirDensity(degenerate, 0.4));
    }
}

public class AtmosphereDensityTests
{
    private static readonly Atmosphere EarthLike =
        new() { ConstantAffectDistance = 1.08, AffectDistance = 1.15 };

    [Fact]
    public void AirlessBodyHasNoAtmosphereAnywhere()
    {
        Assert.Equal(0.0, AtmosphereDensity.LinearRampAltitude(null, 1.0));
        Assert.Equal(0.0, AtmosphereDensity.LinearRampAltitude(null, 0.0));
    }

    [Theory]
    [InlineData(1.00, 1.0)]   // surface
    [InlineData(1.08, 1.0)]   // top of the constant-density shell
    [InlineData(1.15, 0.0)]   // edge of atmosphere
    [InlineData(2.00, 0.0)]   // orbit
    public void RampsFromTheConstantShellToTheEdge(double distanceInRadii, double expected)
    {
        Assert.Equal(expected, AtmosphereDensity.LinearRampAltitude(EarthLike, distanceInRadii), 10);
    }

    [Fact]
    public void HalfwayUpTheRampIsHalfDensity()
    {
        var midpoint = (1.08 + 1.15) / 2;

        Assert.Equal(0.5, AtmosphereDensity.LinearRampAltitude(EarthLike, midpoint), 10);
    }

    [Fact]
    public void DecreasesMonotonicallyWithAltitude()
    {
        var previous = double.PositiveInfinity;
        for (var d = 1.0; d <= 1.2; d += 0.01)
        {
            var density = AtmosphereDensity.LinearRampAltitude(EarthLike, d);
            Assert.True(density <= previous + 1e-12, $"density should not rise; broke at {d}");
            previous = density;
        }
    }
}

public class FlightEnvironmentTests
{
    private static readonly CalculationEngine Engine = CalculationEngine.Create(TestData.Models);

    private static Planet PlanetWith(double? gravity, Atmosphere? atmosphere = null) => new()
    {
        Id = "p",
        Name = "P",
        SurfaceGravity = gravity,
        Atmosphere = atmosphere,
        ProvenanceOverrides = gravity is null
            ? new Dictionary<string, Provenance> { ["surfaceGravity"] = Provenance.Unknown }
            : new Dictionary<string, Provenance> { ["surfaceGravity"] = Provenance.Assumed },
    };

    [Fact]
    public void SurfaceOfAnAtmosphericPlanetHasFullAirDensity()
    {
        var planet = PlanetWith(9.81, new Atmosphere { ConstantAffectDistance = 1.08, AffectDistance = 1.15 });

        var env = FlightEnvironment.ForPlanet(planet, Engine);

        Assert.NotNull(env);
        Assert.Equal(9.81, env!.GravityMetresPerSecondSquared);
        Assert.Equal(1.0, env.AirDensity);
        Assert.Equal(Provenance.Assumed, env.GravityProvenance);
    }

    [Fact]
    public void UnknownGravityYieldsNoEnvironment()
    {
        // The UI must offer an editable field rather than inventing a number.
        Assert.Null(FlightEnvironment.ForPlanet(PlanetWith(null), Engine));
    }

    [Fact]
    public void GravityOverrideMakesAnUnknownPlanetUsable()
    {
        var env = FlightEnvironment.ForPlanet(PlanetWith(null), Engine, gravityOverride: 7.5);

        Assert.NotNull(env);
        Assert.Equal(7.5, env!.GravityMetresPerSecondSquared);
        Assert.Equal(Provenance.Assumed, env.GravityProvenance);
    }

    [Fact]
    public void VacuumHasNoGravityAndNoAir()
    {
        Assert.Equal(0.0, FlightEnvironment.Vacuum.GravityMetresPerSecondSquared);
        Assert.Equal(0.0, FlightEnvironment.Vacuum.AirDensity);
    }
}
