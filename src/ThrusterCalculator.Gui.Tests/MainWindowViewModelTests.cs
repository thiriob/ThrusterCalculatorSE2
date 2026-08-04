using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Gui.Services;
using ThrusterCalculator.Gui.ViewModels;

namespace ThrusterCalculator.Gui.Tests;

public class MainWindowViewModelTests
{
    private static MainWindowViewModel Create()
    {
        // The bundled sample, which is what the app falls back to with no game installed.
        var config = ConfigSource.Load();
        Assert.Equal(ConfigOrigin.Sample, config.Origin);
        return new MainWindowViewModel(config);
    }

    [Fact]
    public void LoadsTheSampleConfigWhenNoRealOneExists()
    {
        var vm = Create();

        Assert.True(vm.IsSampleData);
        Assert.NotEmpty(vm.Planets);
        Assert.NotEmpty(vm.Results);
    }

    [Fact]
    public void SelectsAPlanetAndAdoptsItsGravity()
    {
        var vm = Create();

        Assert.NotNull(vm.SelectedPlanet);
        Assert.Equal(vm.SelectedPlanet!.SurfaceGravity, vm.Gravity);
    }

    [Fact]
    public void MarksGravityAsAssumed()
    {
        // Surface gravity is never in the game's definition files, so it is always the user's to
        // supply — and the UI must say so.
        var vm = Create();

        Assert.True(vm.GravityIsAssumed);
    }

    [Fact]
    public void ProducesFeasibleConfigurationsForAnAtmosphericWorld()
    {
        var vm = Create();

        Assert.Contains(vm.Results, r => r.IsFeasible);
    }

