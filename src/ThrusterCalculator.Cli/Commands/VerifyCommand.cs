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

        CheckExtractedConfig(installation, definitions, args, ref failures);

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

    /// <summary>
    /// Runs the real extraction and checks that nothing was quietly lost on the way out.
    /// </summary>
    /// <remarks>
    /// The checks above look at the raw definitions; these look at the artifact that actually ships.
    /// That distinction matters: every field a block <em>inherits</em> rather than states is present
    /// in the raw data and absent from the config if resolution breaks, so only a check on the
    /// output can see it.
    /// <para>
    /// This exists because it happened. <c>ConsumedResource.Type</c> is inherited by most thrusters,
    /// was read without walking the base chain, and silently vanished for 8 of 12 — no exception, no
    /// warning, just an empty fuel column in the UI. A per-field completeness check on the output is
    /// the cheapest thing that would have caught it on the day.
    /// </para>
    /// </remarks>
    private static void CheckExtractedConfig(
        Se2Installation installation, DefinitionSet definitions, string[] args, ref int failures)
    {
        var (occupancy, inheritance) = CommandContext.OpenEngineSources(
            installation, CommandContext.Flag(args, "--no-engine"));

        var data = new GameDataExtractor(definitions, occupancy, inheritance)
            .Extract("verify", "sha256:verify");

        Console.WriteLine();
        Console.WriteLine($"Extracted config : {data.Thrusters.Count} thrusters, "
                          + $"{data.Containers.Count} containers, {data.Tanks.Count} tanks, "
                          + $"{data.Planets.Count} planets");

        CheckAll("every thruster resolves its consumed resource", data.Thrusters,
            t => t.ConsumedResource is not null, t => t.Id, ref failures);

        CheckAll("every thruster resolves a density", data.Thrusters,
            t => t.Density is not null, t => t.Id, ref failures);

        CheckAll("every thruster has a cell count", data.Thrusters,
            t => t.OccupiedCells is not null, t => t.Id, ref failures);

        CheckAll("every tank resolves its resource", data.Tanks,
            t => t.Resource is not null, t => t.Id, ref failures);

        // Gravity is stated on the planet's gravity generator, usually inherited from a legacy
        // base template that encodes its components as a plain array rather than a delta. Reading
        // only the delta form silently lost it for 8 of 10 planets, which then read as "the game
        // does not ship surface gravity at all".
        CheckAll("every planet resolves its surface gravity", data.Planets,
            p => p.SurfaceGravity is > 0, p => p.Id, ref failures);

        // Schema.md R1: the config is the interface, and it must not leak the game's GUID graph.
        CheckAll("no GUID leaks into a reference", data.Thrusters,
            t => !Guid.TryParse(t.Density, out _)
                 && !Guid.TryParse(t.ConsumedResource?.Resource ?? string.Empty, out _),
            t => t.Id, ref failures);

        // Every reference must land somewhere. A dangling id reads as "unknown" downstream, which is
        // honest but wrong, and it would otherwise only show up as a blank cell in the UI.
        var densityIds = data.Densities.Select(d => d.Id).ToHashSet(StringComparer.Ordinal);
        var resourceIds = data.Resources.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);
        var classIds = data.ThrustClasses.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);

        CheckAll("every thruster reference resolves", data.Thrusters,
            t => densityIds.Contains(t.Density ?? string.Empty)
                 && resourceIds.Contains(t.ConsumedResource?.Resource ?? string.Empty)
                 && classIds.Contains(t.ThrustClass ?? string.Empty),
            t => t.Id, ref failures);
    }

    /// <summary>
    /// Asserts a property of every item, naming the ones that fail.
    /// </summary>
    /// <remarks>
    /// Naming them is the point. "8 thrusters failed" sends you looking; "hydrogenThruster50,
    /// hydrogenThruster200, …" tells you it is the whole hydrogen family and the cause is shared.
    /// </remarks>
    private static void CheckAll<T>(
        string description,
        IReadOnlyList<T> items,
        Func<T, bool> predicate,
        Func<T, string> name,
        ref int failures)
    {
        var bad = items.Where(i => !predicate(i)).Select(name).ToList();

        Check(description, bad.Count == 0,
            $"{bad.Count} of {items.Count} failed: {string.Join(", ", bad)}", ref failures);
    }

    private static void Check(string description, bool passed, string failureDetail, ref int failures)
    {
        Console.WriteLine($"  [{(passed ? "ok" : "FAIL")}] {description}");
        if (passed) return;

        Console.WriteLine($"         {failureDetail}");
        failures++;
    }
}
