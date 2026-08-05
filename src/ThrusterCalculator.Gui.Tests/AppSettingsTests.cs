using ThrusterCalculator.Gui.Services;

namespace ThrusterCalculator.Gui.Tests;

/// <summary>
/// Settings round-tripping, against a temporary file.
/// </summary>
/// <remarks>
/// Never <see cref="AppSettings.FilePath"/>: these tests would then read and overwrite the real
/// user's settings on whatever machine runs them.
/// </remarks>
public sealed class AppSettingsTests : IDisposable
{
    private readonly string _path =
        Path.Combine(Path.GetTempPath(), $"tc-settings-{Guid.NewGuid():N}.ini");

    public void Dispose()
    {
        if (File.Exists(_path)) File.Delete(_path);
    }

    [Fact]
    public void CreatesTheFileOnFirstLoad()
    {
        // Self-creating: the user should find a complete, commented file to edit without having
        // to discover which keys exist.
        var settings = AppSettings.Load(_path);

        Assert.True(File.Exists(_path));
        Assert.Equal(AppSettings.DefaultCustomGravity, settings.CustomGravity);
        Assert.Equal(AppSettings.DefaultTargetThrustToWeight, settings.TargetThrustToWeight);
    }

    [Fact]
    public void RoundTripsThroughTheFile()
    {
        new AppSettings
        {
            SelectedPlanetId = "verdure",
            UseCustomGravity = true,
            CustomGravity = 8.5,
            TargetThrustToWeight = 1.5,
        }.Save(_path);

        var reloaded = AppSettings.Load(_path);

        Assert.Equal("verdure", reloaded.SelectedPlanetId);
        Assert.True(reloaded.UseCustomGravity);
        Assert.Equal(8.5, reloaded.CustomGravity);
        Assert.Equal(1.5, reloaded.TargetThrustToWeight);
    }

    [Fact]
    public void AMissingKeyFallsBackAndTheFileIsCompleted()
    {
        File.WriteAllText(_path, "CustomGravity = 3.7\n");

        var settings = AppSettings.Load(_path);

        Assert.Equal(3.7, settings.CustomGravity);
        Assert.Equal(AppSettings.DefaultTargetThrustToWeight, settings.TargetThrustToWeight);

        // Rewritten complete, so the file documents itself rather than growing silently.
        Assert.Contains("TargetThrustToWeight", File.ReadAllText(_path), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("CustomGravity = banana")]
    [InlineData("CustomGravity = 0")]
    [InlineData("CustomGravity = -9.81")]
    [InlineData("CustomGravity = 1e9")]
    public void AnImplausibleGravityIsRejectedRatherThanUsed(string line)
    {
        // A hand-edited zero or negative would make the requirement zero and every loadout
        // trivially feasible — a wrong answer that looks like a working app.
        File.WriteAllText(_path, line + "\n");

        Assert.Equal(AppSettings.DefaultCustomGravity, AppSettings.Load(_path).CustomGravity);
    }

    [Fact]
    public void CommentsAndJunkLinesAreIgnored()
    {
        File.WriteAllText(_path,
            "# a comment\n; another\n[section]\nnonsense-without-separator\nCustomGravity = 5\n");

        Assert.Equal(5, AppSettings.Load(_path).CustomGravity);
    }

    [Fact]
    public void AnUnreadablePathDoesNotThrow()
    {
        // Losing settings is not worth failing startup or shutdown over.
        var directory = Path.Combine(Path.GetTempPath(), $"tc-{Guid.NewGuid():N}");
        var unusable = Path.Combine(directory, "nested", "settings.ini");

        var settings = AppSettings.Load(unusable);
        settings.Save(unusable);

        Assert.Equal(AppSettings.DefaultCustomGravity, settings.CustomGravity);

        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
