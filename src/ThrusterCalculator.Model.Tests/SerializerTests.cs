namespace ThrusterCalculator.Model.Tests;

public class SerializerTests
{
    [Fact]
    public void RoundTripsWithoutLoss()
    {
        // Note: record equality will NOT do here. The collection members are IReadOnlyList /
        // IReadOnlyDictionary, which compare by reference, so Assert.Equal on two GameData
        // instances passes or fails for reasons unrelated to content. Comparing the re-serialized
        // text is both correct and a stronger statement: write -> read -> write is a fixed point.
        var once = GameDataSerializer.WriteToString(Fixture.Load());
        var twice = GameDataSerializer.WriteToString(GameDataSerializer.Read(once));

        Assert.Equal(once, twice);
    }

    [Fact]
    public void RoundTripPreservesNestedValues()
    {
        var reloaded = GameDataSerializer.Read(GameDataSerializer.WriteToString(Fixture.Load()));

        var thruster = reloaded.Thrusters.Single(t => t.Id == "testThrusterBaseline");
        Assert.Equal(100_000.0, thruster.ThrustNewtons);
        Assert.Equal(100, thruster.OccupiedCells);
        Assert.Equal("testPower", thruster.ConsumedResource!.Resource);
        Assert.Equal(Provenance.Derived, thruster.ProvenanceOf("occupiedCells"));

        var planet = reloaded.Planets.Single(p => p.Id == "testPlanetNoGravity");
        Assert.Null(planet.SurfaceGravity);
        Assert.Equal(Provenance.Unknown, planet.ProvenanceOf("surfaceGravity"));

        Assert.Equal(2, reloaded.Warnings.Count);
        Assert.Equal(5, reloaded.Source.DefinitionCounts["ThrusterDefinitionObjectBuilder"]);
    }

