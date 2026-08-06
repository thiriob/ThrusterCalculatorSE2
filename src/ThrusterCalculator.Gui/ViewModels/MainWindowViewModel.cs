using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ThrusterCalculator.Core;
using ThrusterCalculator.Core.Climb;
using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Gui.Controls;
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
    // Not readonly: a rebuild swaps the config in place rather than restarting the app.
    private GameData _data = null!;
    private GameDataIndex _index = null!;
    private CalculationEngine _engine = null!;
    private ThrusterSizer _sizer = null!;
    private ClimbProfiler _climbProfiler = null!;

    /// <summary>Cargo fill for each preset. Tanks are always full — fuel is not optional.</summary>
    private static readonly (string Name, double Fill)[] Presets =
        [("Empty", 0.0), ("Half", 0.5), ("Full", 1.0)];

    [ObservableProperty] private PlanetOption? _selectedPlanet;

    /// <summary>Whether the user has chosen to override the planet's stated gravity.</summary>
    [ObservableProperty] private bool _useCustomGravity;

    /// <summary>The override value, used only when it is in force.</summary>
    [ObservableProperty] private double? _customGravity = AppSettings.DefaultCustomGravity;

    /// <summary>Whether the user has chosen to override the planet's stated radius.</summary>
    [ObservableProperty] private bool _useCustomRadius;

    /// <summary>The override value in kilometres, used only when it is in force.</summary>
    [ObservableProperty] private double? _customRadiusKm = AppSettings.DefaultCustomRadiusKm;

    [ObservableProperty] private double? _targetThrustToWeight = 1.0;
    [ObservableProperty] private MassEntryMode _massEntryMode = MassEntryMode.Direct;

    // Kilograms, because that is the unit the game puts on screen. The player reads a number off the
    // bottom-right of the HUD and types it in; asking for tonnes would make them divide first, and a
    // slip of three orders of magnitude is a silent, plausible-looking wrong answer.
    [ObservableProperty] private double? _directMassKg = 500_000;
    [ObservableProperty] private double? _hullMassKg = 300_000;
    [ObservableProperty] private int _selectedPresetIndex = 2;

    public MainWindowViewModel(LoadedConfig config)
        : this(config, new AppSettings())
    {
    }

    public MainWindowViewModel(LoadedConfig config, AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(settings);

        Planets = [];
        PlanetItems = [];
        Containers = [];
        Tanks = [];

        Load(config, settings.SelectedPlanetId);

        // Only the override is restored, never a gravity we read from the config: a stored copy of
        // an extracted value would go stale the moment the config is rebuilt, and would then
        // quietly win over the newer number. The planet's own value is looked up every time.
        CustomGravity = settings.CustomGravity;
        UseCustomGravity = settings.UseCustomGravity;

        // Only the override is restored; the planet's own radius is looked up every time, for the
        // same reason gravity is — a cached copy would go stale against a rebuilt config.
        CustomRadiusKm = settings.CustomRadiusKm;
        UseCustomRadius = settings.UseCustomRadius;

        // Ratio is planet-independent, so it always applies.
        TargetThrustToWeight = settings.TargetThrustToWeight;

        Recalculate();
    }

    /// <summary>
    /// Points the view model at a config, rebuilding everything derived from it.
    /// </summary>
    /// <remarks>
    /// Called from the constructor and again after a rebuild, so new game data appears without
    /// restarting. Telling a user to relaunch is asking them to do by hand what the app can do
    /// itself — and it would throw away every number they had typed.
    /// <para>
    /// Collections are refilled rather than replaced, so the bindings attached to them survive.
    /// Selections are restored <em>by id</em>: after a rebuild every object here is new, and the
    /// old instances would match nothing.
    /// </para>
    /// </remarks>
    private void Load(LoadedConfig config, string? selectPlanetId)
    {
        _data = config.Data;
        _index = new GameDataIndex(_data);
        _engine = CalculationEngine.Create(_data.Models);
        _sizer = new ThrusterSizer(_index, _engine);
        _climbProfiler = ClimbProfiler.For(_data);

        Origin = config.Origin;
        OriginDescription = config.Description;

        // Detach first: these rows are about to be discarded, and a live handler on an orphan
        // would keep recalculating for a config nobody is looking at.
        foreach (var row in Containers.Concat(Tanks))
        {
            row.PropertyChanged -= OnStorageChanged;
        }

        // Carry the user's quantities across. They described their ship; a data rebuild should not
        // make them describe it again.
        var counts = Containers.Concat(Tanks)
            .Where(r => r.Fitted > 0)
            .ToDictionary(r => r.Id, r => r.Fitted, StringComparer.Ordinal);

        Refill(Containers, BuildStorage(isTank: false));
        Refill(Tanks, BuildStorage(isTank: true));

        foreach (var row in Containers.Concat(Tanks))
        {
            if (counts.TryGetValue(row.Id, out var count)) row.Count = count;
            row.PropertyChanged += OnStorageChanged;
        }

        // Same detach-carry-refill dance as storage: a rebuild must not lose the loadout the user
        // has been building, nor leave handlers on rows nobody is looking at.
        foreach (var row in ConfiguratorRows) row.PropertyChanged -= OnConfiguratorChanged;

        var placed = ConfiguratorRows
            .Where(r => r.Placed > 0)
            .ToDictionary(r => r.Id, r => r.Placed, StringComparer.Ordinal);

        Refill(ConfiguratorRows, BuildConfiguratorRows());

        foreach (var row in ConfiguratorRows)
        {
            if (placed.TryGetValue(row.Id, out var count)) row.Count = count;
            row.PropertyChanged += OnConfiguratorChanged;
        }

        Refill(ConfiguratorFamilies, ConfiguratorRows
            .GroupBy(r => r.Family, StringComparer.Ordinal)
            .Select(g => new ConfiguratorFamilyViewModel(g.Key, [.. g])));

        Refill(Planets, BuildPlanetOptions());
        Refill(PlanetItems, BuildPlanetItems());

        // A planet can vanish between rebuilds; that must not leave the app with nothing selected.
        SelectedPlanet =
            Planets.FirstOrDefault(p => string.Equals(p.Id, selectPlanetId, StringComparison.Ordinal))
            ?? Planets.FirstOrDefault();

        DescribeConfigAge();

        OnPropertyChanged(nameof(Origin));
        OnPropertyChanged(nameof(OriginDescription));
        OnPropertyChanged(nameof(IsSampleData));
        OnPropertyChanged(nameof(SampleDataAdvice));
        OnPropertyChanged(nameof(DataStatus));
        OnPropertyChanged(nameof(HasExtractionWarnings));
        OnPropertyChanged(nameof(ExtractionWarningSummary));
        OnPropertyChanged(nameof(ConfigIsOutdated));
        OnPropertyChanged(nameof(ConfigOutdatedMessage));
    }

    /// <summary>True when the config was written against an older schema than this build.</summary>
    public bool ConfigIsOutdated { get; private set; }

    /// <summary>What that costs, in terms of things on screen.</summary>
    public string ConfigOutdatedMessage { get; private set; } = string.Empty;

    /// <summary>
    /// Compares the config's schema against this build's and says what is missing because of it.
    /// </summary>
    /// <remarks>
    /// <b>A second, unrelated kind of staleness.</b> <see cref="CheckStalenessAsync"/> asks "has the
    /// game changed since this was extracted?" and needs an install to answer. This asks "has the
    /// app learned to read things this file does not carry?", which the file answers by itself — so
    /// it works on a machine with no game on it, and it fires the moment the app is updated.
    /// <para>
    /// Naming the affected features rather than the version number is the point. "Schema 1.0 &lt;
    /// 1.2" tells a user nothing they can act on; "the climb profile will not draw" tells them
    /// exactly what they are missing and why rebuilding is worth the wait.
    /// </para>
    /// </remarks>
    private void DescribeConfigAge()
    {
        ConfigIsOutdated = false;
        ConfigOutdatedMessage = string.Empty;

        // An unparseable version is not evidence of age — the loader already refused anything with
        // an incompatible major, so whatever reached here is readable. Saying nothing beats guessing.
        if (!SchemaVersion.TryParse(_data.SchemaVersion, out var version)) return;
        if (version.CompareTo(SchemaVersion.Current) >= 0) return;

        var missing = new List<string>();

        if (version.CompareTo(new SchemaVersion(1, 1)) < 0)
        {
            missing.Add("per-planet air density, so an airless world like Palatine will look as "
                        + "though atmospheric thrusters work there");
        }

        if (version.CompareTo(SchemaVersion.GravityFalloffIntroduced) < 0)
        {
            missing.Add("the gravity falloff, so the climb profile cannot be drawn");
        }

        ConfigIsOutdated = true;
        ConfigOutdatedMessage =
            $"This game data was written to schema {version} by an older tc.exe; this app writes "
            + $"{SchemaVersion.Current}. It is missing "
            + string.Join("; and ", missing)
            + ". Press Rebuild to regenerate it — your inputs are kept.";
    }

    private static void Refill<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    /// <summary>
    /// Swaps in a freshly built config without restarting, keeping the user's inputs.
    /// </summary>
    public void Reload(LoadedConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        Load(config, SelectedPlanet?.Id);
        Recalculate();
    }

    /// <summary>Copies the remembered state back out, for saving on a clean exit.</summary>
    public void CaptureInto(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        settings.SelectedPlanetId = SelectedPlanet?.Id;
        settings.UseCustomGravity = UseCustomGravity;
        settings.CustomGravity = Custom;
        settings.UseCustomRadius = UseCustomRadius;
        settings.CustomRadiusKm = CustomRadius;
        settings.TargetThrustToWeight = Ratio;
    }

    public ConfigOrigin Origin { get; private set; }

    public string OriginDescription { get; private set; } = string.Empty;

    public bool IsSampleData => Origin == ConfigOrigin.Sample;

    /// <summary>
    /// How to replace the sample data with real numbers.
    /// </summary>
    /// <remarks>
    /// A release ships no config on purpose, so this banner is the *normal* first-run state rather
    /// than an error — which makes the instruction the most important text in the window. It has to
    /// name the affordance that actually exists: telling someone to run a CLI while a Rebuild
    /// button sits at the bottom of the same window is a needless detour.
    /// </remarks>
    public string SampleDataAdvice => CanRebuild
        ? "Click Rebuild below to read your installed game, then restart."
        : $"Run 'tc extract --out {ConfigSource.FileName}' and put the result beside the app.";

    // ── data panel (Design §4.5.1) ────────────────────────────────────────────────────────────

    /// <summary>Whether a rebuild is possible at all: desktop build, producer bundled.</summary>
    public bool CanRebuild => ProducerProcess.IsAvailable;

    /// <summary>
    /// Why the button is missing, when it is.
    /// </summary>
    /// <remarks>
    /// Design §4.5.1 asks for an explanation rather than a dead button — and rather than silence.
    /// A user who cannot see why rebuilding is unavailable has no way to reach the same outcome;
    /// naming the command they can run themselves does. (Absence with no explanation is the *web*
    /// build's behaviour: different host, different capabilities.)
    /// </remarks>
    public string RebuildUnavailableMessage => CanRebuild
        ? string.Empty
        : $"{ProducerProcess.ExecutableName} is not bundled with this build — run "
          + $"'tc extract --out {ConfigSource.FileName}' and put the result beside the app.";

    public bool ShowRebuildUnavailable => !CanRebuild;

    [ObservableProperty] private bool _isRebuilding;

    [ObservableProperty] private string _dataMessage = string.Empty;

    /// <summary>
    /// Set once the producer says the install no longer matches the loaded config.
    /// </summary>
    /// <remarks>
    /// The whole premise is that the game moves and the numbers move with it (Design P1), so
    /// silently serving a config from before a patch is the one failure that undermines everything
    /// else. The check compares the config's <c>sourceFingerprint</c> against the install's current
    /// one — a directory enumeration, not 17k reads, so it is cheap enough to run on every launch.
    /// </remarks>
    [ObservableProperty] private bool _isStale;

    [ObservableProperty] private string _stalenessMessage = string.Empty;

    private CancellationTokenSource? _rebuild;

    /// <summary>Compares the loaded config against the installed game, in the background.</summary>
    public async Task CheckStalenessAsync()
    {
        if (!CanRebuild || Origin != ConfigOrigin.Extracted) return;

        var install = await ProducerProcess.ReadInstallAsync(CancellationToken.None);

        // No install, or a producer that could not answer: not evidence of staleness. Saying
        // nothing beats crying wolf on a machine that simply has no game on it.
        if (install is null || install.Fingerprint.Length == 0) return;

        if (string.Equals(install.Fingerprint, _data.Source.Fingerprint, StringComparison.Ordinal))
        {
            return;
        }

        IsStale = true;
        StalenessMessage = string.Equals(install.GameBuild, _data.Source.GameBuild, StringComparison.Ordinal)
            ? "Your game's files have changed since this data was extracted."
            : $"Your game is now build {install.GameBuild}; this data is from "
              + $"{_data.Source.GameBuild}.";
    }

    [RelayCommand]
    private async Task RebuildAsync()
    {
        if (IsRebuilding) return;

        _rebuild = new CancellationTokenSource();
        IsRebuilding = true;
        DataMessage = "Reading your installed game…";

        try
        {
            var result = await ProducerProcess.ExtractAsync(_rebuild.Token);

            if (!result.Succeeded)
            {
                DataMessage = result.Message;
                return;
            }

            // Swap the new data in without a restart, keeping the planet the user was looking at
            // and everything they typed.
            Reload(ConfigSource.Load());

            IsStale = false;
            StalenessMessage = string.Empty;
            DataMessage = $"Rebuilt from game build {_data.Source.GameBuild}.";
        }
        catch (OperationCanceledException)
        {
            DataMessage = "Cancelled. The previous data is untouched.";
        }
        finally
        {
            IsRebuilding = false;
            _rebuild?.Dispose();
            _rebuild = null;
        }
    }

    [RelayCommand]
    private void CancelRebuild() => _rebuild?.Cancel();

    public string DataStatus =>
        $"Game build {_data.Source.GameBuild} · {_data.Thrusters.Count} thrusters · "
        + $"extracted {_data.Generator.ExtractedAt.LocalDateTime:yyyy-MM-dd HH:mm}";

    public ObservableCollection<PlanetOption> Planets { get; }

    /// <summary>The dropdown's rows, headings included.</summary>
    public ObservableCollection<PlanetListItem> PlanetItems { get; }

    /// <summary>
    /// The dropdown's selection, kept in step with <see cref="SelectedPlanet"/>.
    /// </summary>
    /// <remarks>
    /// Selecting a heading is rejected here and the previous choice restored, independently of the
    /// style that disables heading containers. Two guards because a failed binding in Avalonia is
    /// silent, and the failure mode — a heading as the departure planet — empties the results.
    /// </remarks>
    [ObservableProperty] private PlanetListItem? _selectedPlanetItem;

    public ObservableCollection<StorageRowViewModel> Containers { get; }

    public ObservableCollection<StorageRowViewModel> Tanks { get; }

    /// <summary>Every sized thruster, flat. The families below are built from this.</summary>
    public ObservableCollection<ThrusterResultViewModel> Results { get; } = [];

    /// <summary>
    /// "If you use one type": every thruster sized alone, against a bare ship.
    /// </summary>
    /// <remarks>
    /// v1's whole answer, kept as a rule of thumb once the configurator becomes the answer. It
    /// ignores whatever is placed on purpose — that is what makes it a reference.
    /// </remarks>
    public ThrusterTableViewModel SingleType { get; } = new();

    /// <summary>
    /// "To cover what's left": the same table, sized against the shortfall the loadout leaves.
    /// </summary>
    public ThrusterTableViewModel Remaining { get; } = new();

    /// <summary>The thruster types the user can place, with their counts.</summary>
    public ObservableCollection<ConfiguratorRowViewModel> ConfiguratorRows { get; } = [];

    /// <summary>The same rows, grouped under a family heading — how the panel renders them.</summary>
    public ObservableCollection<ConfiguratorFamilyViewModel> ConfiguratorFamilies { get; } = [];

    /// <summary>Feasible loadouts, grouped by family — the single-type reference table.</summary>
    public ObservableCollection<ThrusterFamilyViewModel> Families => SingleType.Families;

    /// <summary>The ones that cannot work here, folded away behind one line.</summary>
    public ObservableCollection<ThrusterResultViewModel> Unusable => SingleType.Unusable;

    public bool HasUnusable => SingleType.HasUnusable;

    /// <summary>
    /// One line standing in for the whole unusable set.
    /// </summary>
    /// <remarks>
    /// Collapsed, never hidden. "Does this exist yet, and why can't I use it?" is a real question in
    /// alpha (Design.md §4.4), and on an atmospheric world the ion family being dead is the single
    /// most useful thing the panel says — it just does not need eight rows to say it.
    /// </remarks>
    public string UnusableSummary => SingleType.UnusableSummary;

    // ── climb profile (Roadmap v3) ────────────────────────────────────────

    /// <summary>The computed climb, recomputed with everything else.</summary>
    /// <remarks>
    /// This was a hand-drawn mock until the gravity falloff was extracted and both effectiveness
    /// ramps were confirmed linear against the engine (Backlog B6). Every number here now comes
    /// from the same models the table above uses, evaluated at 240 heights instead of one.
    /// </remarks>
    private ClimbProfile _climb = ClimbProfile.Unavailable(ClimbStatus.NothingToFly);

    /// <summary>Spare acceleration at each height, with altitude as a fraction of the climb.</summary>
    public IReadOnlyList<ClimbSample> ClimbSamples { get; private set; } = [];

    /// <summary>Named heights, ground first — altitude 0 is the bottom of the plot.</summary>
    public IReadOnlyList<ClimbBand> ClimbBands { get; private set; } = [];

    /// <summary>The margin the user asked for, which shrinks with gravity rather than staying flat.</summary>
    /// <remarks>
    /// A target of 1.5 means "half a gravity spare", and half a gravity is a smaller number up high.
    /// Drawing it as a straight line would quietly overstate the requirement at altitude.
    /// </remarks>
    public IReadOnlyList<ClimbSample> ClimbTargetSamples { get; private set; } = [];

    /// <summary>
    /// Right-hand end of the acceleration axis, in m/s². Always zero, which fits it to the data.
    /// </summary>
    /// <remarks>
    /// Spare acceleration spans orders of magnitude between a barely-flying tug and an overpowered
    /// racer, so a fixed axis would either clip the top or squash everything interesting into the
    /// left edge. The cost is that the scale moves as the loadout changes; the named bands and the
    /// zero line stay put, which is what the reader actually navigates by.
    /// </remarks>
    public double ClimbMaxRatio => 0;

    /// <summary>
    /// Where the ship runs out of climb, as a fraction of the plot, for the chart to mark.
    /// </summary>
    public double? ClimbStopAltitude { get; private set; }

    /// <summary>Whether there is a curve to draw at all.</summary>
    public bool HasClimb => _climb.IsAvailable && ClimbSamples.Count > 0;

    public string ClimbCaption { get; private set; } = string.Empty;

    /// <summary>
    /// How to read the picture. Constant, and separate from the verdict on purpose.
    /// </summary>
    /// <remarks>
    /// The verdict changes with every keystroke and is what a returning user actually reads; the
    /// legend never changes and is what a first-time user needs once. Folding them into one
    /// paragraph made the verdict hard to find, which is the thing that had to stay scannable.
    /// </remarks>
    public static string ClimbLegend =>
        "Spare acceleration is thrust ÷ mass − gravity. Zero is the floor; the line is faint "
        + "wherever the ship cannot hold itself up, and dotted where it is the margin you asked "
        + "for. A ship arrives at a faint stretch already moving and may coast through it — "
        + "assuming it climbs straight up and never reaches the game's 300 m/s speed limit.";

    /// <summary>
    /// Recomputes the climb for the current loadout, planet and mass.
    /// </summary>
    /// <remarks>
    /// Altitudes are normalised to a 0–1 fraction of the plotted range because the chart draws a
    /// picture, not a scale — planet radii mean nothing to a player. The named bands carry the
    /// meaning, and once a radius is known they carry a kilometre figure alongside the name.
    /// </remarks>
    private void UpdateClimb(double shipMassKg)
    {
        _climb = SelectedPlanet is null
            ? ClimbProfile.Unavailable(ClimbStatus.NothingToFly)
            : _climbProfiler.Profile(
                SelectedPlanet.Source, _loadout, shipMassKg, GravityIsCustom ? Gravity : null);

        var points = _climb.Points;

        if (!_climb.IsAvailable || points.Count == 0)
        {
            ClimbSamples = [];
            ClimbBands = [];
            ClimbTargetSamples = [];
            ClimbStopAltitude = null;
        }
        else
        {
            var bottom = points[0].DistanceInRadii;
            var span = points[^1].DistanceInRadii - bottom;
            var fraction = span > 0 ? 1.0 / span : 0.0;

            ClimbSamples =
                [.. points.Select(p => new ClimbSample(
                    (p.DistanceInRadii - bottom) * fraction,
                    p.SpareAccelerationMetresPerSecondSquared))];

            // Named heights carry a kilometre figure once the user supplies a radius. The names
            // stay: "atmosphere edge" is what a player navigates by, and 7.5 km alone is not.
            ClimbBands =
                [.. _climb.Markers.Select(m => new ClimbBand(
                    LabelFor(m), (m.DistanceInRadii - bottom) * fraction))];

            // The same subtraction the curve uses, applied to the requested margin: a ratio of R
            // asks for (R−1) gravities of spare acceleration, and gravity is a function of height.
            ClimbTargetSamples =
                [.. points.Select(p => new ClimbSample(
                    (p.DistanceInRadii - bottom) * fraction,
                    (Ratio - 1) * p.GravityMetresPerSecondSquared))];

            // Only worth marking once the ship is moving: a loadout that never leaves the pad stops
            // at the ground, and a dot on the bottom axis says nothing the verdict does not.
            ClimbStopAltitude = _climb.CoastCeilingInRadii is { } stop && stop > bottom
                ? (stop - bottom) * fraction
                : null;
        }

        ClimbCaption = DescribeClimb();

        OnPropertyChanged(nameof(ClimbSamples));
        OnPropertyChanged(nameof(ClimbBands));
        OnPropertyChanged(nameof(ClimbTargetSamples));
        OnPropertyChanged(nameof(ClimbStopAltitude));
        OnPropertyChanged(nameof(ClimbCaption));
        OnPropertyChanged(nameof(HasClimb));
    }

    /// <summary>
    /// What the curve means, in a sentence, including when there is no curve.
    /// </summary>
    /// <remarks>
    /// The unavailable cases say <em>why</em> rather than going blank. "No climb here" reads as a
    /// bug; "this planet states no gravity falloff" reads as data, which is what it is.
    /// </remarks>
    private string DescribeClimb()
    {
        switch (_climb.Status)
        {
            case ClimbStatus.NothingToFly:
                return "Place a thruster to see how high it gets.";

            case ClimbStatus.ConfigPredatesFalloff:
                return "This game data was extracted before the climb profile existed, so it "
                       + "carries no gravity falloff for any planet. Press Rebuild above to "
                       + "regenerate it from your install. Everything else on screen is unaffected.";

            case ClimbStatus.NoFalloffModel:
                return $"{SelectedPlanet?.Name ?? "This planet"} states no gravity falloff, so "
                       + "there is nothing to plot a climb against. Lift-off above is unaffected.";

            case ClimbStatus.UnsupportedGravityShape:
                return $"{SelectedPlanet?.Name ?? "This planet"} does not have a spherical gravity "
                       + "field, so height above the surface is not what its gravity depends on. "
                       + "Drawing a climb here would be about the wrong geometry.";
        }

        // Three clauses, always in the same order: off the ground, through the climb, and once out.
        // A reader comparing two loadouts then compares the same three things in the same places,
        // which a paragraph does not let them do — and the three happen to be exactly what
        // separates the thruster families.
        var caveat = _climb.HasUnknownMass ? " Some placed thruster has unknown mass." : string.Empty;

        return $"{TakeOff()} — {Ascent()}, {InSpace()}.{caveat}";
    }

    /// <summary>How readily it leaves the pad, judged against the gravity it is fighting.</summary>
    /// <remarks>
    /// Relative to local gravity rather than an absolute figure: 2 m/s² spare is a shove on Palatine
    /// and a struggle on Verdure, and the same words must not describe both.
    /// </remarks>
    private string TakeOff()
    {
        var ground = _climb.Points[0];
        var spare = ground.SpareAccelerationMetresPerSecondSquared;

        if (spare <= 0) return "Cannot take off";

        return spare < ground.GravityMetresPerSecondSquared * 0.5
            ? "Difficult take-off"
            : "Easy take-off";
    }

    /// <summary>What happens on the way up.</summary>
    /// <remarks>
    /// Reads the <em>coast</em> ceiling, not the hover ceiling. A ship arrives at the height where
    /// it can no longer hold station already moving, and carries through a shallow dip without
    /// noticing; calling that a stall told a mixed atmospheric/ion ship it was stuck in the
    /// atmosphere when it reached space with 150 m/s to spare.
    /// </remarks>
    private string Ascent()
    {
        var grounded = _climb.CoastCeilingInRadii is { } c
                       && c <= _climb.Points[0].DistanceInRadii;

        if (grounded)
        {
            // Being pinned to the pad and being hopeless are different, and only the floor tells
            // them apart. Ion is the standard case: nothing at all in thick air, useful thrust once
            // the air thins.
            return _climb.HoverFloorInRadii is { } floor
                ? $"would start hovering {BandFor(floor)}"
                : "never flies at any height";
        }

        if (_climb.CoastCeilingInRadii is { } ceiling)
        {
            // Naming both heights when they differ, because the gap between them is the whole
            // story: it kept climbing well past the point where it could still hold itself up.
            return _climb.CoastsThroughADip && _climb.HoverCeilingInRadii is { } lost
                ? $"loses lift {BandFor(lost)} and coasts on to {BandFor(ceiling)}{SizeCondition()}"
                : $"stalls {BandFor(ceiling)}";
        }

        return _climb.CoastsThroughADip
            ? $"coasts through a dip and climbs clear{SizeCondition()}"
            : "climbs clear";
    }

    /// <summary>
    /// The planet size a coast depends on, stated because it cannot be resolved.
    /// </summary>
    /// <remarks>
    /// Every other number here is independent of planet radius, which is why the climb works at all
    /// without one. Coasting is the exception: the engine caps ships at a fixed speed, so the energy
    /// available to cross a dip is fixed while the energy a dip costs grows with the planet. The
    /// same loadout gets through on a small world and does not on a large one.
    /// <para>
    /// Stating the threshold beats picking a radius and hoping. It is one clause, it is true, and it
    /// is the most concrete reason yet to go and measure a planet (Backlog B7).
    /// </para>
    /// </remarks>
    private string SizeCondition()
    {
        if (_climb.CoastRadiusLimitMetres is not { } limit) return string.Empty;

        // With a radius supplied there is nothing conditional left: either the ship has the speed
        // to cross the dip on this planet or it does not, and saying which beats stating a rule.
        if (RadiusMetres is { } radius)
        {
            return radius <= limit
                ? string.Empty
                : $" — except this planet is too large for that at {radius / 1000:0.#} km, so it "
                  + $"stops instead (it would need to be under {limit / 1000:N0} km)";
        }

        return $", if the planet's radius is under {limit / 1000:N0} km";
    }

    /// <summary>
    /// How briskly it accelerates once out of the gravity well, in words plus the figure.
    /// </summary>
    /// <remarks>
    /// Absolute m/s² here, unlike <see cref="TakeOff"/>: there is no gravity left to be relative to,
    /// and this is simply how fast the ship gets moving. It is the number that separates a hydrogen
    /// ship from an ion one, and it stays meaningful for a loadout that never reaches space — that
    /// is what the faint part of the curve is.
    /// </remarks>
    private string InSpace()
    {
        var spare = _climb.Points[^1].SpareAccelerationMetresPerSecondSquared;

        // Atmospheric thrusters land exactly here: zero thrust in vacuum, so the ship cannot move
        // at all once out. Worth saying outright rather than printing "0 m/s²".
        if (spare <= 0.05) return "with no thrust of its own in space";

        var word = spare switch
        {
            < 0.5 => "barely moves",
            < 2.0 => "sluggish",
            < 10.0 => "responsive",
            _ => "very responsive",
        };

        return $"{word} in space ({spare:0.#} m/s²)";
    }

    /// <summary>Radius in metres, from the user's kilometres, or <c>null</c> if not supplied.</summary>
    /// <summary>The override value, defaulted so an empty field cannot mean zero.</summary>
    private double CustomRadius => CustomRadiusKm is > 0
        ? CustomRadiusKm.Value
        : AppSettings.DefaultCustomRadiusKm;

    /// <summary>Radius actually in force, in metres, or <c>null</c> if there is none to use.</summary>
    private double? RadiusMetres =>
        RadiusIsCustom ? CustomRadius * 1000 : SelectedPlanet?.Source.RadiusMetres;

    /// <summary>The planet's own radius, as a plain figure.</summary>
    public string RadiusText =>
        SelectedPlanet?.Source.RadiusMetres is { } r ? $"{r / 1000:0.##} km" : "not stated";

    /// <summary>Whether this planet states a radius we could fall back to.</summary>
    public bool CanUsePlanetRadius => SelectedPlanet?.Source.RadiusMetres is not null;

    /// <summary>
    /// Whether the radius field is live — the checkbox's state.
    /// </summary>
    /// <remarks>
    /// Same shape as <see cref="GravityIsCustom"/>, including the forced-on case: a planet that
    /// states no radius has nothing to fall back to, so the value has to come from the user. The
    /// setter records only intent, so visiting such a planet does not turn the override on
    /// everywhere.
    /// </remarks>
    public bool RadiusIsCustom
    {
        get => UseCustomRadius || !CanUsePlanetRadius;
        set => UseCustomRadius = value;
    }

    /// <summary>Ground sits above the reference sphere, so altitude is not measured from radius R.</summary>
    private double GroundOffset => SelectedPlanet?.Source.GroundOffsetInRadii ?? 0.0;

    private void NotifyRadiusChanged()
    {
        OnPropertyChanged(nameof(RadiusText));
        OnPropertyChanged(nameof(CanUsePlanetRadius));
        OnPropertyChanged(nameof(RadiusIsCustom));
        OnPropertyChanged(nameof(RadiusNote));
    }

    /// <summary>A marker's name, with its height in kilometres when that can be worked out.</summary>
    private string LabelFor(ClimbMarker marker) =>
        RadiusMetres is { } radius
            ? $"{marker.Label}  {(marker.DistanceInRadii - 1.0 - GroundOffset) * radius / 1000:0.#} km"
            : marker.Label;

    /// <summary>Describes a height by the named bands around it, since radii mean nothing.</summary>
    private string BandFor(double distanceInRadii)
    {
        var below = _climb.Markers.LastOrDefault(m => m.DistanceInRadii < distanceInRadii);
        var above = _climb.Markers.FirstOrDefault(m => m.DistanceInRadii >= distanceInRadii);

        return (below, above) switch
        {
            (not null, not null) => $"between {below.Label.ToLowerInvariant()} and "
                                    + above.Label.ToLowerInvariant(),
            (not null, null) => $"above {below.Label.ToLowerInvariant()}",
            (null, not null) => $"below {above.Label.ToLowerInvariant()}",
            _ => "an unnamed height",
        };
    }

    // ── configurator ──────────────────────────────────────────────────────────────────────────

    /// <summary>What the user has placed so far.</summary>
    private Loadout _loadout = Loadout.Empty;

    /// <summary>Thrust the placed loadout still leaves to find, and how close it is.</summary>
    [ObservableProperty] private double _loadoutFraction;

    [ObservableProperty] private string _loadoutSummary = string.Empty;

    [ObservableProperty] private string _shortfallText = string.Empty;

    [ObservableProperty] private bool _loadoutIsSatisfied;

    [ObservableProperty] private bool _loadoutHasUnknownMass;

    public bool HasPlacedThrusters => !_loadout.IsEmpty;

    [RelayCommand]
    private void ClearLoadout()
    {
        foreach (var row in ConfiguratorRows) row.Count = 0;
    }

    private void OnConfiguratorChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConfiguratorRowViewModel.Count)) Recalculate();
    }

    public ObservableCollection<LoadSummary> Loads { get; } = [];

    public IReadOnlyList<string> PresetNames { get; } = [.. Presets.Select(p => p.Name)];

    /// <summary>
    /// The results heading, naming the load the figures are for.
    /// </summary>
    /// <remarks>
    /// In storage mode three load masses are on screen and the configurations silently use one of
    /// them. Saying which removes a real misread — sizing for an empty ship and believing it was
    /// the full one is precisely the failure this app exists to prevent (Design §3.2).
    /// </remarks>
    public string ConfigurationsHeading => IsStorageMode
        ? $"IF YOU USE ONE TYPE · {Presets[Math.Clamp(SelectedPresetIndex, 0, Presets.Length - 1)].Name.ToUpperInvariant()} LOAD"
        : "IF YOU USE ONE TYPE";

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
    private double Custom => CustomGravity ?? AppSettings.DefaultCustomGravity;

    public double Gravity =>
        GravityIsCustom ? Custom : SelectedPlanet?.SurfaceGravity ?? Custom;

    /// <summary>
    /// The planet's own gravity, as a plain figure.
    /// </summary>
    /// <remarks>
    /// This is the planet's number, not the effective one — the custom field below shows the
    /// override, and exactly one of the two is live at a time, the other dimmed. Showing the
    /// effective value here instead would put the same number on screen twice whenever the
    /// override is on, and say nothing about what it replaced.
    /// </remarks>
    /// <summary>Target ratio as the maths sees it; an empty field means "just hover".</summary>
    private double Ratio => TargetThrustToWeight is > 0 ? TargetThrustToWeight.Value : 1.0;

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
    /// <summary>
    /// Extraction warnings about the selected planet, shown where they bite.
    /// </summary>
    /// <remarks>
    /// The producer records these faithfully and the config carries them; until now the app dropped
    /// them on the floor. Schema §6 calls surfacing them "the single cheapest defence against the
    /// failure mode that actually matters here" — a degraded extraction producing confident wrong
    /// answers. Shown against the planet rather than in a list, because that is the moment the
    /// warning changes what you should believe.
    /// </remarks>
    public string PlanetWarningText
    {
        get
        {
            if (SelectedPlanet is null) return string.Empty;

            var details = _data.Warnings
                .Where(w => string.Equals(w.Subject, SelectedPlanet.Id, StringComparison.Ordinal))
                .Select(w => w.Detail)
                .ToList();

            return details.Count == 0 ? string.Empty : "⚠ " + string.Join("  ", details);
        }
    }

    public bool HasPlanetWarning => PlanetWarningText.Length > 0;

    /// <summary>Everything the extraction flagged that is not about one entity we show.</summary>
    public string ExtractionWarningSummary
    {
        get
        {
            var count = _data.Warnings.Count;
            if (count == 0) return string.Empty;

            var codes = _data.Warnings
                .Select(w => w.Code)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal);

            return $"{count} extraction note{(count == 1 ? string.Empty : "s")}: "
                   + string.Join(", ", codes);
        }
    }

    public bool HasExtractionWarnings => _data.Warnings.Count > 0;

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

    partial void OnSelectedPlanetItemChanged(PlanetListItem? value)
    {
        if (value is null) return;

        if (value.Planet is null)
        {
            // A heading. Put the selection back where it was rather than leaving the app with no
            // planet — the user clicked a divider, which should read as "nothing happened".
            SelectedPlanetItem = PlanetItems.FirstOrDefault(i => i.Planet == SelectedPlanet);
            return;
        }

        SelectedPlanet = value.Planet;
    }

    partial void OnSelectedPlanetChanged(PlanetOption? value)
    {
        // Keep the dropdown in step when the planet is set directly, as tests and settings do.
        SelectedPlanetItem = PlanetItems.FirstOrDefault(i => i.Planet == value);

        // Drop the override when the planet changes, so picking a planet visibly changes the
        // gravity in force. Leaving it on made the app look broken: the number never moved, and
        // nothing on screen explained that a custom value was quietly winning.
        //
        // The custom *value* survives — only the tick is cleared — so a user who set one keeps it
        // a click away rather than retyping it.
        UseCustomGravity = false;
        UseCustomRadius = false;

        NotifyGravityChanged();
        NotifyRadiusChanged();
        Recalculate();
    }

    partial void OnUseCustomGravityChanged(bool value)
    {
        NotifyGravityChanged();
        Recalculate();
    }

    partial void OnCustomGravityChanged(double? value)
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
        OnPropertyChanged(nameof(PlanetWarningText));
        OnPropertyChanged(nameof(HasPlanetWarning));
    }

    partial void OnUseCustomRadiusChanged(bool value)
    {
        NotifyRadiusChanged();
        Recalculate();
    }

    partial void OnCustomRadiusKmChanged(double? value)
    {
        NotifyRadiusChanged();
        Recalculate();
    }

    /// <summary>Explains what the radius buys, and how to get one.</summary>
    public string RadiusNote =>
        RadiusIsCustom
            ? CanUsePlanetRadius
                ? "Your own value. Heights on the climb use it."
                : "This planet states no radius, so heights are named rather than measured until "
                  + "you give one. To measure it: hover, note your altitude and the HUD's G:."
            : "Read from the game's own planet data. A world can spawn a planet at a size of its "
              + "own choosing, so tick Custom if yours differs.";

    partial void OnTargetThrustToWeightChanged(double? value) => Recalculate();

    partial void OnDirectMassKgChanged(double? value) => Recalculate();

    partial void OnHullMassKgChanged(double? value) => Recalculate();

    partial void OnSelectedPresetIndexChanged(int value)
    {
        OnPropertyChanged(nameof(ConfigurationsHeading));
        Recalculate();
    }

    partial void OnMassEntryModeChanged(MassEntryMode value)
    {
        // Both, or the button being deselected keeps its filled dot.
        OnPropertyChanged(nameof(IsDirectMode));
        OnPropertyChanged(nameof(IsStorageMode));
        OnPropertyChanged(nameof(ConfigurationsHeading));
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
            $"{shipMass * Gravity * Ratio / 1000:N0} kN at lift-off";

        _loadout = new Loadout(
            ConfiguratorRows.Select(r => new PlacedThruster(r.Id, r.Placed)));

        var bare = new SizingRequest
        {
            ShipMassKg = shipMass,
            Environment = environment,
            TargetThrustToWeight = Ratio,
        };

        var withLoadout = bare with { Placed = _loadout };

        // The reference table ignores what is placed, on purpose: it answers "if you used only
        // this", which is the rule of thumb you check the configurator against.
        foreach (var row in Project(_sizer.SizeAll(bare), shipMass)) Results.Add(row);
        Split(Results, SingleType);

        // The working table answers the different question — what closes the remaining gap.
        Split(Project(_sizer.SizeAll(withLoadout), shipMass).ToList(), Remaining);

        UpdateConfigurator(withLoadout);
        UpdateClimb(shipMass);
    }

    /// <summary>Turns sizing results into rows, feasible first and cheapest leading.</summary>
    private IEnumerable<ThrusterResultViewModel> Project(
        IEnumerable<ThrusterSizing> sized, double shipMassKg) =>
        sized
            .OrderByDescending(r => r.IsFeasible)
            .ThenBy(r => r.AddedMassKg)
            .ThenBy(r => r.ThrusterName, StringComparer.Ordinal)
            .Select(r =>
            {
                var resource = _index.Resource(r.ResourceId);
                var thruster = _index.Thruster(r.ThrusterId);

                return new ThrusterResultViewModel
                {
                    Name = r.ThrusterName,
                    Family = FamilyOf(thruster),
                    SizeCm = thruster?.SizeCm ?? 0,
                    Status = r.Status,
                    Count = r.Count,
                    AddedMassKg = r.AddedMassKg,
                    AchievedThrustToWeight = r.AchievedThrustToWeight,
                    MaxSupportedShipMassKg = r.MaxSupportedShipMassKg,
                    ShipMassKg = shipMassKg,
                    ResourceRateTotal = r.ResourceRateTotal,
                    ResourceName = FriendlyResourceName(resource?.Name),
                    ResourceUnits = resource?.FlowRateUnits,
                    Provenance = r.Provenance,
                };
            });

    /// <summary>Updates the placed rows and the shortfall the loadout still leaves.</summary>
    private void UpdateConfigurator(SizingRequest withLoadout)
    {
        var totals = _sizer.Evaluate(withLoadout);

        foreach (var row in ConfiguratorRows)
        {
            var thruster = _index.Thruster(row.Id);
            if (thruster is null) continue;

            var sizing = _sizer.Size(thruster, withLoadout);

            row.CanContribute = sizing.IsFeasible;
            row.NetContributionN = sizing.NetContributionNEach;
        }

        LoadoutFraction = Math.Clamp(totals.Fraction, 0, 1);
        LoadoutIsSatisfied = totals.IsSatisfied;
        LoadoutHasUnknownMass = totals.HasUnknownMass;

        LoadoutSummary = totals.ThrusterCount == 0
            ? "Nothing placed yet."
            : $"{totals.ThrusterCount} thrusters · +{totals.AddedMassKg:N0} kg · "
              + $"{totals.EffectiveThrustN / 1000:N0} of {totals.RequiredThrustN / 1000:N0} kN";

        ShortfallText = totals.IsSatisfied
            ? "Lifts off."
            : $"{totals.RemainingThrustN / 1000:N0} kN still needed";

        OnPropertyChanged(nameof(HasPlacedThrusters));
    }

    /// <summary>Splits rows into families plus the folded-away remainder, into a table.</summary>
    private static void Split(IReadOnlyList<ThrusterResultViewModel> rows, ThrusterTableViewModel table)
    {
        // Families lead with the cheapest option they can offer, so the best answer overall is the
        // first row on screen.
        var families = rows
            .Where(r => r.IsFeasible)
            .GroupBy(r => r.Family, StringComparer.Ordinal)
            .OrderBy(g => g.Min(r => r.AddedMassKg))
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            // Within a family, ascending size: the progression is the thing worth reading, and it
            // makes "one size up" an adjacent row rather than a search.
            .Select(g => new ThrusterFamilyViewModel(
                g.Key,
                [.. g.OrderBy(r => r.SizeCm).ThenBy(r => r.Name, StringComparer.Ordinal)]));

        table.Set(families, rows.Where(r => !r.IsFeasible));
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
            return Math.Max(0, DirectMassKg ?? 0);
        }

        var total = Math.Max(0, HullMassKg ?? 0);

        foreach (var row in Containers)
        {
            if (row.Fitted <= 0) continue;
            total += row.Fitted * (row.BlockMassKg ?? 0);
            total += row.Fitted * row.CapacityKg * cargoFill;
        }

        foreach (var row in Tanks)
        {
            if (row.Fitted <= 0) continue;
            total += row.Fitted * (row.BlockMassKg ?? 0);
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

    /// <summary>Heading text per group, in the order the enum declares them.</summary>
    private static string HeadingFor(PlanetAvailability availability) => availability switch
    {
        PlanetAvailability.Playable => "—— Playable ——",
        PlanetAvailability.Custom => "—— Custom ——",
        PlanetAvailability.Older => "—— Older milestones ——",
        _ => "—— Not in this build yet ——",
    };

    /// <summary>
    /// The dropdown's rows: a heading per non-empty group, then its planets.
    /// </summary>
    /// <remarks>
    /// Headings appear only for groups that have members, so an install with no modded planets
    /// does not show an empty "Custom" divider.
    /// </remarks>
    private IEnumerable<PlanetListItem> BuildPlanetItems()
    {
        foreach (var group in Planets
                     .GroupBy(p => p.Availability)
                     .OrderBy(g => g.Key))
        {
            yield return new PlanetListItem { Label = HeadingFor(group.Key) };

            foreach (var planet in group)
            {
                yield return new PlanetListItem { Planet = planet, Label = planet.Name };
            }
        }
    }

    private IEnumerable<PlanetOption> BuildPlanetOptions()
    {
        // Which planets are reachable is derived, never listed: a planet is playable when its
        // milestone matches the milestone of the build this config came from. An earlier version
        // hardcoded {verdure, kemik} here, which was a second source of truth and — by the time
        // anyone checked — wrong, since Caligo and Palatine are reachable too.
        var build = _data.Source.GameBuild;

        return _data.Planets
            .Select(p => new PlanetOption
            {
                Id = p.Id,
                Name = p.Name,
                Availability = PlanetAvailabilityRules.Classify(p.Milestone, build),

                SurfaceGravity = p.SurfaceGravity,
                GravityIsAssumed = p.ProvenanceOf("surfaceGravity") != Provenance.Measured,
                Atmosphere = p.Atmosphere,
                Source = p,
            })
            .OrderBy(p => p.Availability)
            .ThenBy(p => p.Name, StringComparer.Ordinal);
    }

    /// <summary>
    /// Every thruster the user could place, in the same order the reference table groups them.
    /// </summary>
    /// <remarks>
    /// Unimplemented blocks are excluded here, unlike in the reference table: "does this exist
    /// yet?" is worth answering, but offering a spinner for something that cannot be built is not.
    /// </remarks>
    private IEnumerable<ConfiguratorRowViewModel> BuildConfiguratorRows() =>
        _data.Thrusters
            .Where(t => t.Implemented)
            .Select(t => new ConfiguratorRowViewModel
            {
                Id = t.Id,
                Name = t.Name,
                SizeCm = t.SizeCm,
                Family = FamilyOf(t),
            })
            .OrderBy(r => r.Family, StringComparer.Ordinal)
            .ThenBy(r => r.SizeCm);

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

/// <summary>
/// One row of the planet dropdown: either a group heading or a planet.
/// </summary>
/// <remarks>
/// A heading is not selectable. The <c>ComboBox</c> style disables its container, and the view
/// model bounces the selection back as well — a style that silently failed to apply would
/// otherwise let a heading become the departure planet, which has no gravity and no atmosphere and
/// would empty the results panel. Bindings fail quietly in Avalonia, so the guard is not paranoia.
/// </remarks>
public sealed class PlanetListItem
{
    public PlanetOption? Planet { get; init; }

    public required string Label { get; init; }

    public bool IsSelectable => Planet is not null;

    public override string ToString() => Label;
}

/// <summary>A selectable departure planet.</summary>
public sealed class PlanetOption
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public double? SurfaceGravity { get; init; }

    /// <summary>Which group this planet is listed under.</summary>
    public PlanetAvailability Availability { get; init; }

    public bool GravityIsAssumed { get; init; }

    public Atmosphere? Atmosphere { get; init; }

    /// <summary>
    /// The config entry this option projects.
    /// </summary>
    /// <remarks>
    /// Carried whole because the climb profile needs the gravity falloff, and copying four more
    /// fields onto this projection would make it a second, drifting copy of <see cref="Planet"/>.
    /// </remarks>
    public required Planet Source { get; init; }

    public override string ToString() => Name;
}
