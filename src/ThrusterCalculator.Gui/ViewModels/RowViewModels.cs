using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Gui.ViewModels;

/// <summary>A block the user can say how many of they have.</summary>
public sealed partial class StorageRowViewModel : ObservableObject
{
    /// <summary>How many are fitted. <c>null</c> while the field is empty — see
    /// <see cref="ConfiguratorRowViewModel.Count"/> for why it has to be nullable.</summary>
    [ObservableProperty]
    private int? _count = 0;

    /// <summary>The count as the calculation sees it: an empty field fits nothing.</summary>
    public int Fitted => Count is > 0 ? Count.Value : 0;

    /// <inheritdoc cref="ConfiguratorRowViewModel.OnCountChanged"/>
    partial void OnCountChanged(int? value)
    {
        if (value is null) Count = 0;
    }

    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Cargo capacity in kg, for containers. Zero for tanks.</summary>
    public double CapacityKg { get; init; }

    /// <summary>Mass of the block itself, or <c>null</c> when unknown.</summary>
    public double? BlockMassKg { get; init; }

    public bool IsMassKnown => BlockMassKg is not null;

    public string CapacityText => CapacityKg > 0 ? $"{CapacityKg:N0} kg cargo" : string.Empty;

    public string BlockMassText =>
        BlockMassKg is { } m ? $"{m:N0} kg each" : "mass unknown";
}

/// <summary>One proposed thruster loadout.</summary>
public sealed class ThrusterResultViewModel
{
    public required string Name { get; init; }

    public required SizingStatus Status { get; init; }

    /// <summary>Block size in centimetres, which is what the family orders by.</summary>
    public required int SizeCm { get; init; }

    /// <summary>The family this belongs under, e.g. <c>"Atmospheric"</c>.</summary>
    public required string Family { get; init; }

    /// <summary>
    /// Just the size, e.g. <c>"2.5 m"</c> — the family is the group heading above it.
    /// </summary>
    public string SizeText
    {
        get
        {
            if (SizeCm <= 0) return Name;

            // Metres read better than centimetres at these sizes: "2.5 m", not "250 cm".
            var metres = SizeCm / 100.0;
            return metres.ToString(metres % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture)
                   + " m";
        }
    }

    public bool IsFeasible => Status == SizingStatus.Feasible;

    public int Count { get; init; }

    public double AddedMassKg { get; init; }

    public double AchievedThrustToWeight { get; init; }

    public double MaxSupportedShipMassKg { get; init; }

    public double ShipMassKg { get; init; }

    public double? ResourceRateTotal { get; init; }

    public string? ResourceName { get; init; }

    /// <summary>The resource's flow-rate units, as the game states them.</summary>
    public string? ResourceUnits { get; init; }

    public Provenance Provenance { get; init; }

    public string CountText => IsFeasible ? $"{Count} ×" : "—";

    /// <summary>
    /// Mass the loadout adds, in kilograms.
    /// </summary>
    /// <remarks>
    /// Every mass in the UI is kilograms, matching the figure the game shows the player. Mixing
    /// units — kilograms in, tonnes out — would mean converting in your head to check whether a
    /// result is sane, which is exactly when a factor of a thousand slips past unnoticed.
    /// </remarks>
    public string AddedMassText => IsFeasible ? $"+{AddedMassKg:N0} kg" : string.Empty;

    public string RatioText => IsFeasible ? $"{AchievedThrustToWeight:0.00}×" : string.Empty;

    /// <summary>
    /// The band of ship mass this exact loadout covers.
    /// </summary>
    /// <remarks>
    /// Whole thrusters mean the answer has slack, and the upper bound is how much the ship can grow
    /// before it needs re-planning — genuinely useful, and free from the same formula.
    /// </remarks>
    public string RangeText => IsFeasible
        ? $"covers {ShipMassKg:N0}–{MaxSupportedShipMassKg:N0} kg"
        : string.Empty;

    /// <summary>The upper bound alone, which is what the table column shows.</summary>
    public string MaxSupportedText => IsFeasible && !double.IsInfinity(MaxSupportedShipMassKg)
        ? $"{MaxSupportedShipMassKg:N0} kg"
        : string.Empty;

