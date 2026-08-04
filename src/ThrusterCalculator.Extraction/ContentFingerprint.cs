using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// A cheap hash of the game's definition tree, used to detect that the game has been patched.
/// </summary>
/// <remarks>
/// Hashes file <em>metadata</em> — relative path, size, last-write time — rather than contents.
/// Over 17k files that difference is what makes the check a directory enumeration instead of 17k
/// reads, and cheap enough to run on every launch. That in turn is what lets the staleness banner
/// be honest rather than decorative (Technic.md §3.3).
/// <para>
/// It will therefore miss an edit that preserves both size and timestamp. That is an acceptable
/// trade: the case it exists for is "Steam updated the game", which never preserves either.
/// </para>
/// </remarks>
public static class ContentFingerprint
{
    /// <summary>Computes the fingerprint, formatted as <c>sha256:…</c>.</summary>
    public static string Compute(string contentPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentPath);

        if (!Directory.Exists(contentPath))
        {
            throw new DirectoryNotFoundException($"Content directory not found: '{contentPath}'");
        }

        var files = Directory.GetFiles(contentPath, "*.def", SearchOption.AllDirectories);
        Array.Sort(files, StringComparer.Ordinal); // order must be stable or the hash is meaningless

        using var sha = SHA256.Create();
        var buffer = new StringBuilder();

        foreach (var file in files)
        {
            var info = new FileInfo(file);
            buffer.Clear();
            buffer.Append(Path.GetRelativePath(contentPath, file).Replace('\\', '/'))
                  .Append('\0')
                  .Append(info.Length.ToString(CultureInfo.InvariantCulture))
                  .Append('\0')
                  .Append(info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture))
                  .Append('\n');

            var bytes = Encoding.UTF8.GetBytes(buffer.ToString());
            sha.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }

        sha.TransformFinalBlock([], 0, 0);

        return "sha256:" + Convert.ToHexStringLower(sha.Hash!);
    }
}
