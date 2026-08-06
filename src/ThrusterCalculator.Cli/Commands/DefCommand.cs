using ThrusterCalculator.Engine;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>
/// Prints a definition's base chain, and where along it a named field actually comes from.
/// </summary>
/// <remarks>
/// A <c>.def</c> file that omits a field is ambiguous on its face: the value may be inherited, or
/// the field may simply take its object-builder default. The two readings give opposite answers and
/// nothing in the file distinguishes them, because <c>BaseGuid</c> is not in the <c>.def</c> files
/// at all — it lives in <c>definitionsets.vrb</c> (<see cref="DefinitionSetInheritance"/>).
/// <para>
/// This resolves the question rather than arguing it. Point it at a GUID and it walks the real
/// inheritance graph, naming the first ancestor that states the field — or reporting that none
/// does, which is what licenses reading the default.
/// </para>
/// </remarks>
internal static class DefCommand
{
    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToList();
        if (positional.Count == 0)
        {
            Console.Error.WriteLine("tc: usage: tc def <guid> [field ...]");
            return 1;
        }

        var guid = positional[0];
        var fields = positional.Skip(1).ToList();

        var definitions = DefinitionScanner.Scan(installation);

        IDefinitionInheritance inheritance;
        try
        {
            var runtime = Se2Runtime.Attach(installation.RootPath);
            inheritance = DefinitionSetInheritance.Open(runtime, installation.ContentPath);
        }
        catch (Exception ex) when (ex is Se2EngineException or BadImageFormatException
                                      or FileLoadException or TypeLoadException)
        {
            Console.Error.WriteLine($"tc: could not host the game's assemblies ({ex.Message}).");
            return 1;
        }

        // The chain first: without it, "the field is absent" says nothing.
        var chain = new List<(string Guid, DefinitionFile? File)>();
        var current = guid;

        for (var depth = 0; depth < 32 && current is not null; depth++)
        {
            var file = definitions.Resolve(current);
            chain.Add((current, file));
            current = inheritance.BaseOf(current);
        }

        Console.WriteLine("base chain:");
        foreach (var (g, file) in chain)
        {
            Console.WriteLine($"  {g}  {file?.RelativePath ?? "<not found in content>"}");
        }

        if (fields.Count == 0) return 0;

        Console.WriteLine();
        foreach (var field in fields)
        {
            var found = false;

            foreach (var (g, file) in chain)
            {
                if (file is null) continue;

                // Any JSON kind counts as "stated": a field explicitly set to null or false is a
                // deliberate value, and treating it as absent would resume inheriting past it.
                if (file.Value.ValueKind != System.Text.Json.JsonValueKind.Object
                    || !file.Value.TryGetProperty(field, out var property))
                {
                    continue;
                }

                Console.WriteLine($"{field} = {property} (from {file.RelativePath})");
                found = true;
                break;
            }

            if (!found)
            {
                Console.WriteLine($"{field} : stated nowhere in the chain — the type's default applies");
            }
        }

        return 0;
    }
}
