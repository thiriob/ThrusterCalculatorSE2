namespace ThrusterCalculator.Model.Tests;

internal static class Fixture
{
    public static string Path { get; } =
        System.IO.Path.Combine(AppContext.BaseDirectory, "fixtures", "synthetic-gamedata.json");

    public static string Json => File.ReadAllText(Path);

    public static GameData Load() => GameDataSerializer.Read(Json);
}
