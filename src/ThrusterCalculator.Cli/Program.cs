namespace ThrusterCalculator.Cli;

/// <summary>
/// Skeleton entry point. Commands are not implemented yet — see Technic.md §8 for sequencing.
/// </summary>
internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 1;
        }

        Console.Error.WriteLine($"tc: '{args[0]}' is not implemented yet.");
        Console.Error.WriteLine();
        PrintUsage();
        return 1;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            tc — ThrusterCalculator SE2 data producer

            USAGE
              tc <command> [options]

            COMMANDS (planned — none implemented yet)
              dump-schemas    Group every .def by $Type and emit the distinct field sets.
                              Also the patch-diffing tool for future game updates.
              extract         Produce gamedata.json from a local Space Engineers 2 install.
              verify          Check a local install against the invariants we rely on.

            NOTES
              Requires Space Engineers 2 installed. The GUI and web frontends do not —
              they consume gamedata.json only. See Schema.md for the contract.
            """);
    }
}
