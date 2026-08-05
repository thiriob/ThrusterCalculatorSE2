using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using ThrusterCalculator.Core;
using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Gui.Services;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Gui.ViewModels;

/// <summary>How the user is telling us the ship's mass.</summary>
public enum MassEntryMode
{
    /// <summary>They read it off the ship in game. Exact, and always available.</summary>
    Direct,

    /// <summary>They describe the storage they have and we resolve it from game data.</summary>
    Storage,
}

/// <summary>Plan mode: departure planet plus ship mass in, thruster loadouts out.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly GameData _data;
    private readonly GameDataIndex _index;
    private readonly CalculationEngine _engine;
    private readonly ThrusterSizer _sizer;

    /// <summary>Cargo fill for each preset. Tanks are always full — fuel is not optional.</summary>
    private static readonly (string Name, double Fill)[] Presets =
        [("Empty", 0.0), ("Half", 0.5), ("Full", 1.0)];

    [ObservableProperty] private PlanetOption? _selectedPlanet;
    [ObservableProperty] private double _gravity = 9.81;
    [ObservableProperty] private double _targetThrustToWeight = 1.0;
    [ObservableProperty] private MassEntryMode _massEntryMode = MassEntryMode.Direct;
    [ObservableProperty] private double _directMassTonnes = 500;
    [ObservableProperty] private double _hullMassTonnes = 300;
    [ObservableProperty] private int _selectedPresetIndex = 2;

    public MainWindowViewModel(LoadedConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        _data = config.Data;
        _index = new GameDataIndex(_data);
        _engine = CalculationEngine.Create(_data.Models);
        _sizer = new ThrusterSizer(_index, _engine);

        Origin = config.Origin;
        OriginDescription = config.Description;

        Planets = new ObservableCollection<PlanetOption>(BuildPlanetOptions());
        Containers = new ObservableCollection<StorageRowViewModel>(BuildStorage(isTank: false));
        Tanks = new ObservableCollection<StorageRowViewModel>(BuildStorage(isTank: true));

        foreach (var row in Containers.Concat(Tanks))
        {
            row.PropertyChanged += OnStorageChanged;
        }

        SelectedPlanet = Planets.FirstOrDefault();
        Recalculate();
    }

    public ConfigOrigin Origin { get; }

    public string OriginDescription { get; }

    public bool IsSampleData => Origin == ConfigOrigin.Sample;

    public string DataStatus =>
        $"Game build {_data.Source.GameBuild} · {_data.Thrusters.Count} thrusters · "
        + $"extracted {_data.Generator.ExtractedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    public ObservableCollection<PlanetOption> Planets { get; }

    public ObservableCollection<StorageRowViewModel> Containers { get; }

    public ObservableCollection<StorageRowViewModel> Tanks { get; }

    public ObservableCollection<ThrusterResultViewModel> Results { get; } = [];

    public ObservableCollection<LoadSummary> Loads { get; } = [];

    public IReadOnlyList<string> PresetNames { get; } = [.. Presets.Select(p => p.Name)];

    [ObservableProperty] private string _shipMassText = string.Empty;
    [ObservableProperty] private string _requirementText = string.Empty;
    [ObservableProperty] private bool _gravityIsAssumed = true;

    /// <summary>
    /// Stated permanently rather than in a dismissible dialog: proposals assume the ship has no
    /// thrusters yet, so a mass read off an already-thrusted ship counts them twice.
    /// </summary>
    public string Assumption => "Assumes no thrusters are currently installed.";

    public bool IsStorageMode => MassEntryMode == MassEntryMode.Storage;

    partial void OnSelectedPlanetChanged(PlanetOption? value)
    {
        if (value?.SurfaceGravity is { } g) Gravity = g;
        GravityIsAssumed = value?.GravityIsAssumed ?? true;
        Recalculate();
    }

    partial void OnGravityChanged(double value) => Recalculate();

    partial void OnTargetThrustToWeightChanged(double value) => Recalculate();

    partial void OnDirectMassTonnesChanged(double value) => Recalculate();

    partial void OnHullMassTonnesChanged(double value) => Recalculate();

    partial void OnSelectedPresetIndexChanged(int value) => Recalculate();

    partial void OnMassEntryModeChanged(MassEntryMode value)
    {
        OnPropertyChanged(nameof(IsStorageMode));
        Recalculate();
    }

    private void OnStorageChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(StorageRowViewModel.Count)) Recalculate();
    }

    private void Recalculate()
    {
        var preset = Presets[Math.Clamp(SelectedPresetIndex, 0, Presets.Length - 1)];

        Loads.Clear();
        for (var i = 0; i < Presets.Length; i++)
        {
            Loads.Add(new LoadSummary(
                Presets[i].Name, ShipMassKg(Presets[i].Fill), i == SelectedPresetIndex));
        }

        var shipMass = ShipMassKg(preset.Fill);
        ShipMassText = $"{shipMass / 1000:N1} t";

        Results.Clear();

        if (SelectedPlanet is null || Gravity <= 0 || shipMass <= 0)
        {
            RequirementText = "—";
            return;
        }

        var environment = new FlightEnvironment
        {
            GravityMetresPerSecondSquared = Gravity,
            AirDensity = _engine.AirDensityAt(SelectedPlanet.Atmosphere, 1.0),
            GravityProvenance = GravityIsAssumed ? Provenance.Assumed : Provenance.Measured,
            PlanetId = SelectedPlanet.Id,
            PlanetName = SelectedPlanet.Name,
        };

        RequirementText =
            $"{shipMass * Gravity * TargetThrustToWeight / 1000:N0} kN at lift-off";

        var request = new SizingRequest
        {
            ShipMassKg = shipMass,
            Environment = environment,
            TargetThrustToWeight = TargetThrustToWeight,
        };

        // Feasible options first, cheapest in added mass leading — that is the choice being made.
        var sized = _sizer.SizeAll(request)
            .OrderByDescending(r => r.IsFeasible)
            .ThenBy(r => r.AddedMassKg)
            .ThenBy(r => r.ThrusterName, StringComparer.Ordinal);

        foreach (var r in sized)
        {
            var resource = _index.Resource(r.ResourceId);

            Results.Add(new ThrusterResultViewModel
            {
                Name = r.ThrusterName,
                Status = r.Status,
                Count = r.Count,
                AddedMassKg = r.AddedMassKg,
                AchievedThrustToWeight = r.AchievedThrustToWeight,
                MaxSupportedShipMassKg = r.MaxSupportedShipMassKg,
                ShipMassKg = shipMass,
                ResourceRateTotal = r.ResourceRateTotal,
                ResourceName = FriendlyResourceName(resource?.Name),
                ResourceUnits = resource?.FlowRateUnits,
                Provenance = r.Provenance,
            });
        }
    }

    /// <summary>
    /// Ship mass at a given cargo fill, excluding thrusters.
    /// </summary>
    /// <remarks>
    /// Tank contents contribute nothing: gas is massless in SE2, confirmed by watching a tank fill
    /// in game with the ship mass unchanged (Backlog B3). Only the tank's own block mass counts, so
    /// "tanks always full" costs nothing and the presets differ by cargo alone.
    /// </remarks>
    private double ShipMassKg(double cargoFill)
    {
        if (MassEntryMode == MassEntryMode.Direct)
        {
            return Math.Max(0, DirectMassTonnes) * 1000;
        }

        var total = Math.Max(0, HullMassTonnes) * 1000;

        foreach (var row in Containers)
        {
            if (row.Count <= 0) continue;
            total += row.Count * (row.BlockMassKg ?? 0);
            total += row.Count * row.CapacityKg * cargoFill;
        }

        foreach (var row in Tanks)
        {
            if (row.Count <= 0) continue;
            total += row.Count * (row.BlockMassKg ?? 0);
        }

        return total;
    }

    /// <summary>
    /// Trims the game's internal prefix: resources are named <c>ResourceElectricity</c> in data.
    /// </summary>
    private static string? FriendlyResourceName(string? name) =>
        name is not null && name.StartsWith("Resource", StringComparison.Ordinal) && name.Length > 8
            ? name[8..]
            : name;

    private IEnumerable<PlanetOption> BuildPlanetOptions()
    {
        // Playable planets first: only Verdure and Kemik are reachable in the current build, so the
        // common case should not need scrolling. The rest stay listed — during alpha "does this
        // exist yet?" is a real question.
        var playable = new[] { "verdure", "kemik" };

        return _data.Planets
            .Select(p => new PlanetOption
            {
                Id = p.Id,
                Name = p.Name,
                SurfaceGravity = p.SurfaceGravity,
                GravityIsAssumed = p.ProvenanceOf("surfaceGravity") != Provenance.Measured,
                Atmosphere = p.Atmosphere,
            })
            .OrderByDescending(p => Array.IndexOf(playable, p.Id) >= 0)
            .ThenBy(p => p.Name, StringComparer.Ordinal);
    }

    private IEnumerable<StorageRowViewModel> BuildStorage(bool isTank)
    {
        if (isTank)
        {
            return _data.Tanks.Select(t => new StorageRowViewModel
            {
                Id = t.Id,
                Name = t.Name,
                CapacityKg = 0,
                BlockMassKg = _sizer.BlockMassKg(t.Density, t.OccupiedCells),
            }).OrderBy(t => t.Name, StringComparer.Ordinal);
        }

        // Only blocks that are plainly storage; every inventory-bearing block would be noise.
        return _data.Containers
            .Where(c => c.Name.Contains("Container", StringComparison.OrdinalIgnoreCase)
                        || c.Name.Contains("Crate", StringComparison.OrdinalIgnoreCase))
            .Select(c => new StorageRowViewModel
            {
                Id = c.Id,
                Name = c.Name,
                CapacityKg = c.MaxMassKg,
                BlockMassKg = _sizer.BlockMassKg(c.Density, c.OccupiedCells),
            })
            .OrderBy(c => c.CapacityKg);
    }
}

/// <summary>A selectable departure planet.</summary>
public sealed class PlanetOption
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public double? SurfaceGravity { get; init; }

    public bool GravityIsAssumed { get; init; }

    public Atmosphere? Atmosphere { get; init; }

    public override string ToString() => Name;
}
