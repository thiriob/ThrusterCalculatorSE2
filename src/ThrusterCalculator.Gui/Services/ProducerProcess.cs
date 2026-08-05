using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ThrusterCalculator.Gui.Services;

/// <summary>What a producer run produced, or why it could not run.</summary>
public sealed record ProducerResult(bool Succeeded, string Message);

/// <summary>What the local install currently looks like, for a staleness check.</summary>
public sealed record InstallFingerprint(string GameBuild, string Fingerprint);

/// <summary>
/// Runs <c>tc.exe</c> as a child process. The only place the GUI touches the producer.
/// </summary>
/// <remarks>
/// Never an assembly reference. Space Engineers ships its own copy of Avalonia in <c>Game2\</c>, so
/// an Avalonia app that loaded the game's assemblies would hit a version collision — the sibling
/// project learned this the hard way, and the producer/consumer split makes it structural rather
/// than a workaround (Technic §4). Batch in, JSON out, process exits.
/// <para>
/// Everything here degrades to an explanation rather than a dead button (Design §4.5.1): no
/// <c>tc.exe</c> beside the app, or no game installed, are both normal states for a consumer, not
/// errors to fail on.
/// </para>
/// </remarks>
public static class ProducerProcess
{
    public const string ExecutableName = "tc.exe";

    /// <summary>Where <c>tc.exe</c> is expected: alongside the app, as the release bundles it.</summary>
    public static string ExecutablePath { get; } =
        Path.Combine(AppContext.BaseDirectory, ExecutableName);

    public static bool IsAvailable => File.Exists(ExecutablePath);

    /// <summary>
    /// Asks the producer what the installed game currently looks like.
    /// </summary>
    /// <returns><c>null</c> when the producer is absent or no install could be found.</returns>
    public static async Task<InstallFingerprint?> ReadInstallAsync(CancellationToken cancellation)
    {
        if (!IsAvailable) return null;

        var (exitCode, stdout, _) = await RunAsync("fingerprint", cancellation).ConfigureAwait(false);
        if (exitCode != 0) return null;

        try
        {
            using var document = JsonDocument.Parse(stdout);
            var root = document.RootElement;

            return new InstallFingerprint(
                root.GetProperty("gameBuild").GetString() ?? "unknown",
                root.GetProperty("fingerprint").GetString() ?? string.Empty);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // A producer from a different version may print something we do not recognise. That is
            // a reason to skip the staleness check, not to fail the app.
            return null;
        }
    }

    /// <summary>Regenerates the config beside the executable, where the app looks for it.</summary>
    public static async Task<ProducerResult> ExtractAsync(CancellationToken cancellation)
    {
        if (!IsAvailable)
        {
            return new ProducerResult(false,
                $"{ExecutableName} is not bundled with this build. Run 'tc extract' yourself and "
                + $"put {ConfigSource.FileName} beside the app.");
        }

        var destination = Path.Combine(AppContext.BaseDirectory, ConfigSource.FileName);

        var (exitCode, _, stderr) = await RunAsync(
            $"extract --out \"{destination}\"", cancellation).ConfigureAwait(false);

        return exitCode == 0
            ? new ProducerResult(true, "Rebuilt from your installed game. Restart to load it.")
            : new ProducerResult(false, LastLine(stderr) ?? "The producer reported a failure.");
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(
        string arguments, CancellationToken cancellation)
    {
        var startInfo = new ProcessStartInfo(ExecutablePath, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync(cancellation);
        var stderr = process.StandardError.ReadToEndAsync(cancellation);

        try
        {
            await process.WaitForExitAsync(cancellation).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Cancellation has to actually stop the work, not just stop waiting for it: a scan of
            // 17k files would otherwise keep running with nobody listening.
            TryKill(process);
            throw;
        }

        return (process.ExitCode,
            await stdout.ConfigureAwait(false),
            await stderr.ConfigureAwait(false));
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or NotSupportedException)
        {
            // Already gone, which is the outcome we wanted.
        }
    }

    /// <summary>The producer's last diagnostic line — its actual complaint, not its banner.</summary>
    private static string? LastLine(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length > 0 ? lines[^1] : null;
    }
}
