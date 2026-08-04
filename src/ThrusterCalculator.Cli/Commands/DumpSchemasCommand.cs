using System.Globalization;
using System.Text;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>
/// Lists every <c>$Type</c> in an installation and the fields it carries.
/// </summary>
/// <remarks>
/// Output is deterministic so two runs can be diffed across a game update — that is the point of
/// keeping this as a real command rather than a throwaway script.
/// </remarks>
internal static class DumpSchemasCommand
{
    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var filter = CommandContext.Option(args, "--filter");
        var outPath = CommandContext.Option(args, "--out");

        var definitions = DefinitionScanner.Scan(installation, Progress());
        Console.Error.WriteLine();
        Console.Error.WriteLine(
            $"Read {definitions.All.Count:N0} of {definitions.FilesSeen:N0} .def files "
            + $"({definitions.Warnings.Count} warning(s)).");

        var schemas = SchemaDump.Describe(definitions);
        if (filter is not null)
        {
            schemas = schemas
                .Where(s => s.TypeName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        var report = Render(definitions, schemas, filter);

        if (outPath is not null)
        {
            File.WriteAllText(outPath, report);
            Console.Error.WriteLine($"Wrote {schemas.Count:N0} type(s) to {outPath}");
        }
        else
        {
            Console.Out.Write(report);
        }

        return 0;
    }

    private static IProgress<ScanProgress> Progress() =>
        new Progress<ScanProgress>(p =>
            Console.Error.Write($"\rScanning… {p.FilesRead:N0}/{p.TotalFiles:N0}"));

    private static string Render(
        DefinitionSet definitions, IReadOnlyList<TypeSchema> schemas, string? filter)
    {
        var text = new StringBuilder();

        text.AppendLine(CultureInfo.InvariantCulture,
            $"# Definition schemas — {definitions.All.Count:N0} definitions, {schemas.Count:N0} types");
        text.AppendLine(CultureInfo.InvariantCulture,
            $"# Game build (max bundle stamp): {definitions.MaxBundleVersion() ?? "unknown"}");
        if (filter is not null)
        {
            text.AppendLine(CultureInfo.InvariantCulture, $"# Filtered by: {filter}");
        }

        text.AppendLine();

        foreach (var schema in schemas)
        {
            text.AppendLine(CultureInfo.InvariantCulture,
                $"{schema.TypeName}  ({schema.Count:N0})");
            text.AppendLine(CultureInfo.InvariantCulture, $"    type: {schema.FullType}");
            text.AppendLine(CultureInfo.InvariantCulture, $"    e.g.: {schema.ExampleFile}");

            foreach (var field in schema.Fields)
            {
                // '?' marks a field that is absent on at least one definition of this type —
                // exactly the case a parser must tolerate.
                var optional = field.AlwaysPresent ? " " : "?";
                var kinds = string.Join("|", field.Kinds);
                var example = field.Example is null ? string.Empty : $"  = {field.Example}";

                text.AppendLine(CultureInfo.InvariantCulture,
                    $"      {optional} {field.Name,-34} {kinds,-20} {field.Occurrences,6:N0}{example}");
            }

            text.AppendLine();
        }

        if (definitions.Warnings.Count > 0)
        {
            text.AppendLine("# Warnings");
            foreach (var warning in definitions.Warnings)
            {
                text.AppendLine(CultureInfo.InvariantCulture,
                    $"  [{warning.Code}] {warning.File}: {warning.Detail}");
            }
        }

        return text.ToString();
    }
}