    [Fact]
    public void IonThrustersAreRejectedAtSeaLevelWithAReason()
    {
        // "no thrust in this atmosphere" is the useful answer; a bare zero is not.
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.Single(p => p.Atmosphere is not null && p.SurfaceGravity is not null);

        var ion = vm.Results.FirstOrDefault(r => r.Name.Contains("Ion", StringComparison.Ordinal));

        Assert.NotNull(ion);
        Assert.False(ion!.IsFeasible);
        Assert.Equal(SizingStatus.NoThrustInEnvironment, ion.Status);
        Assert.Contains("atmosphere", ion.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void IonThrustersWorkOnAnAirlessBody()
    {
        var vm = Create();

        vm.SelectedPlanet = vm.Planets.Single(p => p.Atmosphere is null);

        var ion = vm.Results.Single(r => r.Name.Contains("Ion", StringComparison.Ordinal));
        Assert.True(ion.IsFeasible);
    }

    [Fact]
    public void AtmosphericThrustersDieOnAnAirlessBody()
    {
        var vm = Create();

        vm.SelectedPlanet = vm.Planets.Single(p => p.Atmosphere is null);

        var atmo = vm.Results.First(r => r.Name.Contains("Atmospheric", StringComparison.Ordinal));
        Assert.Equal(SizingStatus.NoThrustInEnvironment, atmo.Status);
    }

    [Fact]
    public void UnimplementedBlocksAreShownRatherThanHidden()
    {
        // During alpha "does this exist yet?" is a real question.
        var vm = Create();

        var underwater = vm.Results.Single(r => r.Name.Contains("Underwater", StringComparison.Ordinal));

        Assert.Equal(SizingStatus.NotImplemented, underwater.Status);
        Assert.Contains("not in this build", underwater.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlayablePlanetsComeFirst()
    {
        // Only Verdure and Kemik are reachable in the current build, so they should not need
        // scrolling for. The sample has neither, so this checks the ordering is at least stable.
        var vm = Create();

        Assert.Equal(vm.Planets.OrderByDescending(p => p.Id is "verdure" or "kemik")
                               .ThenBy(p => p.Name, StringComparer.Ordinal)
                               .Select(p => p.Id),
                     vm.Planets.Select(p => p.Id));
    }

    // ── mass entry ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void DirectMassDrivesTheRequirement()
    {
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Direct;

        vm.DirectMassTonnes = 500;
        var lighter = vm.Results.First(r => r.IsFeasible).Count;

        vm.DirectMassTonnes = 1000;
        var heavier = vm.Results.First(r => r.IsFeasible).Count;

        Assert.True(heavier > lighter);
    }

    [Fact]
    public void LoadPresetsAreAllReportedAtOnce()
    {
        // The failure this app exists to prevent is the ship that lifts empty and strands full,
        // which you cannot see if you have to toggle between views.
        var vm = Create();

        Assert.Equal(3, vm.Loads.Count);
        Assert.Equal(["Empty", "Half", "Full"], vm.Loads.Select(l => l.Name));
    }

    [Fact]
    public void CargoFillRaisesMassInStorageMode()
    {
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Storage;
        vm.HullMassTonnes = 300;
        vm.Containers.First().Count = 4;

        var masses = vm.Loads.Select(l => l.TotalMassKg).ToList();

        Assert.True(masses[0] < masses[1], "half load should exceed empty");
        Assert.True(masses[1] < masses[2], "full load should exceed half");
    }

    [Fact]
    public void EmptyPresetStillIncludesContainerBlockMass()
    {
        // Empty means no cargo, not no containers.
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Storage;
        vm.HullMassTonnes = 100;

        var bare = vm.Loads[0].TotalMassKg;
        vm.Containers.First().Count = 10;

        Assert.True(vm.Loads[0].TotalMassKg > bare);
    }

    [Fact]
    public void TanksContributeBlockMassOnly()
    {
        // Gas is massless in SE2 — verified by watching a tank fill with the ship mass unchanged —
        // so a tank adds its own weight and nothing else, and cargo fill must not move it.
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Storage;
        vm.HullMassTonnes = 100;

        var bare = vm.Loads[0].TotalMassKg;
        vm.Tanks.First().Count = 3;

        var withTanks = vm.Loads.Select(l => l.TotalMassKg).ToList();

        Assert.True(withTanks[0] > bare, "tanks should add their own block mass");
        Assert.Equal(withTanks[0], withTanks[2]);   // empty and full load identical: no cargo, no gas
    }

    [Fact]
    public void HigherTargetRatioNeedsMoreThrusters()
    {
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Direct;
        vm.DirectMassTonnes = 500;

        vm.TargetThrustToWeight = 1.0;
        var at1 = vm.Results.First(r => r.IsFeasible).Count;

        vm.TargetThrustToWeight = 2.0;
        Assert.True(vm.Results.First(r => r.IsFeasible).Count > at1);
    }

    [Fact]
    public void FeasibleResultsAreOrderedByAddedMass()
    {
        var vm = Create();

        var feasible = vm.Results.Where(r => r.IsFeasible).Select(r => r.AddedMassKg).ToList();

        Assert.Equal(feasible.OrderBy(m => m), feasible);
    }

    [Fact]
    public void ResultsCarryTheirSupportedRangeAndHeadroom()
    {
        var vm = Create();

        var first = vm.Results.First(r => r.IsFeasible);

        Assert.Contains("covers", first.RangeText, StringComparison.Ordinal);
        Assert.True(first.MaxSupportedShipMassKg >= first.ShipMassKg);
        Assert.NotEmpty(first.HeadroomText);
    }

    [Fact]
    public void UncertainResultsSaySo()
    {
        // Sample gravity is assumed, so every result inherits that and must be marked.
        var vm = Create();

        var first = vm.Results.First(r => r.IsFeasible);

        Assert.True(first.IsUncertain);
        Assert.Equal("assumed", first.ProvenanceText);
    }

    [Fact]
    public void UnknownGravityPlanetDoesNotCrashTheCalculation()
    {
        var vm = Create();

        vm.SelectedPlanet = vm.Planets.Single(p => p.SurfaceGravity is null);

        // Gravity keeps its previous value for the user to correct, rather than becoming zero.
        Assert.True(vm.Gravity > 0);
        Assert.True(vm.GravityIsAssumed);
    }

    [Fact]
    public void ZeroMassProducesNoConfigurationsRatherThanNonsense()
    {
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Direct;

        vm.DirectMassTonnes = 0;

        Assert.Empty(vm.Results);
        Assert.Equal("—", vm.RequirementText);
    }

    [Fact]
    public void AssumptionAboutExistingThrustersIsAlwaysStated()
    {
        Assert.Contains("no thrusters", Create().Assumption, StringComparison.OrdinalIgnoreCase);
    }
}
