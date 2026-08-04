using ThrusterCalculator.Model;

namespace ThrusterCalculator.Extraction;

/// <summary>Progress while walking the content tree.</summary>
public readonly record struct ScanProgress(int FilesRead, int TotalFiles)
{
    public double Fraction => TotalFiles > 0 ? (double)FilesRead / TotalFiles : 0;
}

/// <summary>
/// Walks an installation's content tree and reads every <c>.def</c> file.
/// </summary>
public static class DefinitionScanner
{
    /// <summary>How often to report progress. Per-file reporting would cost more than the parsing.</summary>
    private const int ProgressInterval = 250;

    /// <summary>
    /// Reads every definition under <paramref name="installation"/>'s content root.
    /// </summary>
    /// <remarks>
    /// A file that cannot be read becomes a warning and the scan continues — one malformed document
    /// out of 17k must never abort extraction (Technic.md §7.2).
    /// </remarks>
    public static DefinitionSet Scan(
        Se2Installation installation,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installation);

        return Scan(installation.ContentPath, progress, cancellationToken);
    }

    /// <inheritdoc cref="Scan(Se2Installation, IProgress{ScanProgress}, CancellationToken)"/>
    public static DefinitionSet Scan(
        string contentPath,
        IProgress<ScanProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        if (!Directory.Exists(contentPath))
        {
            throw new DirectoryNotFoundException($"Content directory not found: '{contentPath}'");
        }

        var files = Directory.GetFiles(contentPath, "*.def", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal); // deterministic order, so output is diffable

        var definitions = new List<DefinitionFile>(files.Length);
        var warnings = new List<ExtractionWarning>();

        for (var i = 0; i < files.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var file = files[i];
            var relative = Relative(contentPath, file);

            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add(new ExtractionWarning
                {
                    Code = "unreadableFile",
                    Detail = ex.Message,
                    File = relative,
                });
                continue;
            }

            var definition = DefinitionReader.TryRead(relative, json, out var failure);
            if (definition is null)
            {
                warnings.Add(new ExtractionWarning
                {
                    Code = "unparsableDefinition",
                    Detail = failure ?? "unknown parse failure",
                    File = relative,
                });
                continue;
            }

            definitions.Add(definition);

            if (progress is not null && (i % ProgressInterval == 0 || i == files.Length - 1))
            {
                progress.Report(new ScanProgress(i + 1, files.Length));
            }
        }

        return new DefinitionSet(definitions, warnings, files.Length);
    }

    private static string Relative(string root, string file) =>
        Path.GetRelativePath(root, file).Replace('\\', '/');
}
