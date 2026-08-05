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

    public string CapacityText => CapacityKg > 0 ? $"{CapacityKg / 1000:N1} t cargo" : string.Empty;

    public string BlockMassText =>
        BlockMassKg is { } m ? $"{m:N0} kg each" : "mass unknown";
}

/// <summary>One proposed thruster loadout.</summary>
public sealed class ThrusterResultViewModel
{
    public required string Name { get; init; }

    public required SizingStatus Status { get; init; }

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

    public string AddedMassText => IsFeasible ? $"+{AddedMassKg / 1000:N1} t" : string.Empty;

    public string RatioText => IsFeasible ? $"{AchievedThrustToWeight:0.00}×" : string.Empty;

    /// <summary>
    /// The band of ship mass this exact loadout covers.
    /// </summary>
    /// <remarks>
    /// Whole thrusters mean the answer has slack, and the upper bound is how much the ship can grow
    /// before it needs re-planning — genuinely useful, and free from the same formula.
    /// </remarks>
    public string RangeText => IsFeasible
        ? $"covers {ShipMassKg / 1000:N0}–{MaxSupportedShipMassKg / 1000:N0} t"
        : string.Empty;

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

/// <summary>Total mass under one cargo loading, with tanks always full.</summary>
public sealed record LoadSummary(string Name, double TotalMassKg, bool IsSelected)
{
    public string MassText => $"{TotalMassKg / 1000:N1} t";
}
