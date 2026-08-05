using System.Text.Json;
using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>
/// Prints the local install's fingerprint and build, as JSON on stdout.
/// </summary>
/// <remarks>
/// This is how the desktop app answers "has the game changed since this config was made?" without
/// breaking the rule that the consumer touches no game files (Technic §1). The GUI spawns this,
/// reads one small object, and compares it against the <c>source</c> block of the config it loaded
/// — the same shelling-out pattern the Rebuild button uses, so no new coupling appears.
/// <para>
/// Deliberately cheap: the fingerprint is a hash over <c>(relative path, size, mtime)</c> of the
/// definition files, which is a directory enumeration rather than 17k reads, so it is fast enough
/// to run on every launch.
/// </para>
/// </remarks>
internal static class FingerprintCommand
{
    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var definitions = DefinitionScanner.Scan(installation);

        var payload = new
        {
            gameBuild = definitions.MaxBundleVersion() ?? "unknown",
            fingerprint = ContentFingerprint.Compute(installation.ContentPath),
        };

        // stdout stays machine-readable; every diagnostic above went to stderr.
        Console.WriteLine(JsonSerializer.Serialize(payload));
        return 0;
    }
}
