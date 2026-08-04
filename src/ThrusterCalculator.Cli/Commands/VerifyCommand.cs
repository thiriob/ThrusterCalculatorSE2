using ThrusterCalculator.Extraction;

namespace ThrusterCalculator.Cli.Commands;

/// <summary>
/// Checks a real installation against the assumptions the app relies on.
/// </summary>
/// <remarks>
/// CI can never catch "Keen changed the data format" — there is no game on the runner — so this is
/// the canary, run by hand on patch day. It exists precisely so that check is available without
/// committing any of Keen's files to the repo (Technic.md §7.1.1).
/// </remarks>
internal static class VerifyCommand
{
    public static int Run(string[] args)
    {
        var installation = CommandContext.ResolveInstallation(args);
        if (installation is null) return 1;

        var definitions = DefinitionScanner.Scan(installation);
        var failures = 0;

        Console.WriteLine($"Definitions read : {definitions.All.Count:N0} of {definitions.FilesSeen:N0} files");
        Console.WriteLine($"Distinct types   : {definitions.CountsByType().Count:N0}");
        Console.WriteLine($"Max bundle stamp : {definitions.MaxBundleVersion() ?? "unknown"}");
        Console.WriteLine($"Fingerprint      : {ContentFingerprint.Compute(installation.ContentPath)}");
        Console.WriteLine();

        Check("every file parsed", definitions.Warnings.Count == 0,
            $"{definitions.Warnings.Count} file(s) failed to parse", ref failures);

        var allThrusters = definitions.OfType("ThrusterDefinitionObjectBuilder");
        var templates = allThrusters.Where(t => t.IsTemplate).ToList();
        var thrusters = allThrusters.Where(t => !t.IsTemplate).ToList();

        Check("thruster definitions found", thrusters.Count > 0,
            "no concrete ThrusterDefinitionObjectBuilder definitions", ref failures);

        // Templates are excluded deliberately: HydrogenThrusterDefinition is a base definition with
        // ThrustPower 0, and counting it here would fail this check for the wrong reason.
        Check("every thruster has positive thrust",
            thrusters.All(t => t.GetDouble("ThrustPower") is > 0),
            "at least one thruster has missing or non-positive ThrustPower", ref failures);

        Check("every thruster resolves a thrust class",
            thrusters.All(t => t.GetString("ThrustClass") is not null)
            || templates.Any(t => t.GetString("ThrustClass") is not null),
            "thrusters omit ThrustClass and no template supplies one", ref failures);

        Check("thrust class configuration found",
            definitions.OfType("ThrustClassesConfigurationObjectBuilder").Count == 1,
            "expected exactly one ThrustClassesConfigurationObjectBuilder", ref failures);

        Check("block mass configuration found",
            definitions.OfType("CubeBlockMassConfigurationObjectBuilder").Count == 1,
            "expected exactly one CubeBlockMassConfigurationObjectBuilder", ref failures);

        var densities = definitions.OfType("CubeBlockDensityDefinitionObjectBuilder");
        Check("density definitions found", densities.Count > 0,
            "no CubeBlockDensityDefinitionObjectBuilder definitions", ref failures);

        Check("every density has a mass curve modifier",
            densities.All(d => d.GetDouble("MassCurveModifier") is > 0),
            "at least one density is missing MassCurveModifier", ref failures);

        // The join a block depends on. If Keen ever restructures compositions this is the check
        // that says so, rather than the app quietly losing every thruster's mass and name.
        var compositions = BlockCompositionIndex.Build(definitions);
        var unpaired = allThrusters
            .Where(t => compositions.FindSibling(t, "PowerableBlockDefinitionObjectBuilder") is null)
            .ToList();

        Check("every thruster pairs with its block definition", unpaired.Count == 0,
            $"{unpaired.Count} thruster(s) have no PowerableBlockDefinition via composition: "
            + string.Join(", ", unpaired.Select(t => Path.GetFileName(t.RelativePath))),
            ref failures);

        Console.WriteLine();
        Console.WriteLine($"Thrusters: {thrusters.Count} concrete, {templates.Count} template(s)");
        foreach (var thruster in thrusters.OrderBy(t => t.RelativePath, StringComparer.Ordinal))
        {
            Console.WriteLine(Describe(thruster));
        }

        foreach (var template in templates.OrderBy(t => t.RelativePath, StringComparer.Ordinal))
        {
            Console.WriteLine(Describe(template) + "   [template]");
        }

        string Describe(DefinitionFile thruster)
        {
            var name = Path.GetFileNameWithoutExtension(thruster.RelativePath);
            var block = compositions.FindSibling(thruster, "PowerableBlockDefinitionObjectBuilder");

            // "no block" and "block with no name of its own" are different failures and must not
            // share a label — the second is normal, the first would be a broken join.
            string blockLabel;
            if (block is null)
            {
                blockLabel = "NO BLOCK";
            }
            else
            {
                var uiName = block.GetElement("UIData") is { } ui
                             && ui.TryGetProperty("Name", out var n)
                             && n.ValueKind == System.Text.Json.JsonValueKind.String
                    ? n.GetString()
                    : null;

                blockLabel = uiName ?? "(name inherited)";
            }

            return $"  {name,-52} class={thruster.GetString("ThrustClass") ?? "(inherited)",-12} "
                   + $"thrust={thruster.GetDouble("ThrustPower"),14:N1}  block={blockLabel}";
        }

        Console.WriteLine();
        if (failures == 0)
        {
            Console.WriteLine("All checks passed.");
            return 0;
        }

        Console.Error.WriteLine($"{failures} check(s) failed.");
        return 1;
    }

    private static void Check(string description, bool passed, string failureDetail, ref int failures)
    {
        Console.WriteLine($"  [{(passed ? "ok" : "FAIL")}] {description}");
        if (passed) return;

        Console.WriteLine($"         {failureDetail}");
        failures++;
    }
}
