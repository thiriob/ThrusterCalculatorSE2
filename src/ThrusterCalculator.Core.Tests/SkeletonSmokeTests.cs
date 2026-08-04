namespace ThrusterCalculator.Core.Tests;

/// <summary>
/// Placeholder proving the test harness runs and the synthetic fixture is where the build
/// expects it. Delete once real tests exist.
/// </summary>
public class SkeletonSmokeTests
{
    [Fact]
    public void SyntheticFixtureIsCopiedToOutput()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-gamedata.json");

        Assert.True(File.Exists(path),
            $"Expected the synthetic fixture at '{path}'. Check the Content item in the csproj.");
    }
}
