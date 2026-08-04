using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// Derives stable ids, sizes and display names for blocks.
/// </summary>
/// <remarks>
/// Display names have to be synthesised because the game's <c>UIData.Name</c> is a <em>family</em>
/// key, not a per-block name: all four atmospheric thrusters report <c>ThrusterAtmo</c> and all four
/// ions report <c>ThrusterIon</c> (Research.md §3.3). Localised strings live in <c>.loc-texts</c>
/// files, but the app is English-only by decision (Schema.md §8), so the producer builds readable
/// names here instead.
/// </remarks>
public static partial class BlockNaming
{
    /// <summary>Trailing size in centimetres, e.g. <c>AtmosphericThruster250</c> → 250.</summary>
    [GeneratedRegex(@"^(?<stem>.*?)(?<size>\d+)$")]
    private static partial Regex SizeSuffixPattern { get; }

    /// <summary>Splits a PascalCase run into words, keeping digit groups intact.</summary>
    [GeneratedRegex(@"(?<!^)(?=[A-Z])")]
    private static partial Regex PascalCaseBoundary { get; }

    /// <summary>
    /// The block name a definition belongs to, taken from its filename.
    /// </summary>
    /// <remarks>
    /// A block's files are all prefixed with the block name and suffixed with the component they
    /// hold — <c>AtmosphericThruster250_ThrusterDefinition.def</c>,
    /// <c>HydrogenThruster250_HydrogenThrusterDefinition.def</c> — so the first underscore-delimited
    /// segment is the block. Used only for naming and for the cell-count lookup; the definitions
    /// themselves are joined through the composite graph, never by filename.
    /// </remarks>
    public static string BlockNameOf(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var fileName = Path.GetFileNameWithoutExtension(relativePath);
        var underscore = fileName.IndexOf('_', StringComparison.Ordinal);

        return underscore > 0 ? fileName[..underscore] : fileName;
    }

    /// <summary>A stable camelCase id, e.g. <c>atmosphericThruster250</c>.</summary>
    public static string IdOf(string blockName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);

        return char.ToLowerInvariant(blockName[0]) + blockName[1..];
    }

    /// <summary>Trailing size in centimetres, or <c>null</c> when the name carries none.</summary>
    public static int? SizeCmOf(string blockName)
    {
        var match = SizeSuffixPattern.Match(blockName ?? string.Empty);

        return match.Success
               && int.TryParse(match.Groups["size"].Value, NumberStyles.None,
                   CultureInfo.InvariantCulture, out var size)
            ? size
            : null;
    }

    /// <summary>
    /// A readable name, e.g. <c>AtmosphericThruster250</c> → <c>"Atmospheric Thruster 2.5 m"</c>.
    /// </summary>
    public static string DisplayNameOf(string blockName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blockName);

        var match = SizeSuffixPattern.Match(blockName);
        var stem = match.Success ? match.Groups["stem"].Value : blockName;
        var size = SizeCmOf(blockName);

        var words = PascalCaseBoundary.Split(stem).Where(w => w.Length > 0);
        var text = new StringBuilder(string.Join(' ', words));

        if (size is { } cm)
        {
            // Metres read better than centimetres at these sizes: "2.5 m", not "250 cm".
            var metres = cm / 100.0;
            text.Append(' ')
                .Append(metres.ToString(metres % 1 == 0 ? "0" : "0.##", CultureInfo.InvariantCulture))
                .Append(" m");
        }

        return text.ToString();
    }
}
