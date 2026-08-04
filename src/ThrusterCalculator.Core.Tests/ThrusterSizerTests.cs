using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

public class ThrusterSizerTests
{
    // Real Atmospheric Thruster 2.5 m: 287,136.3 N, V = 288 cells.
    private const double Atmo250Thrust = 287136.3;
    private const int Atmo250Cells = 288;

    private static ThrusterSizer SizerWith(params Thruster[] thrusters) =>
        ThrusterSizer.For(TestData.Build(thrusters));

    /// <summary>
    /// The worked example from Technic.md §5.2, end to end: a 500 t hull on a 9.81 m/s² world at
    /// TWR 1.0 needs 18 Atmospheric 2.5 m thrusters.
    /// </summary>
    [Fact]
    public void WorkedExample_500TonneHull()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(SizingStatus.Feasible, result.Status);
        Assert.Equal(18, result.Count);

        // 11 * sqrt(288) * log10(288) + 5
        Assert.Equal(464.09, result.ThrusterMassKgEach, 1);

        // Rounding up to a whole thruster leaves a little headroom.
        Assert.True(result.AchievedThrustToWeight >= 1.0);
        Assert.Equal(1.036, result.AchievedThrustToWeight, 2);

        // ...and that headroom is exactly the growing room the range reports.
        Assert.True(result.MaxSupportedShipMassKg > 500_000);
        Assert.True(Math.Abs(result.MaxSupportedShipMassKg - 518_501) < 100,
            $"expected ~518,501 kg ceiling, got {result.MaxSupportedShipMassKg:F0}");
    }

    [Fact]
    public void SeventeenThrustersWouldNotHaveBeenEnough()
    {
        // Guards the ceiling: 17 is the floor of the exact solution, and must fall short.
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = SizerWith(thruster);
        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81) });

        var mass = result.ThrusterMassKgEach;
        var thrustOf17 = 17 * Atmo250Thrust;
        var weightOf17 = (500_000 + (17 * mass)) * 9.81;

        Assert.True(thrustOf17 < weightOf17, "17 thrusters should not lift the ship");
        Assert.Equal(18, result.Count);
    }

    [Fact]
    public void AccountsForItsOwnAddedMass()
    {
        // 490 t is chosen deliberately: it is a mass where ignoring thruster weight changes the
        // answer. (At 500 t the naive count happens to coincide, so it proves nothing.)
        const double shipMass = 490_000;
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = shipMass, Environment = TestData.Surface(9.81) });

        var naive = (int)Math.Ceiling(shipMass * 9.81 / Atmo250Thrust);

        Assert.Equal(17, naive);
        Assert.Equal(18, result.Count);

        // And the naive answer really is wrong: 17 of them cannot lift the resulting ship.
        var weightWith17 = (shipMass + (17 * result.ThrusterMassKgEach)) * 9.81;
        Assert.True(17 * Atmo250Thrust < weightWith17);

        Assert.Equal(result.Count * result.ThrusterMassKgEach, result.AddedMassKg, 6);
        Assert.Equal(shipMass + result.AddedMassKg, result.TotalMassKg, 6);
    }

    [Fact]
    public void MaxSupportedMassIsActuallySupported()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = SizerWith(thruster);
        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81) });

        // At exactly the ceiling, thrust still meets weight...
        var atLimit = (result.MaxSupportedShipMassKg + (result.Count * result.ThrusterMassKgEach)) * 9.81;
        Assert.True(result.TotalThrustN >= atLimit - 1e-3);

        // ...and one more thruster would be needed just past it.
        var justOver = sizer.Size(thruster, new SizingRequest
        {
            ShipMassKg = result.MaxSupportedShipMassKg + 1_000,
            Environment = TestData.Surface(9.81),
        });
        Assert.Equal(result.Count + 1, justOver.Count);
    }

    // ── the failure modes ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void ThrusterThatCannotLiftItselfIsReportedRatherThanSolved()
    {
        // 1 kN of thrust against ~14 t of thruster: no quantity is ever enough. A naive
        // implementation divides by a negative denominator and returns a confident positive.
        var thruster = TestData.Thruster("brick", 1_000, 10_000);
        var sizer = ThrusterSizer.For(TestData.Build(thruster) with
        {
            Densities = [new Density { Id = "mostlyHollow", Name = "Solid", MassCurveModifier = 35 }],
        });

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(10) });

        Assert.Equal(SizingStatus.CannotLiftOwnWeight, result.Status);
        Assert.False(result.IsFeasible);
        Assert.Equal(0, result.Count);
    }

    [Fact]
    public void ThrusterExactlyBreakingEvenIsStillImpossible()
    {
        // Denominator exactly zero: T*E == R*g*m. The guard must be <= 0, not < 0.
        const double gravity = 10.0;
        var mass = Calculations.BlockMass.SqrtLog10CellCount(288, TestData.MostlyHollow, TestData.MinBlockMass);
        var thruster = TestData.Thruster("breakEven", mass * gravity, 288);
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 1_000, Environment = TestData.Surface(gravity) });

        Assert.Equal(SizingStatus.CannotLiftOwnWeight, result.Status);
    }

    [Fact]
    public void IonThrusterAtSeaLevelReportsNoThrustRatherThanZero()
    {
        // "no thrust in atmosphere" is the useful answer; a 0 or a blank row is not.
        var thruster = TestData.Thruster("ion", 856_368, 1898, thrustClass: "ion");
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81, airDensity: 1.0) });

        Assert.Equal(SizingStatus.NoThrustInEnvironment, result.Status);
    }

    [Fact]
    public void IonThrusterInVacuumWorks()
    {
        var thruster = TestData.Thruster("ion", 856_368, 1898, thrustClass: "ion");
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(3.0, airDensity: 0.0) });

        Assert.Equal(SizingStatus.Feasible, result.Status);
        Assert.Equal(1.0, result.Effectiveness);
    }

    [Fact]
    public void WaterOnlyThrusterNeverProducesThrust()
    {
        var thruster = TestData.Thruster("underwater", 500_000, 288, thrustClass: "water");
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 10_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(SizingStatus.NoThrustInEnvironment, result.Status);
    }

    [Fact]
    public void UnimplementedThrusterIsReportedNotSkipped()
    {
        var thruster = TestData.Thruster("future", null, null, implemented: false);
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 10_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(SizingStatus.NotImplemented, result.Status);
    }

    [Fact]
    public void UnknownThrustIsReported()
    {
        var thruster = TestData.Thruster("mystery", null, 288);
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 10_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(SizingStatus.ThrustUnknown, result.Status);
    }

    [Fact]
    public void UnknownMassIsReportedRatherThanTreatedAsZero()
    {
        // Substituting zero here would silently corrupt the denominator and produce a confident
        // under-count.
        var thruster = TestData.Thruster("mystery", 500_000, occupiedCells: null);
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 10_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(SizingStatus.MassUnknown, result.Status);
    }

    // ── behaviour around the inputs ───────────────────────────────────────────────────────────

    [Fact]
    public void HigherTargetRatioNeedsMoreThrusters()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = SizerWith(thruster);

        var at1 = sizer.Size(thruster, new SizingRequest
        {
            ShipMassKg = 500_000, Environment = TestData.Surface(9.81), TargetThrustToWeight = 1.0,
        });
        var at2 = sizer.Size(thruster, new SizingRequest
        {
            ShipMassKg = 500_000, Environment = TestData.Surface(9.81), TargetThrustToWeight = 2.0,
        });

        Assert.True(at2.Count > at1.Count);
        Assert.True(at2.AchievedThrustToWeight >= 2.0);
    }

    [Fact]
    public void HigherGravityNeedsMoreThrusters()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells);
        var sizer = SizerWith(thruster);

        var light = sizer.Size(thruster, new SizingRequest
        {
            ShipMassKg = 500_000, Environment = TestData.Surface(3.0),
        });
        var heavy = sizer.Size(thruster, new SizingRequest
        {
            ShipMassKg = 500_000, Environment = TestData.Surface(9.81),
        });

        Assert.True(heavy.Count > light.Count);
    }

    [Fact]
    public void ResourceDrawScalesWithCount()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells) with
        {
            ConsumedResource = new ConsumedResource { Resource = "electricity", RatePerThrust = 650 },
        };
        var sizer = SizerWith(thruster);

        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81) });

        Assert.Equal("electricity", result.ResourceId);
        Assert.Equal(result.Count * 650.0, result.ResourceRateTotal);
    }

    [Fact]
    public void SizeAllCoversEveryThrusterInDeclarationOrder()
    {
        var sizer = SizerWith(
            TestData.Thruster("a", Atmo250Thrust, Atmo250Cells),
            TestData.Thruster("b", null, null, implemented: false));

        var results = sizer.SizeAll(
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(2, results.Count);
        Assert.Equal("a", results[0].ThrusterId);
        Assert.Equal(SizingStatus.Feasible, results[0].Status);
        Assert.Equal(SizingStatus.NotImplemented, results[1].Status);
    }

    // ── provenance ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ResultInheritsTheWeakestInputProvenance()
    {
        var thruster = TestData.Thruster("atmo250", Atmo250Thrust, Atmo250Cells) with
        {
            ProvenanceOverrides = new Dictionary<string, Provenance>
            {
                ["occupiedCells"] = Provenance.Derived,
            },
        };
        var sizer = SizerWith(thruster);

        // Gravity is assumed, cells are derived: assumed is weaker, so it wins.
        var result = sizer.Size(thruster,
            new SizingRequest { ShipMassKg = 500_000, Environment = TestData.Surface(9.81) });

        Assert.Equal(Provenance.Assumed, result.Provenance);
    }

    [Fact]
    public void MissingThrustClassDowngradesProvenance()
    {
        // The engine's default for a class-less thruster is inferred, not confirmed, so the answer
        // must not present itself as measured.
        var thruster = TestData.Thruster("h2", 1_895_631, 936, thrustClass: null);
        var sizer = SizerWith(thruster);

        var env = TestData.Surface(9.81) with { GravityProvenance = Provenance.Measured };
        var result = sizer.Size(thruster, new SizingRequest { ShipMassKg = 500_000, Environment = env });

        Assert.Equal(SizingStatus.Feasible, result.Status);
        Assert.Equal(1.0, result.Effectiveness);
        Assert.Equal(Provenance.Assumed, result.Provenance);
    }
}
