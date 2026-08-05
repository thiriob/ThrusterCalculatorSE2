using System.Collections.Generic;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using ThrusterCalculator.Core.Sizing;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Gui.ViewModels;

/// <summary>A block the user can say how many of they have.</summary>
public sealed partial class StorageRowViewModel : ObservableObject
{
    [ObservableProperty]
    private int _count;

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

/// <summary>Total mass under one cargo loading, with tanks always full.</summary>
public sealed record LoadSummary(string Name, double TotalMassKg, bool IsSelected)
{
    public string MassText => $"{TotalMassKg:N0} kg";
}
