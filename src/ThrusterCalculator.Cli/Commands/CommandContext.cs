using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>Minimal argument handling. A parser library would outweigh the surface it parses.</summary>
internal static class CommandContext
{
    public static string? Option(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.Ordinal))
            {
                return args[i + 1];
            }
        }

        return null;
    }

    public static bool Flag(string[] args, string name) =>
        Array.Exists(args, a => string.Equals(a, name, StringComparison.Ordinal));

    /// <summary>
    /// Locates the installation, reporting clearly when it cannot be found.
    /// </summary>
    public static Se2Installation? ResolveInstallation(string[] args)
    {
        var overridePath = Option(args, "--install");
        var installation = Se2InstallationLocator.Locate(overridePath);

        if (installation is not null)
        {
            // Diagnostics go to stderr so stdout stays clean for redirection and diffing.
            Console.Error.WriteLine($"Using install: {installation.RootPath}");
            Console.Error.WriteLine($"  found via:   {installation.DiscoveredVia}");
            return installation;
        }

        if (overridePath is not null)
        {
            Console.Error.WriteLine(
                $"tc: '{overridePath}' does not look like a Space Engineers 2 install "
                + "(no GameData/Vanilla/Content inside).");
        }
        else
        {
            Console.Error.WriteLine("tc: could not find a Space Engineers 2 installation.");
            Console.Error.WriteLine("    Searched these Steam libraries:");
            foreach (var library in Se2InstallationLocator.CandidateLibraries())
            {
                Console.Error.WriteLine($"      {library}");
            }

            Console.Error.WriteLine("    Pass --install PATH to point at it directly.");
        }

        return null;
    }
}