    /// <summary>
    /// The full picture, for the row's tooltip.
    /// </summary>
    /// <remarks>
    /// Headroom is not a column: it is derivable from the covered range, and the table earns its
    /// readability by carrying only what you choose between. It stays reachable rather than cut.
    /// </remarks>
    public string DetailText =>
        IsFeasible ? $"{RangeText} · {HeadroomText}" : StatusText;

    public string HeadroomText
    {
        get
        {
            if (!IsFeasible || ShipMassKg <= 0) return string.Empty;
            var headroom = (MaxSupportedShipMassKg - ShipMassKg) / ShipMassKg;
            return $"{headroom:P0} headroom";
        }
    }

    /// <summary>
    /// Total draw, with units.
    /// </summary>
    /// <remarks>
    /// The units are not decoration. Atmospheric and ion thrusters draw electricity in kilowatts
    /// while hydrogen thrusters draw hydrogen in litres per second, and the raw figures differ by
    /// orders of magnitude — 16 000 against 120 for the two largest. Printing bare numbers in one
    /// column invites reading a hydrogen thruster as wildly more efficient (Research.md §3).
    /// </remarks>
    public string DrawText => ResourceRateTotal is { } rate && ResourceName is not null
        ? $"{rate:N0} {ShortUnits(ResourceUnits) ?? ResourceName}"
        : string.Empty;

    private static string? ShortUnits(string? units) => units switch
    {
        "Kilowatts" => "kW",
        "LitersPerSecond" => "L/s",
        // An unrecognised unit falls back to the resource name rather than being printed raw:
        // "9,375 SomeNewUnitEnumValue" reads as a bug, which it would be ours to notice.
        _ => null,
    };

    /// <summary>Why a loadout is not possible — the useful answer, where a bare 0 is not.</summary>
    public string StatusText => Status switch
    {
        SizingStatus.Feasible => string.Empty,
        SizingStatus.NotImplemented => "not in this build of the game",
        SizingStatus.NoThrustInEnvironment => "no thrust in this atmosphere",
        SizingStatus.CannotLiftOwnWeight => "cannot lift its own weight here",
        SizingStatus.ThrustUnknown => "thrust unknown",
        SizingStatus.MassUnknown => "block mass unknown",
        _ => Status.ToString(),
    };

    public bool IsUncertain => Provenance is not Provenance.Measured;

    public string ProvenanceText => Provenance switch
    {
        Provenance.Measured => string.Empty,
        Provenance.Derived => "derived",
        Provenance.Assumed => "assumed",
        _ => "unknown",
    };
}

/// <summary>
/// One thruster family, with its sizes in ascending order.
/// </summary>
/// <remarks>
/// Grouping rather than tabbing is deliberate. The panel's whole job is letting you weigh an
/// atmospheric loadout against a hydrogen one (Design.md §3.3), and a tab strip puts two thirds of
/// the options behind a click — you would be comparing against memory. Headings give the eye
/// somewhere to rest without hiding anything.
/// </remarks>
public sealed record ThrusterFamilyViewModel(
    string Name, IReadOnlyList<ThrusterResultViewModel> Rows);

/// <summary>
/// One family's worth of configurator rows, split into the two columns it renders as.
/// </summary>
/// <remarks>
/// Split here rather than left to a <c>UniformGrid</c> so a divider can sit between the columns,
/// and so the split cannot silently reflow to one or three columns when the window is resized.
/// Alternating indices keeps the reading order left-to-right across each line — 1 m, 2.5 m on the
/// first, 5 m, 10 m on the second — which is the order sizes are usually compared in.
/// </remarks>
public sealed record ConfiguratorFamilyViewModel(
    string Name, IReadOnlyList<ConfiguratorRowViewModel> Rows)
{
    public IReadOnlyList<ConfiguratorRowViewModel> LeftRows =>
        [.. Rows.Where((_, i) => i % 2 == 0)];

    public IReadOnlyList<ConfiguratorRowViewModel> RightRows =>
        [.. Rows.Where((_, i) => i % 2 == 1)];
}

/// <summary>
/// One thruster type in the configurator, with the count the user has placed.
/// </summary>
/// <remarks>
/// Nothing here distinguishes families: placing two sizes of the same thruster and placing an
/// atmospheric beside an ion are the same operation, because they are the same computation
/// (<c>Core.Loadout</c>). "Mixed types" needed no separate feature.
/// </remarks>
public sealed partial class ConfiguratorRowViewModel : ObservableObject
{
    /// <summary>
    /// How many are placed. <c>null</c> while the field is empty.
    /// </summary>
    /// <remarks>
    /// Nullable because <c>NumericUpDown</c> genuinely has no value when its text is cleared, and
    /// it writes that null straight back. Bound to a non-nullable <c>int</c> the conversion throws
    /// and Avalonia paints <c>System.InvalidCastException</c> into the field — clearing a number to
    /// retype it is an ordinary thing to do, so the model has to admit the empty state exists.
    /// </remarks>
    [ObservableProperty]
    private int? _count = 0;

