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

    /// <summary>Whether the user has chosen to override the planet's stated gravity.</summary>
    [ObservableProperty] private bool _useCustomGravity;

    /// <summary>The override value, used only when it is in force.</summary>
    [ObservableProperty] private double _customGravity = AppSettings.DefaultCustomGravity;

    [ObservableProperty] private double _targetThrustToWeight = 1.0;
    [ObservableProperty] private MassEntryMode _massEntryMode = MassEntryMode.Direct;

    // Kilograms, because that is the unit the game puts on screen. The player reads a number off the
    // bottom-right of the HUD and types it in; asking for tonnes would make them divide first, and a
    // slip of three orders of magnitude is a silent, plausible-looking wrong answer.
    [ObservableProperty] private double _directMassKg = 500_000;
    [ObservableProperty] private double _hullMassKg = 300_000;
    [ObservableProperty] private int _selectedPresetIndex = 2;

    public MainWindowViewModel(LoadedConfig config)
        : this(config, new AppSettings())
    {
    }

    public MainWindowViewModel(LoadedConfig config, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(settings);

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

        // Restore the remembered planet, falling back to the first — a planet can vanish between
        // runs when the config is rebuilt, and that must not leave the app with nothing selected.
        var remembered = Planets.FirstOrDefault(
            p => string.Equals(p.Id, settings.SelectedPlanetId, StringComparison.Ordinal));

        SelectedPlanet = remembered ?? Planets.FirstOrDefault();

        // Only the override is restored, never a gravity we read from the config: a stored copy of
        // an extracted value would go stale the moment the config is rebuilt, and would then
        // quietly win over the newer number. The planet's own value is looked up every time.
        CustomGravity = settings.CustomGravity;
        UseCustomGravity = settings.UseCustomGravity;

        // Ratio is planet-independent, so it always applies.
        TargetThrustToWeight = settings.TargetThrustToWeight;

        Recalculate();
    }

    /// <summary>Copies the remembered state back out, for saving on a clean exit.</summary>
    public void CaptureInto(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.SelectedPlanetId = SelectedPlanet?.Id;
        settings.UseCustomGravity = UseCustomGravity;
        settings.CustomGravity = CustomGravity;
        settings.TargetThrustToWeight = TargetThrustToWeight;
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

    /// <summary>Every sized thruster, flat. The families below are built from this.</summary>
    public ObservableCollection<ThrusterResultViewModel> Results { get; } = [];

    /// <summary>Feasible loadouts, grouped by family — what the panel actually renders.</summary>
    public ObservableCollection<ThrusterFamilyViewModel> Families { get; } = [];

    /// <summary>The ones that cannot work here, folded away behind one line.</summary>
    public ObservableCollection<ThrusterResultViewModel> Unusable { get; } = [];

    public bool HasUnusable => Unusable.Count > 0;

    /// <summary>
    /// One line standing in for the whole unusable set.
    /// </summary>
    /// <remarks>
    /// Collapsed, never hidden. "Does this exist yet, and why can't I use it?" is a real question in
    /// alpha (Design.md §4.4), and on an atmospheric world the ion family being dead is the single
    /// most useful thing the panel says — it just does not need eight rows to say it.
    /// </remarks>
    public string UnusableSummary
    {
        get
        {
            if (Unusable.Count == 0) return string.Empty;

            var reasons = Unusable
                .Select(r => r.StatusText)
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return reasons.Count == 1
                ? $"{Unusable.Count} not usable here — {reasons[0]}"
                : $"{Unusable.Count} not usable here";
        }
    }

    public ObservableCollection<LoadSummary> Loads { get; } = [];

    public IReadOnlyList<string> PresetNames { get; } = [.. Presets.Select(p => p.Name)];

    [ObservableProperty] private string _shipMassText = string.Empty;
    [ObservableProperty] private string _requirementText = string.Empty;

    /// <summary>
    /// Stated permanently rather than in a dismissible dialog: proposals assume the ship has no
    /// thrusters yet, so a mass read off an already-thrusted ship counts them twice.
    /// </summary>
    public string Assumption => "Assumes no thrusters are currently installed.";

    /// <summary>
    /// The two mass-entry radio buttons, as settable properties.
    /// </summary>
    /// <remarks>
    /// These must have setters. An earlier version exposed only <c>IsStorageMode</c> as a computed
    /// getter and bound the other button to <c>{Binding !IsStorageMode}</c> — so neither button
    /// could write the mode back, and clicking "Work it out from storage" appeared to do nothing:
    /// the section stayed disabled and focus stayed on the direct field. A radio group needs a
    /// two-way target per option, and a negated binding is not one.
    /// <para>
    /// Setting to <c>false</c> is ignored on purpose. Radio buttons unset the outgoing option before
    /// setting the incoming one, so honouring the <c>false</c> would flip the mode and immediately
    /// flip it back.
    /// </para>
    /// </remarks>
    public bool IsDirectMode
    {
        get => MassEntryMode == MassEntryMode.Direct;
        set
        {
            if (value) MassEntryMode = MassEntryMode.Direct;
        }
    }

    /// <inheritdoc cref="IsDirectMode"/>
    public bool IsStorageMode
    {
        get => MassEntryMode == MassEntryMode.Storage;
        set
        {
            if (value) MassEntryMode = MassEntryMode.Storage;
        }
    }

    /// <summary>
    /// Gravity actually used, in m/s².
    /// </summary>
    /// <remarks>
    /// Derived rather than stored, so there is exactly one answer to "what gravity is in force"
    /// and the field on screen cannot drift away from the number the maths uses.
    /// </remarks>
    public double Gravity =>
        GravityIsCustom ? CustomGravity : SelectedPlanet?.SurfaceGravity ?? CustomGravity;

    /// <summary>
    /// The planet's own gravity, as a plain figure.
    /// </summary>
    /// <remarks>
    /// This is the planet's number, not the effective one — the custom field below shows the
    /// override, and exactly one of the two is live at a time, the other dimmed. Showing the
    /// effective value here instead would put the same number on screen twice whenever the
    /// override is on, and say nothing about what it replaced.
    /// </remarks>
    public string GravityText =>
        SelectedPlanet?.SurfaceGravity is { } g ? $"{g:0.##} m/s²" : "not stated";

    /// <summary>Whether this planet states a gravity we could fall back to.</summary>
    public bool CanUsePlanetGravity => SelectedPlanet?.SurfaceGravity is not null;

    /// <summary>
    /// Whether the entry field is live — the checkbox's state.
    /// </summary>
    /// <remarks>
    /// Reads as forced-on when the planet states no gravity, which is every planet in a real
    /// extracted config: there is nothing to fall back to, so the value has to come from the user.
    /// The setter records only the user's <em>intent</em>, so visiting an unknown-gravity planet
    /// does not silently turn the override on for every other planet too.
    /// </remarks>
    public bool GravityIsCustom
    {
        get => UseCustomGravity || !CanUsePlanetGravity;
        set => UseCustomGravity = value;
    }

    /// <summary>Whether to show the "this is a guess" caveat.</summary>
    public bool GravityIsAssumed =>
        GravityIsCustom || (SelectedPlanet?.GravityIsAssumed ?? true);

    /// <summary>
    /// What to say about where this gravity came from.
    /// </summary>
    /// <remarks>
    /// The explanation is not decoration: a planet in the list is a <em>generator</em>, and the
    /// world decides how big to build it. Two saves can hold the same planet at different radii
    /// and therefore different surface gravity, so the app has to be clear that its number is for
    /// a default-sized world and point at the reading that settles it.
    /// </remarks>
    public string GravityNote
    {
        // Order matters. When nothing is known the override is forced on, so testing for "custom"
        // first would replace the explanation of *why* we are asking with a bare "your own value"
        // — leaving the user to guess what number to type and why the app cannot supply it.
        get
        {
            if (!CanUsePlanetGravity)
            {
                return "⚠ Not available for this planet. Surface gravity follows from the "
                       + "planet's radius, which a world chooses when it spawns the planet — the "
                       + "definition files describe the shape of the gravity field, never its "
                       + "strength. Stand on the surface and read the G: figure at the bottom "
                       + "right: 1.00 g is 9.81 m/s².";
            }

            if (UseCustomGravity) return "Your own value, used as entered.";

            return "Read from the game's own planet data. Tick Custom if your world differs — a "
                   + "world can spawn a planet at a size of its own choosing.";
        }
    }

    partial void OnSelectedPlanetChanged(PlanetOption? value)
    {
        // Drop the override when the planet changes, so picking a planet visibly changes the
        // gravity in force. Leaving it on made the app look broken: the number never moved, and
        // nothing on screen explained that a custom value was quietly winning.
        //
        // The custom *value* survives — only the tick is cleared — so a user who set one keeps it
        // a click away rather than retyping it.
        UseCustomGravity = false;

        NotifyGravityChanged();
        Recalculate();
    }

    partial void OnUseCustomGravityChanged(bool value)
    {
        NotifyGravityChanged();
        Recalculate();
    }

    partial void OnCustomGravityChanged(double value)
    {
        OnPropertyChanged(nameof(Gravity));
        OnPropertyChanged(nameof(GravityText));
        Recalculate();
    }

    private void NotifyGravityChanged()
    {
        OnPropertyChanged(nameof(Gravity));
        OnPropertyChanged(nameof(GravityText));
        OnPropertyChanged(nameof(CanUsePlanetGravity));
        OnPropertyChanged(nameof(GravityIsCustom));
        OnPropertyChanged(nameof(GravityIsAssumed));
        OnPropertyChanged(nameof(GravityNote));
    }

    partial void OnTargetThrustToWeightChanged(double value) => Recalculate();

    partial void OnDirectMassKgChanged(double value) => Recalculate();

    partial void OnHullMassKgChanged(double value) => Recalculate();

    partial void OnSelectedPresetIndexChanged(int value) => Recalculate();

    partial void OnMassEntryModeChanged(MassEntryMode value)
    {
        // Both, or the button being deselected keeps its filled dot.
        OnPropertyChanged(nameof(IsDirectMode));
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
        ShipMassText = $"{shipMass:N0} kg";

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
            var thruster = _index.Thruster(r.ThrusterId);

            Results.Add(new ThrusterResultViewModel
            {
                Name = r.ThrusterName,
                Family = FamilyOf(thruster),
                SizeCm = thruster?.SizeCm ?? 0,
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

        RebuildFamilies();
    }

    /// <summary>Splits the flat results into families plus the folded-away remainder.</summary>
    private void RebuildFamilies()
    {
        Families.Clear();
        Unusable.Clear();

        foreach (var row in Results.Where(r => !r.IsFeasible))
        {
            Unusable.Add(row);
        }

        // Families lead with the cheapest option they can offer, so the best answer overall is the
        // first row on screen — the same ordering the flat list had, one level up.
        var families = Results
            .Where(r => r.IsFeasible)
            .GroupBy(r => r.Family, StringComparer.Ordinal)
            .OrderBy(g => g.Min(r => r.AddedMassKg))
            .ThenBy(g => g.Key, StringComparer.Ordinal);

        foreach (var family in families)
        {
            // Within a family, ascending size: the progression is the thing worth reading, and it
            // makes "one size up" an adjacent row rather than a search.
            Families.Add(new ThrusterFamilyViewModel(
                family.Key,
                [.. family.OrderBy(r => r.SizeCm).ThenBy(r => r.Name, StringComparer.Ordinal)]));
        }

        OnPropertyChanged(nameof(HasUnusable));
        OnPropertyChanged(nameof(UnusableSummary));
    }

    /// <summary>
    /// The family heading a thruster sits under, from its thrust class.
    /// </summary>
    /// <remarks>
    /// The thrust class is the right grouping key rather than the name: it is what actually decides
    /// whether the thruster works here, and it is data rather than a string prefix that happens to
    /// look right today.
    /// </remarks>
    private static string FamilyOf(Thruster? thruster)
    {
        var thrustClass = thruster?.ThrustClass;
        if (string.IsNullOrEmpty(thrustClass)) return "Other";

        return char.ToUpperInvariant(thrustClass[0]) + thrustClass[1..];
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
            return Math.Max(0, DirectMassKg);
        }

        var total = Math.Max(0, HullMassKg);

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
