using ThrusterCalculator.Core;
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

    // ── gravity override ──────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlanetGravityIsUsedAndTheFieldIsLockedUntilOverridden()
    {
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.First(p => p.SurfaceGravity is not null);

        Assert.False(vm.GravityIsCustom);
        Assert.True(vm.CanUsePlanetGravity);
        Assert.Equal(vm.SelectedPlanet.SurfaceGravity, vm.Gravity);
    }

    [Fact]
    public void TheOverrideTakesEffectWithoutDisturbingTheStoredValue()
    {
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.First(p => p.SurfaceGravity is not null);
        vm.CustomGravity = 3.71;

        // Not in force yet, so the planet's own value is what counts.
        Assert.Equal(vm.SelectedPlanet.SurfaceGravity, vm.Gravity);

        vm.GravityIsCustom = true;

        // Ticking must not overwrite what the user typed — the two numbers are separate on screen
        // and the custom one is theirs to keep.
        Assert.Equal(3.71, vm.CustomGravity);
        Assert.Equal(3.71, vm.Gravity);
    }

    [Fact]
    public void ChangingPlanetClearsTheOverrideButKeepsItsValue()
    {
        // Without this, selecting a planet changed the maths and nothing on screen, because the
        // override silently kept winning — it read as the planet picker being broken.
        var vm = Create();
        var planets = vm.Planets.Where(p => p.SurfaceGravity is not null).Take(2).ToList();

        vm.SelectedPlanet = planets[0];
        vm.GravityIsCustom = true;
        vm.CustomGravity = 3.71;

        vm.SelectedPlanet = planets[1];

        Assert.False(vm.GravityIsCustom);
        Assert.Equal(planets[1].SurfaceGravity, vm.Gravity);

        // The value survives, so re-enabling it is one click rather than retyping.
        Assert.Equal(3.71, vm.CustomGravity);
    }

    [Fact]
    public void TheDisplayedGravityFollowsTheSelectedPlanet()
    {
        var vm = Create();
        var planets = vm.Planets.Where(p => p.SurfaceGravity is not null).Take(2).ToList();

        vm.SelectedPlanet = planets[0];
        var first = vm.GravityText;

        vm.SelectedPlanet = planets[1];

        Assert.NotEqual(first, vm.GravityText);
        Assert.Contains("m/s", vm.GravityText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheGravityNoteExplainsWhereTheNumberCameFrom()
    {
        var vm = Create();

        // Stated in the config, which after the delta-encoding fix is every real planet.
        vm.SelectedPlanet = vm.Planets.First(p => p.SurfaceGravity is not null);
        Assert.Contains("game's own planet data", vm.GravityNote, StringComparison.Ordinal);

        // Overridden by choice: no explanation of a value we are not using.
        vm.GravityIsCustom = true;
        Assert.Contains("Your own value", vm.GravityNote, StringComparison.Ordinal);

        // Nothing known. The override is forced on here, but the note must still explain *why*
        // we are asking — otherwise the user is left guessing what to type.
        vm.GravityIsCustom = false;
        vm.SelectedPlanet = vm.Planets.Single(p => p.SurfaceGravity is null);

        Assert.True(vm.GravityIsCustom);
        Assert.Contains("radius", vm.GravityNote, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanetWithNoStatedGravityForcesTheOverrideOn()
    {
        // Every planet in a real extracted config is this case: gravity depends on planet radius,
        // which is per-world data the definitions do not carry. There is nothing to fall back to.
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.Single(p => p.SurfaceGravity is null);

        Assert.False(vm.CanUsePlanetGravity);
        Assert.True(vm.GravityIsCustom);
        Assert.Equal(vm.CustomGravity, vm.Gravity);
    }

    [Fact]
    public void BeingForcedOnDoesNotRecordAnOverrideForOtherPlanets()
    {
        // The forced state is a consequence of the planet, not a choice the user made, so it must
        // not follow them to a planet that does state its gravity.
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.Single(p => p.SurfaceGravity is null);

        Assert.True(vm.GravityIsCustom);
        Assert.False(vm.UseCustomGravity);

        vm.SelectedPlanet = vm.Planets.First(p => p.SurfaceGravity is not null);

        Assert.False(vm.GravityIsCustom);
        Assert.Equal(vm.SelectedPlanet.SurfaceGravity, vm.Gravity);
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
    public void EveryFeasibleLoadoutReportsItsDrawWithUnits()
    {
        // The producer used to lose ConsumedResource for any thruster that inherited it, which was
        // most of them, so this column was blank for 8 of 12 real thrusters with no warning
        // anywhere. Asserting on every feasible row is what makes that visible here.
        var vm = Create();

        var feasible = vm.Results.Where(r => r.IsFeasible).ToList();

        Assert.NotEmpty(feasible);
        Assert.All(feasible, r => Assert.NotEqual(string.Empty, r.DrawText));

        // Units matter: electricity is kW and hydrogen is L/s, and the bare numbers differ by
        // orders of magnitude, so an unlabelled column reads as a false efficiency comparison.
        Assert.Contains(feasible, r => r.DrawText.EndsWith("kW", StringComparison.Ordinal));
        Assert.Contains(feasible, r => r.DrawText.EndsWith("L/s", StringComparison.Ordinal));
    }

    // ── results panel ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void FeasibleLoadoutsAreGroupedByFamilyAndOrderedBySize()
    {
        var vm = Create();

        Assert.NotEmpty(vm.Families);

        foreach (var family in vm.Families)
        {
            Assert.NotEmpty(family.Rows);
            Assert.All(family.Rows, r => Assert.Equal(family.Name, r.Family));
            Assert.All(family.Rows, r => Assert.True(r.IsFeasible));

            // Ascending size, so "one size up" is the next row rather than a search.
            Assert.Equal(family.Rows.OrderBy(r => r.SizeCm).Select(r => r.SizeCm),
                         family.Rows.Select(r => r.SizeCm));
        }
    }

    [Fact]
    public void TheCheapestFamilyLeads()
    {
        // The best answer overall should be the first row on screen.
        var vm = Create();

        var bestPerFamily = vm.Families.Select(f => f.Rows.Min(r => r.AddedMassKg)).ToList();

        Assert.Equal(bestPerFamily.OrderBy(m => m), bestPerFamily);
    }

    [Fact]
    public void UnusableLoadoutsAreFoldedAwayButStillCounted()
    {
        // Collapsed, never hidden — on an atmospheric world "the ion family is dead here" is the
        // most useful thing the panel says, it just does not need a row each to say it.
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.Single(p => p.Atmosphere is not null && p.SurfaceGravity is not null);

        Assert.True(vm.HasUnusable);
        Assert.All(vm.Unusable, r => Assert.False(r.IsFeasible));
        Assert.Contains(vm.Unusable.Count.ToString(), vm.UnusableSummary, StringComparison.Ordinal);

        // Nothing may fall between the two collections.
        Assert.Equal(vm.Results.Count,
                     vm.Families.Sum(f => f.Rows.Count) + vm.Unusable.Count);
    }

    [Fact]
    public void FamiliesRebuildWhenTheEnvironmentChanges()
    {
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.Single(p => p.Atmosphere is null);

        // In vacuum ion works and atmospheric does not, so the grouping must have inverted.
        Assert.Contains(vm.Families, f => f.Name.Contains("Ion", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(vm.Unusable, r => r.Name.Contains("Atmospheric", StringComparison.Ordinal));
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
    public void PlanetsAreOrderedByAvailabilityThenName()
    {
        // Playable first, so the common case needs no scrolling. Derived from each planet's
        // milestone rather than a hardcoded list of names.
        var vm = Create();

        Assert.Equal(vm.Planets.OrderBy(p => p.Availability)
                               .ThenBy(p => p.Name, StringComparer.Ordinal)
                               .Select(p => p.Id),
                     vm.Planets.Select(p => p.Id));
    }

    [Fact]
    public void TheDropdownCarriesAHeadingForEachNonEmptyGroup()
    {
        var vm = Create();

        var headings = vm.PlanetItems.Where(i => !i.IsSelectable).ToList();
        var planets = vm.PlanetItems.Where(i => i.IsSelectable).ToList();

        // Every planet appears exactly once, and no heading exists without members.
        Assert.Equal(vm.Planets.Count, planets.Count);
        Assert.Equal(vm.Planets.Select(p => p.Id), planets.Select(i => i.Planet!.Id));
        Assert.Equal(vm.Planets.Select(p => p.Availability).Distinct().Count(), headings.Count);

        // A heading always precedes the planets it introduces.
        Assert.False(vm.PlanetItems[0].IsSelectable);
    }

    [Fact]
    public void SelectingAHeadingIsRejectedRatherThanEmptyingTheApp()
    {
        // The container style disables headings, but a failed binding is silent in Avalonia and
        // the failure mode is a departure "planet" with no gravity and no results.
        var vm = Create();
        var before = vm.SelectedPlanet;

        vm.SelectedPlanetItem = vm.PlanetItems.First(i => !i.IsSelectable);

        Assert.Same(before, vm.SelectedPlanet);
        Assert.NotNull(vm.SelectedPlanetItem);
        Assert.Same(before, vm.SelectedPlanetItem!.Planet);
        Assert.NotEmpty(vm.Results);
    }

    [Fact]
    public void SettingThePlanetDirectlyMovesTheDropdown()
    {
        var vm = Create();
        var target = vm.Planets.Last();

        vm.SelectedPlanet = target;

        Assert.Same(target, vm.SelectedPlanetItem?.Planet);
    }

    // ── configurator ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PlacingThrustersClosesTheGap()
    {
        var vm = Create();
        var row = vm.ConfiguratorRows.First(r => r.CanContribute);

        Assert.False(vm.HasPlacedThrusters);
        var before = vm.LoadoutFraction;

        row.Count = 2;

        Assert.True(vm.HasPlacedThrusters);
        Assert.True(vm.LoadoutFraction > before);
        Assert.Contains("2 thrusters", vm.LoadoutSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void EnoughThrustersSatisfiesTheRequirement()
    {
        var vm = Create();
        var row = vm.ConfiguratorRows.First(r => r.CanContribute);

        row.Count = 999;

        Assert.True(vm.LoadoutIsSatisfied);
        Assert.Equal(1, vm.LoadoutFraction, 6);
        Assert.Contains("Lifts off", vm.ShortfallText, StringComparison.Ordinal);
    }

    [Fact]
    public void TheReferenceTableIgnoresWhatIsPlacedButTheWorkingOneDoesNot()
    {
        // The two tables answer different questions, and that difference is the whole point of
        // having both: one is the rule of thumb, the other is what finishes the job.
        var vm = Create();
        var row = vm.ConfiguratorRows.First(r => r.CanContribute);

        var referenceBefore = vm.SingleType.Families.First().Rows.First().Count;
        var remainingBefore = vm.Remaining.Families.First().Rows.First().Count;

        row.Count = 3;

        Assert.Equal(referenceBefore, vm.SingleType.Families.First().Rows.First().Count);
        Assert.True(vm.Remaining.Families.First().Rows.First().Count < remainingBefore);
    }

    [Fact]
    public void NetContributionIsShownAndIsLessThanRatedThrust()
    {
        // The number that stops the shortfall looking like broken arithmetic.
        var vm = Create();
        var row = vm.ConfiguratorRows.First(r => r.CanContribute);

        Assert.True(row.NetContributionN > 0);
        Assert.Contains("kN each", row.NetContributionText, StringComparison.Ordinal);
    }

    [Fact]
    public void ClearingEmptiesTheLoadout()
    {
        var vm = Create();
        vm.ConfiguratorRows.First(r => r.CanContribute).Count = 4;
        Assert.True(vm.HasPlacedThrusters);

        vm.ClearLoadoutCommand.Execute(null);

        Assert.False(vm.HasPlacedThrusters);
        Assert.All(vm.ConfiguratorRows, r => Assert.Equal(0, r.Count));
    }

    [Fact]
    public void ConfiguratorOffersOnlyBlocksThatExistInThisBuild()
    {
        // The reference table lists unimplemented blocks so "does this exist yet?" has an answer;
        // offering a spinner for something unbuildable would not.
        var vm = Create();

        Assert.DoesNotContain(vm.ConfiguratorRows,
            r => r.Name.Contains("Underwater", StringComparison.Ordinal));
        Assert.NotEmpty(vm.ConfiguratorRows);
    }

    // ── reloading after a rebuild ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task RebuildingWithoutTheProducerLeavesEverythingIntact()
    {
        // No tc.exe in a test run, so the command takes its degradation path. The point is that a
        // failed rebuild must not clear the catalogue or the user's inputs — the app has to be
        // exactly as usable afterwards as before.
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Storage;
        vm.Containers.First().Count = 3;
        var planet = vm.SelectedPlanet;

        await vm.RebuildCommand.ExecuteAsync(null);

        Assert.False(vm.IsRebuilding);
        Assert.NotEmpty(vm.DataMessage);
        Assert.Same(planet, vm.SelectedPlanet);
        Assert.Equal(3, vm.Containers.First().Count);
        Assert.NotEmpty(vm.Results);
    }

    [Fact]
    public void ReloadingKeepsWhatTheUserTyped()
    {
        // The point of reloading in place: new game data without asking anyone to restart, and
        // without throwing away the ship they just described.
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Storage;
        vm.HullMassKg = 250_000;
        vm.TargetThrustToWeight = 1.4;
        vm.Containers.First().Count = 3;

        var planetId = vm.SelectedPlanet!.Id;
        var planetCount = vm.Planets.Count;

        vm.Reload(ConfigSource.Load());

        Assert.Equal(planetId, vm.SelectedPlanet!.Id);
        Assert.Equal(250_000, vm.HullMassKg);
        Assert.Equal(1.4, vm.TargetThrustToWeight);
        Assert.Equal(3, vm.Containers.First().Count);

        // Refilled, not appended: a reload that doubled the catalogue would be very obvious in the
        // UI and very easy to write.
        Assert.Equal(planetCount, vm.Planets.Count);
        Assert.NotEmpty(vm.Results);
    }

    [Fact]
    public void StorageRowsAreSubscribedExactlyOnceAcrossReloads()
    {
        // Reload detaches before refilling. Miss that and each rebuild adds another handler, so one
        // edit triggers N recalculations — invisible until it is slow.
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Storage;

        vm.Reload(ConfigSource.Load());
        vm.Reload(ConfigSource.Load());

        var recalculations = 0;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.ShipMassText)) recalculations++;
        };

        vm.Containers.First().Count = 1;

        Assert.Equal(1, recalculations);
    }

    // ── warnings and headings ─────────────────────────────────────────────────────────────────

    [Fact]
    public void TheResultsHeadingNamesTheLoadItIsFor()
    {
        // Three load masses on screen and one set of configurations: which one they are for has to
        // be stated, or sizing for an empty ship reads as sizing for a full one.
        var vm = Create();

        vm.MassEntryMode = MassEntryMode.Direct;
        Assert.Equal("IF YOU USE ONE TYPE", vm.ConfigurationsHeading);

        vm.MassEntryMode = MassEntryMode.Storage;
        vm.SelectedPresetIndex = 0;
        Assert.Contains("EMPTY", vm.ConfigurationsHeading, StringComparison.Ordinal);

        vm.SelectedPresetIndex = 2;
        Assert.Contains("FULL", vm.ConfigurationsHeading, StringComparison.Ordinal);
    }

    [Fact]
    public void ExtractionWarningsAreSurfacedRatherThanDropped()
    {
        // Schema §6 calls this the cheapest defence against a degraded extraction producing
        // confident wrong answers. The producer records them; the app used to ignore them.
        var vm = Create();

        Assert.True(vm.HasExtractionWarnings);
        Assert.Contains("note", vm.ExtractionWarningSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void APlanetsOwnWarningAppearsWhenItIsSelected()
    {
        // Matched by the warning's subject id, never by looking for the planet's name inside the
        // detail text — that string heuristic is the shape of bug this project keeps hitting.
        var vm = Create();
        var warned = vm.Planets.FirstOrDefault(p => p.Id == "sampleUnknownGravity");

        Assert.NotNull(warned);
        vm.SelectedPlanet = warned;

        Assert.True(vm.HasPlanetWarning);
        Assert.Contains("⚠", vm.PlanetWarningText, StringComparison.Ordinal);

        // And it does not follow you to a planet it has nothing to do with.
        vm.SelectedPlanet = vm.Planets.First(p => p.SurfaceGravity is not null);
        Assert.False(vm.HasPlanetWarning);
    }

    // ── mass entry ────────────────────────────────────────────────────────────────────────────

    // ── settings ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void RestoresTheRememberedPlanetOverrideAndRatio()
    {
        var config = ConfigSource.Load();
        var target = new MainWindowViewModel(config).Planets.Last();

        var vm = new MainWindowViewModel(config, new AppSettings
        {
            SelectedPlanetId = target.Id,
            UseCustomGravity = true,
            CustomGravity = 3.71,
            TargetThrustToWeight = 1.6,
        });

        Assert.Equal(target.Id, vm.SelectedPlanet?.Id);
        Assert.True(vm.GravityIsCustom);
        Assert.Equal(3.71, vm.Gravity);
        Assert.Equal(1.6, vm.TargetThrustToWeight);
    }

    [Fact]
    public void AStoredGravityNeverMasksThePlanetsOwn()
    {
        // Only the override is persisted. A stored copy of an extracted gravity would go stale the
        // moment the config is rebuilt and then quietly win over the newer number — so with the
        // override off, the planet's own value must be what shows, whatever the file says.
        var vm = new MainWindowViewModel(
            ConfigSource.Load(),
            new AppSettings { UseCustomGravity = false, CustomGravity = 42 });

        vm.SelectedPlanet = vm.Planets.First(p => p.SurfaceGravity is not null);

        Assert.Equal(vm.SelectedPlanet.SurfaceGravity, vm.Gravity);
        Assert.NotEqual(42, vm.Gravity);
    }

    [Fact]
    public void FallsBackToTheFirstPlanetWhenTheRememberedOneIsGone()
    {
        // Planets can disappear between runs when the config is rebuilt; that must not leave the
        // app with nothing selected and an empty results panel.
        var vm = new MainWindowViewModel(
            ConfigSource.Load(), new AppSettings { SelectedPlanetId = "no-such-planet" });

        Assert.NotNull(vm.SelectedPlanet);
        Assert.Equal(vm.Planets.First().Id, vm.SelectedPlanet!.Id);
    }

    [Fact]
    public void CapturesItsStateBackIntoSettings()
    {
        var vm = Create();
        vm.SelectedPlanet = vm.Planets.Last();
        vm.GravityIsCustom = true;
        vm.CustomGravity = 5.5;
        vm.TargetThrustToWeight = 1.8;

        var settings = new AppSettings();
        vm.CaptureInto(settings);

        Assert.True(settings.UseCustomGravity);
        Assert.Equal(vm.Planets.Last().Id, settings.SelectedPlanetId);
        Assert.Equal(5.5, settings.CustomGravity);
        Assert.Equal(1.8, settings.TargetThrustToWeight);
    }

    [Fact]
    public void EitherRadioOptionCanSelectItsMode()
    {
        // The bug this covers: IsStorageMode was a computed getter with no setter, and the other
        // option was bound to {Binding !IsStorageMode}. Neither could write the mode back, so
        // clicking "Work it out from storage" left the section disabled and the selection unmoved.
        var vm = Create();

        Assert.True(vm.IsDirectMode);
        Assert.False(vm.IsStorageMode);

        vm.IsStorageMode = true;

        Assert.Equal(MassEntryMode.Storage, vm.MassEntryMode);
        Assert.False(vm.IsDirectMode);

        vm.IsDirectMode = true;

        Assert.Equal(MassEntryMode.Direct, vm.MassEntryMode);
        Assert.False(vm.IsStorageMode);
    }

    [Fact]
    public void DeselectingAnOptionDoesNotFlipTheMode()
    {
        // Radio buttons unset the outgoing option before setting the incoming one. Honouring that
        // false would flip the mode and immediately flip it back.
        var vm = Create();
        vm.IsStorageMode = true;

        vm.IsStorageMode = false;

        Assert.Equal(MassEntryMode.Storage, vm.MassEntryMode);
    }

    [Fact]
    public void MassesAreReportedInKilogramsThroughout()
    {
        // The player reads kilograms off the HUD and types kilograms in; every mass we print back
        // must be the same unit, or checking a result means converting in your head.
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Direct;
        vm.DirectMassKg = 500000;

        Assert.Contains("kg", vm.ShipMassText, StringComparison.Ordinal);
        Assert.All(vm.Loads, l => Assert.Contains("kg", l.MassText, StringComparison.Ordinal));

        var feasible = vm.Results.First(r => r.IsFeasible);
        Assert.Contains("kg", feasible.AddedMassText, StringComparison.Ordinal);
        Assert.Contains("kg", feasible.RangeText, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectMassDrivesTheRequirement()
    {
        var vm = Create();
        vm.MassEntryMode = MassEntryMode.Direct;

        vm.DirectMassKg = 500000;
        var lighter = vm.Results.First(r => r.IsFeasible).Count;

        vm.DirectMassKg = 1000000;
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
        vm.HullMassKg = 300000;
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
        vm.HullMassKg = 100000;

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
        vm.HullMassKg = 100000;

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
        vm.DirectMassKg = 500000;

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

        vm.DirectMassKg = 0;

        Assert.Empty(vm.Results);
        Assert.Equal("—", vm.RequirementText);
    }

    [Fact]
    public void AssumptionAboutExistingThrustersIsAlwaysStated()
    {
        Assert.Contains("no thrusters", Create().Assumption, StringComparison.OrdinalIgnoreCase);
    }
}
