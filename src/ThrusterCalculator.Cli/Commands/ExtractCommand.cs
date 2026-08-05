using ThrusterCalculator.Engine;
using ThrusterCalculator.Extraction;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>Produces <c>gamedata.json</c> — the artifact every consumer reads.</summary>
internal static class ExtractCommand
{
    private const string DefaultOutput = "gamedata.json";

    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var outPath = CommandContext.Option(args, "--out") ?? DefaultOutput;

        var definitions = DefinitionScanner.Scan(installation, Progress());
        Console.Error.WriteLine();

        var (occupancy, inheritance) = CommandContext.OpenEngineSources(
            installation, CommandContext.Flag(args, "--no-engine"));

        var fingerprint = ContentFingerprint.Compute(installation.ContentPath);
        var extractor = new GameDataExtractor(definitions, occupancy, inheritance);
        var data = extractor.Extract(ToolVersion, fingerprint);
        Console.Error.WriteLine($"Occupancy source:   {extractor.OccupancySourceName}");
        Console.Error.WriteLine($"Inheritance source: {extractor.InheritanceSourceName}");

        using (var stream = File.Create(outPath))
        {
            GameDataSerializer.Write(stream, data);
        }

        Report(data, outPath);
        return 0;
    }

    private static string ToolVersion =>
        typeof(ExtractCommand).Assembly.GetName().Version?.ToString() ?? "0.0.0";

    private static IProgress<ScanProgress> Progress() =>
        new Progress<ScanProgress>(p =>
            Console.Error.Write($"\rScanning… {p.FilesRead:N0}/{p.TotalFiles:N0}"));

    private static void Report(GameData data, string outPath)
    {
        Console.WriteLine($"Wrote {outPath}");
        Console.WriteLine($"  game build   {data.Source.GameBuild}");
        Console.WriteLine($"  thrusters    {data.Thrusters.Count}");
        Console.WriteLine($"  thrustClasses{data.ThrustClasses.Count,4}");
        Console.WriteLine($"  densities    {data.Densities.Count}");
        Console.WriteLine($"  resources    {data.Resources.Count}");
        Console.WriteLine($"  containers   {data.Containers.Count}");
        Console.WriteLine($"  tanks        {data.Tanks.Count}");
        Console.WriteLine($"  planets      {data.Planets.Count}");

        if (data.Warnings.Count == 0)
        {
            Console.WriteLine("  warnings     none");
            return;
        }

        // Warnings are grouped rather than listed: a systemic problem shows up as a large count on
        // one code, which is the signal worth seeing.
        Console.WriteLine($"  warnings     {data.Warnings.Count}");
        foreach (var group in data.Warnings.GroupBy(w => w.Code).OrderByDescending(g => g.Count()))
        {
            Console.WriteLine($"    {group.Count(),4}  {group.Key}");
            foreach (var warning in group.Take(3))
            {
                Console.WriteLine($"            {warning.Detail}");
            }

            if (group.Count() > 3)
            {
                Console.WriteLine($"            … and {group.Count() - 3} more");
            }
        }
    }
}
