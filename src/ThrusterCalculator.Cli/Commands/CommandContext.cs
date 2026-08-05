using ThrusterCalculator.Engine;
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

    /// <summary>
    /// Prefers the game's own precomputed occupancy and inheritance, falling back to the table.
    /// </summary>
    /// <remarks>
    /// Hosting the game's assemblies is the fragile part of this tool, so a failure here degrades
    /// rather than aborting: the table covers fewer blocks and inheritance goes unresolved, but the
    /// run still produces a usable config (Design.md P5). <c>--no-engine</c> forces that path, which
    /// is also how the two occupancy sources get compared.
    /// <para>
    /// Shared by <c>extract</c> and <c>verify</c> deliberately — verify must exercise the same
    /// resolution the real run does, or it is checking something other than what ships.
    /// </para>
    /// </remarks>
    public static (IOccupancySource Occupancy, IDefinitionInheritance Inheritance)
        OpenEngineSources(Se2Installation installation, bool noEngine)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var table = new TableOccupancySource();

        if (noEngine)
        {
            Console.Error.WriteLine("Engine disabled (--no-engine).");
            return (table, new NoDefinitionInheritance());
        }

        try
        {
            var occupancy = ContentCacheOccupancySource.Open(
                installation.RootPath, installation.ContentPath, table);
            Console.Error.WriteLine($"Content cache:  {occupancy.Coverage:N0} asset entries.");

            // Both live behind the same runtime, so one attach serves both.
            var inheritance = DefinitionSetInheritance.Open(
                Se2Runtime.Attach(installation.RootPath), installation.ContentPath);
            Console.Error.WriteLine(
                $"Definition sets: {inheritance.Count:N0} of {inheritance.TotalDefinitions:N0} "
                + "definitions declare a base.");

            return (occupancy, inheritance);
        }
        catch (Exception ex) when (ex is Se2EngineException or BadImageFormatException
                                      or FileLoadException or TypeLoadException)
        {
            Console.Error.WriteLine($"Could not host the game's assemblies ({ex.Message}).");
            Console.Error.WriteLine("Falling back to the recovered table; inheritance unresolved.");
            return (table, new NoDefinitionInheritance());
        }
    }
}
