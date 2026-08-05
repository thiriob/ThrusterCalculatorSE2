using ThrusterCalculator.Core;

namespace ThrusterCalculator.Core.Tests;

/// <summary>
/// The rule that decides which planets belong to the build you are playing.
/// </summary>
/// <remarks>
/// Groundwork for grouping the planet dropdown. Kept tested rather than left to the day it is
/// wired up, because the interesting part is the rule, not the list control.
/// </remarks>
public class PlanetAvailabilityTests
{
    [Theory]
    [InlineData("2.3.0.2788", "VS2_3")]
    [InlineData("2.2.0.540", "VS2_2")]
    [InlineData("1.5", "VS1_5")]
    [InlineData("unknown", null)]
    [InlineData("", null)]
    public void DerivesTheMilestoneFromTheBuildString(string build, string? expected) =>
        Assert.Equal(expected, PlanetAvailabilityRules.MilestoneOfBuild(build));

    [Theory]
    // The four reachable on 2.3.0: authored for this milestone.
    [InlineData("VS2_3", PlanetAvailability.Playable)]
    // Legacy — where the generic archetypes and Geomeles land.
    [InlineData("VS1_5", PlanetAvailability.Older)]
    [InlineData("VS2_2", PlanetAvailability.Older)]
    // Data ships ahead of the content; Byblos is the water milestone.
    [InlineData("VS3_0", PlanetAvailability.Upcoming)]
    public void ClassifiesAgainstTheBuildsOwnMilestone(string milestone, PlanetAvailability expected) =>
        Assert.Equal(expected, PlanetAvailabilityRules.Classify(milestone, "2.3.0.2788"));

    [Fact]
    public void MilestonesAreComparedNumericallyNotAsText()
    {
        // VS2_10 sorts before VS2_2 as a string, which would file a current planet as legacy.
        Assert.Equal(PlanetAvailability.Playable,
            PlanetAvailabilityRules.Classify("VS2_10", "2.10.0.1"));
        Assert.Equal(PlanetAvailability.Upcoming,
            PlanetAvailabilityRules.Classify("VS2_10", "2.2.0.1"));
    }

    [Fact]
    public void AnUnversionedPlanetIsCustomRatherThanLegacy()
    {
        // A modded or hand-written planet made no claim about Keen's roster, so neither should we.
        Assert.Equal(PlanetAvailability.Custom,
            PlanetAvailabilityRules.Classify(null, "2.3.0.2788"));
    }

    [Fact]
    public void AnUnreadableBuildDoesNotFileTheWholeRosterAsLegacy()
    {
        // "We don't know" beats confidently demoting every planet.
        Assert.Equal(PlanetAvailability.Custom,
            PlanetAvailabilityRules.Classify("VS2_3", "not-a-version"));
    }
}
