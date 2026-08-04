namespace ThrusterCalculator.Extraction.Tests;

public class ContentFingerprintTests
{
    [Fact]
    public void IsStableAcrossRuns()
    {
        var first = ContentFingerprint.Compute(Fixtures.DefRoot);
        var second = ContentFingerprint.Compute(Fixtures.DefRoot);

        Assert.Equal(first, second);
        Assert.StartsWith("sha256:", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangesWhenAFileChangesSize()
    {
        using var temp = new TempContent();
        temp.Write("a.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": {} }""");
        var before = ContentFingerprint.Compute(temp.Path);

        temp.Write("a.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": { "More": 1 } }""");

        Assert.NotEqual(before, ContentFingerprint.Compute(temp.Path));
    }

    [Fact]
    public void ChangesWhenAFileIsAdded()
    {
        using var temp = new TempContent();
        temp.Write("a.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": {} }""");
        var before = ContentFingerprint.Compute(temp.Path);

        temp.Write("b.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": {} }""");

        Assert.NotEqual(before, ContentFingerprint.Compute(temp.Path));
    }

    [Fact]
    public void ChangesWhenAFileIsRemoved()
    {
        using var temp = new TempContent();
        temp.Write("a.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": {} }""");
        temp.Write("b.def", """{ "$Type": "A:B.CObjectBuilder", "$Value": {} }""");
        var before = ContentFingerprint.Compute(temp.Path);

        File.Delete(Path.Combine(temp.Path, "b.def"));

        Assert.NotEqual(before, ContentFingerprint.Compute(temp.Path));
    }

    [Fact]
    public void MissingDirectoryThrows() =>
        Assert.Throws<DirectoryNotFoundException>(
            () => ContentFingerprint.Compute(Path.Combine(Path.GetTempPath(), "tc-nope-" + Guid.NewGuid())));

    private sealed class TempContent : IDisposable
    {
        public TempContent()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tc-fp-" + Guid.NewGuid());
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Write(string name, string content) =>
            File.WriteAllText(System.IO.Path.Combine(Path, name), content);

        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); }
            catch (IOException) { /* best effort */ }
        }
    }
}

public class SchemaDumpTests
{
    [Fact]
    public void DescribesEveryType()
    {
        var schemas = SchemaDump.Describe(Fixtures.Scan());

        Assert.Contains(schemas, s => s.TypeName == Fixtures.ThrusterType);
        Assert.Contains(schemas, s => s.TypeName == "CompletelyUnknownObjectBuilder");
    }

    [Fact]
    public void OrdersByDescendingFrequency()
    {
        // The property, not a particular type: the most common types come first so a dump is
        // readable top-down. Asserting which type happens to win would break whenever a fixture
        // is added.
        var counts = SchemaDump.Describe(Fixtures.Scan()).Select(s => s.Count).ToList();

        Assert.Equal(counts.OrderByDescending(c => c), counts);
    }

    [Fact]
    public void MarksAlwaysPresentFields()
    {
        var thruster = SchemaDump.Describe(Fixtures.Scan())
            .Single(s => s.TypeName == Fixtures.ThrusterType);

        var thrustPower = thruster.Fields.Single(f => f.Name == "ThrustPower");

        Assert.True(thrustPower.AlwaysPresent);
        Assert.Equal(3, thrustPower.Occurrences);
    }

    [Fact]
    public void MarksOptionalFields()
    {
        // The whole reason this command exists: knowing which fields a parser must tolerate
        // being absent.
        var thruster = SchemaDump.Describe(Fixtures.Scan())
            .Single(s => s.TypeName == Fixtures.ThrusterType);

        var thrustClass = thruster.Fields.Single(f => f.Name == "ThrustClass");

        Assert.False(thrustClass.AlwaysPresent);
        Assert.Equal(2, thrustClass.Occurrences);
    }

    [Fact]
    public void RecordsObservedJsonKinds()
    {
        var thruster = SchemaDump.Describe(Fixtures.Scan())
            .Single(s => s.TypeName == Fixtures.ThrusterType);

        Assert.Equal(["Number"], thruster.Fields.Single(f => f.Name == "ThrustPower").Kinds);
        Assert.Equal(["String"], thruster.Fields.Single(f => f.Name == "Guid").Kinds);
    }

    [Fact]
    public void CarriesAnExampleFileAndValue()
    {
        var thruster = SchemaDump.Describe(Fixtures.Scan())
            .Single(s => s.TypeName == Fixtures.ThrusterType);

        Assert.EndsWith(".def", thruster.ExampleFile, StringComparison.Ordinal);
        Assert.NotNull(thruster.Fields.Single(f => f.Name == "ThrustPower").Example);
    }
}

public class Se2InstallationLocatorTests
{
    [Fact]
    public void ValidateRejectsADirectoryWithoutContent()
    {
        Assert.Null(Se2InstallationLocator.Validate(Path.GetTempPath(), "test"));
    }

    [Fact]
    public void ValidateAcceptsADirectoryWithContent()
    {
        var root = Path.Combine(Path.GetTempPath(), "tc-install-" + Guid.NewGuid());
        Directory.CreateDirectory(Path.Combine(root, "GameData", "Vanilla", "Content"));

        try
        {
            var install = Se2InstallationLocator.Validate(root, "test");

            Assert.NotNull(install);
            Assert.Equal("test", install!.DiscoveredVia);
            Assert.EndsWith("Content", install.ContentPath, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LocateHonoursAnExplicitOverride()
    {
        // A bad override must fail rather than silently falling back to a search, or a user
        // pointing at the wrong folder would get numbers from an install they did not choose.
        Assert.Null(Se2InstallationLocator.Locate(Path.GetTempPath()));
    }

    [Fact]
    public void CandidateLibrariesAreDistinctAndAbsolute()
    {
        var candidates = Se2InstallationLocator.CandidateLibraries();

        Assert.All(candidates, c => Assert.True(Path.IsPathRooted(c)));
        Assert.Equal(candidates.Count, candidates.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void CandidateSearchIsFast()
    {
        // Regression guard: probing IsReady before DriveType made this block for minutes on a
        // machine with an empty removable drive.
        var started = System.Diagnostics.Stopwatch.StartNew();

        Se2InstallationLocator.CandidateLibraries();

        Assert.True(started.Elapsed < TimeSpan.FromSeconds(10),
            $"install discovery took {started.Elapsed.TotalSeconds:F1}s");
    }
}