    [Fact]
    public void WritesCamelCase()
    {
        var json = GameDataSerializer.WriteToString(Fixture.Load());

        Assert.Contains("\"schemaVersion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"occupiedCells\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"SchemaVersion\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesProvenanceAsCamelCaseStrings()
    {
        var json = GameDataSerializer.WriteToString(Fixture.Load());

        Assert.Contains("\"derived\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Derived\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void WritesExplicitNulls()
    {
        // An explicit null paired with 'unknown' provenance is meaningful and must survive a
        // round-trip — it is not the same as an absent field.
        var json = GameDataSerializer.WriteToString(Fixture.Load());

        Assert.Contains("\"surfaceGravity\": null", json, StringComparison.Ordinal);
    }

    [Fact]
    public void ToleratesCommentsAndTrailingCommas()
    {
        // Users hand-edit this file (Schema.md R5).
        const string json = """
            {
              // a comment
              "schemaVersion": "1.0",
              "generator": { "tool": "tc", "version": "0", "extractedAt": "2000-01-01T00:00:00Z" },
              "source": { "gameBuild": "x", "fingerprint": "y" },
              "models": {
                "blockMass": { "kind": "sqrtLog10CellCount", "minBlockMass": 5 },
                "thrustEffectiveness": { "kind": "linearRampAirDensity" },
                "atmosphereDensity": { "kind": "linearRampAltitude" },
              },
            }
            """;

        var data = GameDataSerializer.Read(json);

        Assert.Equal("1.0", data.SchemaVersion);
        Assert.Empty(data.Thrusters); // collections default to empty, never null
    }

    [Fact]
    public void CollectionsDefaultToEmptyNotNull()
    {
        var data = GameDataSerializer.Read(MinimalJson);

        Assert.NotNull(data.Thrusters);
        Assert.NotNull(data.Planets);
        Assert.NotNull(data.Warnings);
        Assert.NotNull(data.Source.DefinitionCounts);
    }

    [Theory]
    [InlineData("2.0")]
    [InlineData("0.9")]
    public void RefusesIncompatibleMajorVersion(string version)
    {
        var json = MinimalJson.Replace("\"1.0\"", $"\"{version}\"", StringComparison.Ordinal);

        var ex = Assert.Throws<GameDataFormatException>(() => GameDataSerializer.Read(json));

        Assert.Contains(version, ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SchemaOnePointZeroConfigsStillLoadWithTheDefaultsTheyWereWrittenUnder()
    {
        // The fixture moved on, so this is what keeps the older shape covered. A config that
        // predates a field must not fail, and must not silently acquire a different meaning: the
        // atmosphere defaults to full density and the gravity model to the only kind implemented.
        var data = GameDataSerializer.Read(MinimalJson);

        Assert.Equal("1.0", data.SchemaVersion);
        Assert.Equal("powerOrLinearRamp", data.Models.GravityFalloff.Kind);

        var atmosphere = new Atmosphere { AffectDistance = 1.15, ConstantAffectDistance = 1.08 };
        Assert.Equal(1.0, atmosphere.Density);

        // No speed limit rather than an invented one: a consumer must decline to claim a ship
        // coasts anywhere, not assume it can accelerate forever.
        Assert.Null(data.Limits);
    }

    [Fact]
    public void AcceptsHigherMinorVersion()
    {
        // Additive changes are safe: unknown fields are ignored, so an older reader still gets
        // everything it understands.
        var json = MinimalJson.Replace("\"1.0\"", "\"1.7\"", StringComparison.Ordinal);

        var data = GameDataSerializer.Read(json);

        Assert.Equal("1.7", data.SchemaVersion);
    }

    [Theory]
    [InlineData("not-a-version")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    [InlineData("")]
    public void RejectsMalformedVersion(string version)
    {
        var json = MinimalJson.Replace("\"1.0\"", $"\"{version}\"", StringComparison.Ordinal);

        Assert.Throws<GameDataFormatException>(() => GameDataSerializer.Read(json));
    }

    [Fact]
    public void RejectsNullDocument()
    {
        var ex = Assert.Throws<GameDataFormatException>(() => GameDataSerializer.Read("null"));

        Assert.Contains("empty", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void WrapsMalformedJsonInAReadableError()
    {
        var ex = Assert.Throws<GameDataFormatException>(() => GameDataSerializer.Read("{ oops"));

        Assert.Contains("could not be parsed", ex.Message, StringComparison.Ordinal);
    }

    private const string MinimalJson = """
        {
          "schemaVersion": "1.0",
          "generator": { "tool": "tc", "version": "0", "extractedAt": "2000-01-01T00:00:00Z" },
          "source": { "gameBuild": "x", "fingerprint": "y" },
          "models": {
            "blockMass": { "kind": "sqrtLog10CellCount", "minBlockMass": 5 },
            "thrustEffectiveness": { "kind": "linearRampAirDensity" },
            "atmosphereDensity": { "kind": "linearRampAltitude" }
          }
        }
        """;
}

public class SchemaVersionTests
{
    [Theory]
    [InlineData("1.0", 1, 0)]
    [InlineData("2.15", 2, 15)]
    [InlineData("0.1", 0, 1)]
    public void Parses(string text, int major, int minor)
    {
        Assert.True(SchemaVersion.TryParse(text, out var v));
        Assert.Equal(new SchemaVersion(major, minor), v);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("1")]
    [InlineData("1.2.3")]
    [InlineData("a.b")]
    [InlineData("-1.0")]
    public void RejectsMalformed(string? text) => Assert.False(SchemaVersion.TryParse(text, out _));

    [Fact]
    public void FormatsRoundTrip()
    {
        Assert.Equal("1.4", SchemaVersion.Current.ToString());
        Assert.True(SchemaVersion.TryParse(SchemaVersion.Current.ToString(), out var v));
        Assert.Equal(SchemaVersion.Current, v);
    }

    [Fact]
    public void Orders()
    {
        Assert.True(new SchemaVersion(1, 2).CompareTo(new SchemaVersion(1, 10)) < 0);
        Assert.True(new SchemaVersion(2, 0).CompareTo(new SchemaVersion(1, 99)) > 0);
    }
}
