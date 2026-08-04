using System.Text.RegularExpressions;

namespace ThrusterCalculator.Extraction;

/// <summary>A located Space Engineers 2 installation.</summary>
public sealed record Se2Installation
{
    /// <summary>Install root, e.g. <c>…\steamapps\common\SpaceEngineers2</c>.</summary>
    public required string RootPath { get; init; }

    /// <summary>The definition tree, <c>&lt;root&gt;\GameData\Vanilla\Content</c>.</summary>
    public required string ContentPath { get; init; }

    /// <summary>How it was found — useful when a user is debugging a wrong or missing install.</summary>
    public required string DiscoveredVia { get; init; }
}

/// <summary>
/// Finds a local Space Engineers 2 installation.
/// </summary>
/// <remarks>
/// Never hardcodes a path. Steam's app id for SE2 is <c>1133870</c>, and libraries routinely live off
/// the system drive — on the machine this was developed against the game sits on <c>G:</c> while
/// Steam itself is on <c>C:</c>, so probing only the default location would fail outright.
/// <para>
/// Deliberately uses no Windows-only API (no registry), which keeps this project on a
/// platform-neutral target framework. The trade-off is probing a handful of well-known locations
/// instead of asking Steam directly; a manual override always wins.
/// </para>
/// </remarks>
public static partial class Se2InstallationLocator
{
    /// <summary>Steam's app id for Space Engineers 2.</summary>
    public const string SteamAppId = "1133870";

    private const string GameFolderName = "SpaceEngineers2";

    private static readonly string ContentSuffix =
        Path.Combine("GameData", "Vanilla", "Content");

    [GeneratedRegex("""^\s*"path"\s*"(?<path>[^"]+)"\s*$""", RegexOptions.Multiline)]
    private static partial Regex LibraryPathPattern { get; }

    /// <summary>
    /// Locates an installation, or returns <c>null</c> if none is found.
    /// </summary>
    /// <param name="overridePath">
    /// An explicit install root, which is validated and used in preference to any search.
    /// </param>
    public static Se2Installation? Locate(string? overridePath = null)
    {
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return Validate(overridePath, "explicit override");
        }

        foreach (var library in CandidateLibraries())
        {
            var root = Path.Combine(library, "steamapps", "common", GameFolderName);
            if (Validate(root, $"Steam library at '{library}'") is { } install)
            {
                return install;
            }
        }

        return null;
    }

    /// <summary>Validates that a directory really is an SE2 install.</summary>
    public static Se2Installation? Validate(string rootPath, string discoveredVia)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        var content = Path.Combine(rootPath, ContentSuffix);

        return Directory.Exists(content)
            ? new Se2Installation
            {
                RootPath = Path.GetFullPath(rootPath),
                ContentPath = Path.GetFullPath(content),
                DiscoveredVia = discoveredVia,
            }
            : null;
    }

    /// <summary>
    /// Every Steam library worth probing: those declared in each Steam root's
    /// <c>libraryfolders.vdf</c>, plus the roots themselves.
    /// </summary>
    /// <remarks>
    /// The VDF is scanned for <c>"path"</c> entries rather than fully parsed. A real parser would
    /// buy nothing here — we only need candidate directories, and every candidate is validated by
    /// checking for the content tree anyway.
    /// </remarks>
    public static IReadOnlyList<string> CandidateLibraries()
    {
        var candidates = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var full = Path.GetFullPath(path.Replace(@"\\", @"\", StringComparison.Ordinal));
            if (seen.Add(full)) candidates.Add(full);
        }

        foreach (var steamRoot in SteamRoots())
        {
            Add(steamRoot);

            var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;

            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (Match match in LibraryPathPattern.Matches(text))
            {
                Add(match.Groups["path"].Value);
            }
        }

        return candidates;
    }

    private static IEnumerable<string> SteamRoots()
    {
        foreach (var variable in new[] { "ProgramFiles(x86)", "ProgramFiles" })
        {
            var root = Environment.GetEnvironmentVariable(variable);
            if (!string.IsNullOrWhiteSpace(root))
            {
                yield return Path.Combine(root, "Steam");
            }
        }

        // Secondary drives commonly hold a bare \Steam or \SteamLibrary that no VDF points at,
        // for instance after a drive letter change.
        foreach (var drive in SafeDrives())
        {
            yield return Path.Combine(drive, "Steam");
            yield return Path.Combine(drive, "SteamLibrary");
        }
    }

    private static IEnumerable<string> SafeDrives()
    {
        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            yield break;
        }

        foreach (var drive in drives)
        {
            var usable = false;
            try
            {
                // DriveType first, deliberately. Querying IsReady on a removable or optical drive
                // makes Windows spin it up or wait for media, which can block for minutes — that
                // alone made install discovery appear to hang on a machine with an empty D: drive.
                // DriveType is metadata and never touches the device.
                usable = drive.DriveType == DriveType.Fixed && drive.IsReady;
            }
            catch (IOException)
            {
                // Unreadable drive; skip it rather than failing the whole search.
            }

            if (usable) yield return drive.RootDirectory.FullName;
        }
    }
}
