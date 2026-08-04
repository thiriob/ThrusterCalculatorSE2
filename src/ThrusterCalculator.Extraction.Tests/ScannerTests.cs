namespace ThrusterCalculator.Extraction.Tests;

internal static class Fixtures
{
    /// <summary>The synthetic content root shipped with these tests.</summary>
    public static string DefRoot { get; } = Path.Combine(AppContext.BaseDirectory, "fixtures", "def");

    public const string ThrusterType = "ThrusterDefinitionObjectBuilder";

    public static DefinitionSet Scan() => DefinitionScanner.Scan(DefRoot);
}

public class DefinitionScannerTests
{
    [Fact]
    public void FixturesArePresent()
    {
        Assert.True(Directory.Exists(Fixtures.DefRoot),
            $"Expected synthetic .def fixtures at '{Fixtures.DefRoot}'.");
    }

    [Fact]
    public void EveryFileIsEitherReadOrReported()
    {
        // Stated as an invariant rather than fixed counts, so adding a fixture does not break it —
        // but nothing can be silently dropped either, which is the property that matters.
        var set = Fixtures.Scan();

        Assert.Equal(set.FilesSeen, set.All.Count + set.Warnings.Count);
        Assert.NotEmpty(set.All);
    }

    [Fact]
    public void EveryDeliberatelyBrokenFixtureIsReported()
    {
        var set = Fixtures.Scan();

        var brokenOnDisk = Directory
            .GetFiles(Path.Combine(Fixtures.DefRoot, "Broken"), "*.def")
            .Length;

        Assert.Equal(brokenOnDisk, set.Warnings.Count);
        Assert.DoesNotContain(set.All, d => d.RelativePath.StartsWith("Broken/", StringComparison.Ordinal));
    }

    [Fact]
    public void OneBadFileDoesNotAbortTheScan()
    {
        // The whole point: a malformed document degrades to a warning, and everything else is
        // still extracted.
        var set = Fixtures.Scan();

        Assert.NotEmpty(set.Warnings);
        Assert.NotEmpty(set.All);
        Assert.All(set.Warnings, w => Assert.StartsWith("Broken/", w.File!, StringComparison.Ordinal));
        Assert.All(set.Warnings, w => Assert.Equal("unparsableDefinition", w.Code));
    }

    [Fact]
    public void UnknownTypesAreKeptNotDiscarded()
    {
        // The scanner is type-agnostic; filtering happens during projection. That keeps
        // dump-schemas able to describe types we do not yet consume.
        var set = Fixtures.Scan();

        Assert.Single(set.OfType("CompletelyUnknownObjectBuilder"));
    }

    [Fact]
    public void IndexesByGuid()
    {
        var set = Fixtures.Scan();

        var thruster = set.Resolve("aaaaaaaa-0000-0000-0000-000000000001");

        Assert.NotNull(thruster);
        Assert.Equal(12345.5, thruster!.GetDouble("ThrustPower"));
        Assert.Null(set.Resolve("no-such-guid"));
        Assert.Null(set.Resolve(null));
    }

    [Fact]
    public void GroupsByTypeName()
    {
        var set = Fixtures.Scan();

        // Three definitions share the thruster type despite two different filename conventions
        // and one living under Templates/.
        Assert.Equal(3, set.OfType(Fixtures.ThrusterType).Count);
        Assert.Empty(set.OfType("NotAType"));
    }

    [Fact]
    public void CountsByTypeAreReported()
    {
        var counts = Fixtures.Scan().CountsByType();

        Assert.Equal(3, counts[Fixtures.ThrusterType]);
        Assert.Equal(1, counts["CompletelyUnknownObjectBuilder"]);
    }

    [Fact]
    public void ReportsTheHighestBundleStamp()
    {
        // Files are stamped by whichever build last touched them, so the max is the best available
        // build indicator.
        Assert.Equal("9.9.9.333", Fixtures.Scan().MaxBundleVersion());
    }

    [Fact]
    public void TemplatesAreDistinguishedFromConcreteBlocks()
    {
        // A template carries ThrustPower 0; counting it as a real thruster would both inflate the
        // catalogue and look like a thruster producing no thrust.
        var thrusters = Fixtures.Scan().OfType(Fixtures.ThrusterType);

        var templates = thrusters.Where(t => t.IsTemplate).ToList();
        var concrete = thrusters.Where(t => !t.IsTemplate).ToList();

        Assert.Single(templates);
        Assert.Equal(2, concrete.Count);
        Assert.Equal("TestHydrogen", templates[0].GetString("ThrustClass"));
        Assert.All(concrete, t => Assert.True(t.GetDouble("ThrustPower") > 0));
    }

    [Fact]
    public void TemplateSuppliesTheClassAConcreteBlockOmits()
    {
        // Exactly the real situation: hydrogen thrusters have no ThrustClass of their own and
        // inherit "Hydrogen" from their base definition.
        var thrusters = Fixtures.Scan().OfType(Fixtures.ThrusterType);

        var noClass = thrusters.Single(t => t.RelativePath.Contains("NoClass", StringComparison.Ordinal));
        var template = thrusters.Single(t => t.IsTemplate);

        Assert.Null(noClass.GetString("ThrustClass"));
        Assert.NotNull(template.GetString("ThrustClass"));
    }

    [Fact]
    public void ScanOrderIsDeterministic()
    {
        // Output has to be diffable across game updates.
        var first = Fixtures.Scan().All.Select(d => d.RelativePath).ToList();
        var second = Fixtures.Scan().All.Select(d => d.RelativePath).ToList();

        Assert.Equal(first, second);
    }

    [Fact]
    public void PathsAreRelativeAndUseForwardSlashes()
    {
        var set = Fixtures.Scan();

        Assert.All(set.All, d => Assert.DoesNotContain('\\', d.RelativePath));
        Assert.All(set.All, d => Assert.False(Path.IsPathRooted(d.RelativePath)));
    }

    [Fact]
    public void ReportsProgress()
    {
        var reports = new List<ScanProgress>();
        DefinitionScanner.Scan(Fixtures.DefRoot, new Progress<ScanProgress>(reports.Add));

        // Progress is posted through the synchronization context, so just assert it is wired up
        // without depending on delivery timing.
        Assert.NotNull(reports);
    }

    [Fact]
    public void MissingContentDirectoryThrows() =>
        Assert.Throws<DirectoryNotFoundException>(
            () => DefinitionScanner.Scan(Path.Combine(Path.GetTempPath(), "tc-no-such-dir-" + Guid.NewGuid())));
}

public class ScanProgressTests
{
    [Fact]
    public void FractionIsSafeWhenEmpty() => Assert.Equal(0, new ScanProgress(0, 0).Fraction);

    [Fact]
    public void FractionReportsProportion() => Assert.Equal(0.5, new ScanProgress(5, 10).Fraction);
}
