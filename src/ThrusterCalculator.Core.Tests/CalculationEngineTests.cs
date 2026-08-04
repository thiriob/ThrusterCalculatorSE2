using ThrusterCalculator.Model;

namespace ThrusterCalculator.Core.Tests;

public class CalculationEngineTests
{
    [Fact]
    public void AcceptsTheShippedModelKinds()
    {
        var engine = CalculationEngine.Create(TestData.Models);

        Assert.Equal(TestData.MinBlockMass, engine.MinBlockMassKg);
    }

    [Theory]
    [InlineData("blockMass")]
    [InlineData("thrustEffectiveness")]
    [InlineData("atmosphereDensity")]
    public void RejectsAnUnknownModelKindLoudly(string which)
    {
        // Silently falling back to a default model would compute confident numbers with the wrong
        // formula — the exact failure the provenance system exists to prevent.
        var models = which switch
        {
            "blockMass" => TestData.Models with
            {
                BlockMass = new BlockMassModel { Kind = "somethingNew", MinBlockMass = 5 },
            },
            "thrustEffectiveness" => TestData.Models with
            {
                ThrustEffectiveness = new ThrustEffectivenessModel { Kind = "somethingNew" },
            },
            _ => TestData.Models with
            {
                AtmosphereDensity = new AtmosphereDensityModel { Kind = "somethingNew" },
            },
        };

        var ex = Assert.Throws<UnknownCalculationModelException>(() => CalculationEngine.Create(models));

        Assert.Equal(which, ex.ModelName);
        Assert.Equal("somethingNew", ex.Kind);
        Assert.Contains("somethingNew", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidatesEagerlyAtConstruction()
    {
        var models = TestData.Models with
        {
            AtmosphereDensity = new AtmosphereDensityModel { Kind = "nope" },
        };

        // Fails building the sizer, not at the first calculation.
        Assert.Throws<UnknownCalculationModelException>(
            () => ThrusterSizerFactory(TestData.Build() with { Models = models }));

        static object ThrusterSizerFactory(GameData data) => Sizing.ThrusterSizer.For(data);
    }
}

public class GameDataIndexTests
{
    [Fact]
    public void ResolvesByIdAndReturnsNullForMisses()
    {
        var index = new GameDataIndex(TestData.Build(TestData.Thruster("a", 1, 1)));

        Assert.NotNull(index.Thruster("a"));
        Assert.NotNull(index.Density("mostlyHollow"));
        Assert.NotNull(index.ThrustClass("ion"));

        Assert.Null(index.Thruster("nope"));
        Assert.Null(index.Thruster(null));
        Assert.Null(index.Density(null));
    }

    [Fact]
    public void DuplicateIdsDoNotThrow()
    {
        // A malformed config should still be usable; the producer records duplicates as warnings.
        var data = TestData.Build(
            TestData.Thruster("dup", 100, 10),
            TestData.Thruster("dup", 200, 20));

        var index = new GameDataIndex(data);

        Assert.Equal(200, index.Thruster("dup")!.ThrustNewtons);
    }
}

public class ProvenanceOrderTests
{
    [Fact]
    public void EnumIsDeclaredWeakestLast()
    {
        // ProvenanceExtensions.Weakest relies on this ordering. Reordering the enum without
        // updating that method would silently invert every confidence report.
        Assert.True(Provenance.Measured < Provenance.Derived);
        Assert.True(Provenance.Derived < Provenance.Assumed);
        Assert.True(Provenance.Assumed < Provenance.Unknown);
    }

    [Fact]
    public void WeakestPicksTheLeastTrustworthy()
    {
        Assert.Equal(Provenance.Measured, ProvenanceExtensions.Weakest(Provenance.Measured));
        Assert.Equal(Provenance.Assumed,
            ProvenanceExtensions.Weakest(Provenance.Measured, Provenance.Assumed, Provenance.Derived));
        Assert.Equal(Provenance.Unknown,
            ProvenanceExtensions.Weakest(Provenance.Assumed, Provenance.Unknown));
    }

    [Fact]
    public void WeakestOfNothingIsMeasured()
    {
        Assert.Equal(Provenance.Measured, ProvenanceExtensions.Weakest());
    }
}
