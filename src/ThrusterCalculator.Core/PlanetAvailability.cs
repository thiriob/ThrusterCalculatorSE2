using System.Globalization;

namespace ThrusterCalculator.Core;

/// <summary>Whether a planet belongs to the build you are actually playing.</summary>
public enum PlanetAvailability
{
    /// <summary>Authored for the milestone this build ships — reachable in game.</summary>
    Playable,

    /// <summary>Ships as data but belongs to an older or unreleased milestone.</summary>
    Other,

    /// <summary>Not milestone-versioned at all: added by a mod, or hand-written into the config.</summary>
    Custom,
}

/// <summary>
/// Sorts planets into the build's own, everything else, and anything unversioned.
/// </summary>
/// <remarks>
/// Derived, never hardcoded. Ten planets ship as data and four are reachable; an earlier version
/// carried the reachable ones as a literal list in the view model, which was both a second source
/// of truth and — by the time anyone checked — wrong, naming only Verdure and Kemik.
/// <para>
/// The rule: a planet is playable when its newest milestone matches the milestone of the game build
/// the config was extracted from. Keen re-authors the current roster for each milestone, so a
/// planet untouched since <c>VS1_5</c> is legacy and one authored for <c>VS3_0</c> is not shipped
/// yet. Both fall out of the same comparison, and it re-derives itself when the game moves on.
/// </para>
/// <para>
/// <b>Corroborated independently.</b> The <c>Worlds\Encounters\</c> folder holds hand-authored
/// surface content named per planet — <c>VerdureEncounter_*</c>, <c>KemikEncounter_*</c>,
/// <c>CaligoEncounter_*</c>, <c>PalatineEncounter_*</c> — and that set is exactly the four this
/// rule selects, from a completely unrelated part of the install. Two signals agreeing is the
/// standard this project holds itself to (Research §4.0.0).
/// </para>
/// <para>
/// <b>Why a heuristic is acceptable here, when it was not for density.</b> A misclassification
/// moves a planet between headings in a dropdown. Nothing is hidden, every planet stays selectable,
/// and no number changes — so the worst case is visible and harmless, unlike the slot-signature
/// rule that silently produced wrong masses (Technic §7.2.2).
/// </para>
/// </remarks>
public static class PlanetAvailabilityRules
{
    /// <summary>
    /// The milestone folder name a build belongs to, e.g. <c>"2.3.0.2788"</c> → <c>"VS2_3"</c>.
    /// </summary>
    /// <returns><c>null</c> when the build string is not in the expected shape.</returns>
    public static string? MilestoneOfBuild(string? gameBuild)
    {
        if (string.IsNullOrWhiteSpace(gameBuild)) return null;

        var parts = gameBuild.Split('.');
        if (parts.Length < 2) return null;

        return int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
               && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
            ? $"VS{major}_{minor}"
            : null;
    }

    /// <summary>Classifies one planet against the build its config came from.</summary>
    public static PlanetAvailability Classify(string? planetMilestone, string? gameBuild)
    {
        // No milestone at all means the planet did not come from Keen's versioned folders — a mod,
        // or a hand-edited config. Neither claim about the shipped roster applies to it.
        if (string.IsNullOrWhiteSpace(planetMilestone)) return PlanetAvailability.Custom;

        var current = MilestoneOfBuild(gameBuild);

        // An unreadable build string is not evidence that every planet is legacy. Saying "we don't
        // know" beats confidently filing the whole roster under Other.
        if (current is null) return PlanetAvailability.Custom;

        return string.Equals(planetMilestone, current, StringComparison.OrdinalIgnoreCase)
            ? PlanetAvailability.Playable
            : PlanetAvailability.Other;
    }
}
