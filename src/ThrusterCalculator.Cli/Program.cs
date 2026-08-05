using ThrusterCalculator.Cli.Commands;

namespace ThrusterCalculator.Cli;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        var command = args[0];
        var rest = args[1..];

        try
        {
            return command switch
            {
                "dump-schemas" => DumpSchemasCommand.Run(rest),
                "extract" => ExtractCommand.Run(rest),
                "verify" => VerifyCommand.Run(rest),
                "planet" => PlanetCommand.Run(rest),
                "-h" or "--help" or "help" => Help(),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"tc: {ex.Message}");
            return 1;
        }
    }

    private static int Help()
    {
        PrintUsage();
        return 0;
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"tc: unknown command '{command}'.");
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    private static void PrintUsage() => Console.WriteLine("""
        tc — ThrusterCalculator SE2 data producer

        USAGE
          tc <command> [options]

        COMMANDS
          dump-schemas    Group every .def by $Type and list the fields each carries.
                          Diff two runs across a game update to see what Keen changed.
          extract         Produce gamedata.json from a local Space Engineers 2 install.
          verify          Check a local install against the invariants we rely on.
          planet [NAME]   Walk a planet's inheritance chain, showing where each
                          gravity and atmosphere value comes from — or stops.

        COMMON OPTIONS
          --install PATH  Use this Space Engineers 2 install instead of searching for one.
          --out PATH      Where to write output (extract, dump-schemas).

        NOTES
          Requires Space Engineers 2 installed. The GUI and web frontends do not —
          they consume gamedata.json only. See Schema.md for the contract.
        """);
}