    /// <summary>The count as the solver sees it: an empty field places nothing.</summary>
    public int Placed => Count is > 0 ? Count.Value : 0;

    /// <summary>
    /// A cleared field settles back to zero rather than staying blank.
    /// </summary>
    /// <remarks>
    /// The property stays nullable because the control genuinely writes null when its text is
    /// emptied, and refusing that is what painted an exception into the box. Coercing here keeps
    /// the model honest and the display readable: a row showing nothing and a row showing 0 mean
    /// the same thing, and only one of them looks broken.
    /// </remarks>
    partial void OnCountChanged(int? value)
    {
        if (value is null) Count = 0;
    }

    /// <summary>What one more of these buys, after its own weight. Updated on every recalculation.</summary>
    [ObservableProperty]
    private double _netContributionN;

    [ObservableProperty]
    private bool _canContribute = true;

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required int SizeCm { get; init; }

    public required string Family { get; init; }

    /// <summary>Just the size — the family is the heading above it.</summary>
    public string SizeText
    {
        get
        {
            if (SizeCm <= 0) return Name;

            var metres = SizeCm / 100.0;
            return metres.ToString(metres % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture)
                   + " m";
        }
    }

    /// <summary>
    /// The honest figure: thrust delivered less the extra requirement its own mass creates.
    /// </summary>
    /// <remarks>
    /// Shown because the shortfall does <em>not</em> fall by a thruster's rated thrust. Add a
    /// 100 kN thruster and the gap closes by perhaps 95 kN; without this number on screen that
    /// reads as broken arithmetic rather than as the physics the whole app exists to model.
    /// </remarks>
    public string NetContributionText => CanContribute
        ? $"+{NetContributionN / 1000:N0} kN each"
        : "cannot lift itself";

    partial void OnNetContributionNChanged(double value) =>
        OnPropertyChanged(nameof(NetContributionText));

    partial void OnCanContributeChanged(bool value) =>
        OnPropertyChanged(nameof(NetContributionText));
}

/// <summary>
/// A table of proposals: feasible ones grouped by family, the rest folded away.
/// </summary>
/// <remarks>
/// Two of these are on screen at once — "if you use one type" against an empty ship, and "to cover
/// what's left" against whatever is placed. Same component, different requirement fed in, which is
/// a decent sign the model underneath is the right shape.
/// </remarks>
public sealed partial class ThrusterTableViewModel : ObservableObject
{
    public ObservableCollection<ThrusterFamilyViewModel> Families { get; } = [];

    public ObservableCollection<ThrusterResultViewModel> Unusable { get; } = [];

    public bool HasUnusable => Unusable.Count > 0;

    public bool IsEmpty => Families.Count == 0;

    /// <summary>One line standing in for the whole unusable set. Collapsed, never hidden.</summary>
    public string UnusableSummary
    {
        get
        {
            if (Unusable.Count == 0) return string.Empty;

            var reasons = Unusable.Select(r => r.StatusText).Distinct(StringComparer.Ordinal).ToList();

            return reasons.Count == 1
                ? $"{Unusable.Count} not usable here — {reasons[0]}"
                : $"{Unusable.Count} not usable here";
        }
    }

    /// <summary>Replaces the contents, keeping the collections the bindings are attached to.</summary>
    public void Set(IEnumerable<ThrusterFamilyViewModel> families, IEnumerable<ThrusterResultViewModel> unusable)
    {
        Families.Clear();
        foreach (var family in families) Families.Add(family);

        Unusable.Clear();
        foreach (var row in unusable) Unusable.Add(row);

        OnPropertyChanged(nameof(HasUnusable));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(UnusableSummary));
    }
}

/// <summary>Total mass under one cargo loading, with tanks always full.</summary>
public sealed record LoadSummary(string Name, double TotalMassKg, bool IsSelected)
{
    public string MassText => $"{TotalMassKg:N0} kg";
}
