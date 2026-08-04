using System.Text.Json;
using ThrusterCalculator.Model;

namespace ThrusterCalculator.Extraction;

/// <summary>
/// Projects a scanned installation into the <c>gamedata.json</c> contract.
/// </summary>
/// <remarks>
/// This is where the GUID graph stops and the flat, fully-resolved config begins. Everything
/// downstream — the GUI, a web frontend, the sizing math — sees only the output, never a GUID
/// (Schema.md R1).
/// </remarks>
public sealed class GameDataExtractor
{
    private const string ThrusterType = "ThrusterDefinitionObjectBuilder";
    private const string PowerableType = "PowerableBlockDefinitionObjectBuilder";

    /// <summary>
    /// The definition types that describe a placeable block, in specificity order.
    /// </summary>
    /// <remarks>
    /// There is no single block-definition type. Thrusters use <c>PowerableBlockDefinition</c>,
    /// tanks use <c>FunctionalBlockDefinition</c>, and plainer blocks use <c>CubeBlockDefinition</c>
    /// — all three carry <c>Density</c> and <c>UIData</c>. Hardcoding the first of them silently
    /// dropped every tank, so the lookup tries each in turn.
    /// </remarks>
    private static readonly string[] BlockDefinitionTypes =
    [
        PowerableType,
        "FunctionalBlockDefinitionObjectBuilder",
        "CubeBlockDefinitionObjectBuilder",
        "ArmorBlockDefinitionObjectBuilder",
    ];
    private const string DensityType = "CubeBlockDensityDefinitionObjectBuilder";
    private const string ResourceType = "ResourceTypeDefinitionObjectBuilder";
    private const string ThrustClassesType = "ThrustClassesConfigurationObjectBuilder";
    private const string MassConfigType = "CubeBlockMassConfigurationObjectBuilder";
    private const string InventoryType = "InventoryDefinitionObjectBuilder";
    private const string ResourceContainerType = "ResourceContainerDefinitionObjectBuilder";
    private const string PlanetInfoType = "PlanetInfoDefinitionObjectBuilder";
    private const string BlockModelType = "BlockModelComponentDefinitionObjectBuilder";

    private readonly DefinitionSet _definitions;
    private readonly BlockCompositionIndex _compositions;
    private readonly List<ExtractionWarning> _warnings;
    private readonly IOccupancySource _occupancy;
    private readonly IDefinitionInheritance _inheritance;

    public GameDataExtractor(
        DefinitionSet definitions,
        IOccupancySource? occupancy = null,
        IDefinitionInheritance? inheritance = null)
    {
        ArgumentNullException.ThrowIfNull(definitions);

        _definitions = definitions;
        _compositions = BlockCompositionIndex.Build(definitions);
        _warnings = [.. definitions.Warnings];
        _occupancy = occupancy ?? new TableOccupancySource();
        _inheritance = inheritance ?? new NoDefinitionInheritance();
    }

    /// <summary>Which occupancy source answered, recorded so the config's origin is traceable.</summary>
    public string OccupancySourceName => _occupancy.Name;

    /// <summary>Which inheritance source answered.</summary>
    public string InheritanceSourceName => _inheritance.Name;

    /// <summary>Longest base chain we will walk, guarding against a cycle in the data.</summary>
    private const int MaxInheritanceDepth = 16;

    /// <summary>A definition and each of its ancestors, nearest first.</summary>
    private IEnumerable<DefinitionFile> BaseChain(DefinitionFile? start)
    {
        var current = start;
        var guid = start?.Guid;

        for (var depth = 0; depth < MaxInheritanceDepth; depth++)
        {
            if (current is null) yield break;

            yield return current;

            guid = guid is null ? null : _inheritance.BaseOf(guid);
            if (guid is null) yield break;

            current = _definitions.Resolve(guid);
        }
    }

    /// <summary>
    /// A field from a definition, or from the nearest ancestor that states it.
    /// </summary>
    /// <remarks>
    /// Walks <c>BaseGuid</c>, the game's own parent pointer. Blocks routinely omit what their base
    /// states: no cargo container declares a density, and the value sits one level up.
    /// </remarks>
    private string? InheritedString(DefinitionFile? definition, string field)
    {
        var current = definition;
        var guid = definition?.Guid;

        for (var depth = 0; depth < MaxInheritanceDepth; depth++)
        {
            if (current?.GetString(field) is { Length: > 0 } value) return value;

            guid = guid is null ? null : _inheritance.BaseOf(guid);
            if (guid is null) return null;

            current = _definitions.Resolve(guid);
        }

        return null;
    }

    /// <summary>
    /// Cells occupied by a block, resolving its model through the composite graph first.
    /// </summary>
    /// <remarks>
    /// The engine keys occupancy by <em>model</em> asset, not by block, so the block's
    /// BlockModelComponentDefinition has to be found and its <c>Model</c> reference read. That
    /// reference is written as <c>{G}&lt;guid&gt;</c>.
    /// </remarks>
    private OccupancyResult OccupiedCellsFor(DefinitionFile anchor, string blockName)
    {
        var model = _compositions.FindSibling(anchor, BlockModelType)?.GetString("Model");
        Guid? modelGuid = null;

        if (model is { Length: > 0 })
        {
            var text = model.StartsWith("{G}", StringComparison.Ordinal) ? model[3..] : model;
            if (Guid.TryParse(text, out var parsed)) modelGuid = parsed;
        }

        return _occupancy.OccupiedCells(blockName, modelGuid);
    }

    public GameData Extract(string toolVersion, string fingerprint)
    {
        var densities = ExtractDensities();
        var resources = ExtractResources();

        return new GameData
        {
            SchemaVersion = SchemaVersion.Current.ToString(),
            Generator = new GeneratorInfo
            {
                Tool = "tc",
                Version = toolVersion,
                ExtractedAt = DateTimeOffset.UtcNow,
            },
            Source = new SourceInfo
            {
                GameBuild = _definitions.MaxBundleVersion() ?? "unknown",
                Fingerprint = fingerprint,
                DefinitionCounts = _definitions.CountsByType(),
            },
            Models = ExtractModels(),
            Densities = densities,
            Resources = resources,
            ThrustClasses = ExtractThrustClasses(),
            Thrusters = ExtractThrusters(),
            Containers = ExtractContainers(),
            Tanks = ExtractTanks(),
            Planets = ExtractPlanets(),
            Warnings = _warnings,
        };
    }

    // ── models ────────────────────────────────────────────────────────────────────────────────

    private CalculationModels ExtractModels()
    {
        var config = _definitions.OfType(MassConfigType).FirstOrDefault();
        var minBlockMass = config?.GetDouble("MinBlockMass");

        if (minBlockMass is null)
        {
            Warn("missingMassConfiguration",
                "No CubeBlockMassConfiguration found; falling back to a floor of 5 kg.", config?.RelativePath);
        }

        return new CalculationModels
        {
            BlockMass = new BlockMassModel
            {
                Kind = "sqrtLog10CellCount",
                MinBlockMass = minBlockMass ?? 5.0,
            },
            ThrustEffectiveness = new ThrustEffectivenessModel { Kind = "linearRampAirDensity" },
            AtmosphereDensity = new AtmosphereDensityModel { Kind = "linearRampAltitude" },
        };
    }

    // ── lookup tables ─────────────────────────────────────────────────────────────────────────

    private List<Density> ExtractDensities() =>
        _definitions.OfType(DensityType)
            .Where(d => d.Guid is not null)
            .Select(d => new Density
            {
                Id = d.Guid!,
                Name = Path.GetFileNameWithoutExtension(d.RelativePath),
                MassCurveModifier = d.GetDouble("MassCurveModifier") ?? 0,
            })
            .ToList();

    private List<Model.Resource> ExtractResources() =>
        _definitions.OfType(ResourceType)
            .Where(r => r.Guid is not null)
            .Select(r => new Model.Resource
            {
                Id = r.Guid!,
                Name = r.GetString("Name") ?? Path.GetFileNameWithoutExtension(r.RelativePath),
                FlowRateUnits = r.GetString("FlowRateUnits") ?? "unknown",
                StorageUnits = r.GetString("StorageUnits") ?? "unknown",
                RequiresConveyors = r.GetBoolean("RequiresConveyors") ?? false,
            })
            .ToList();

    private List<Model.ThrustClass> ExtractThrustClasses()
    {
        var config = _definitions.OfType(ThrustClassesType).FirstOrDefault();
        if (config?.GetElement("ThrustClasses") is not { ValueKind: JsonValueKind.Array } array)
        {
            Warn("missingThrustClasses",
                "No ThrustClassesConfiguration found; every thruster will be treated as having no "
                + "environmental falloff.", config?.RelativePath);
            return [];
        }

        var classes = new List<Model.ThrustClass>();
        foreach (var entry in array.EnumerateArray())
        {
            if (!entry.TryGetProperty("$Key", out var key)
                || !entry.TryGetProperty("$Value", out var value))
            {
                continue;
            }

            classes.Add(new Model.ThrustClass
            {
                Id = key.GetString() ?? "unknown",
                MaxThrustAirDensity = Number(value, "MaxThrustAirDensity") ?? 0,
                MinThrustAirDensity = Number(value, "MinThrustAirDensity") ?? -1,
                WaterSubmersionTolerance = Number(value, "WaterSubmersionTolerance") ?? 1,
                WaterOnly = Boolean(value, "WaterOnly") ?? false,
            });
        }

        return classes;
    }

    // ── blocks ────────────────────────────────────────────────────────────────────────────────

    private List<Thruster> ExtractThrusters()
    {
        var thrusters = new List<Thruster>();

        foreach (var definition in _definitions.OfType(ThrusterType).Where(d => !d.IsTemplate))
        {
            var blockName = BlockNaming.BlockNameOf(definition.RelativePath);
            var block = _compositions.FindSibling(definition, PowerableType);

            if (block is null)
            {
                Warn("unpairedBlock",
                    $"{blockName}: no PowerableBlockDefinition reachable through its composite; "
                    + "density and consumed resource are unavailable.", definition.RelativePath);
            }

            // Absent on hydrogen thrusters, which inherit it from their template.
            var thrustClass = definition.GetString("ThrustClass");
            var provenance = new Dictionary<string, Provenance>(StringComparer.Ordinal);

            if (thrustClass is null)
            {
                thrustClass = InheritedString(definition, "ThrustClass");
                if (thrustClass is null)
                {
                    Warn("unresolvedThrustClass",
                        $"{blockName}: no ThrustClass, and none inherited from a template.",
                        definition.RelativePath);
                }
            }

            var occupancy = OccupiedCellsFor(definition, blockName);
            var cells = occupancy.Cells;
            provenance["occupiedCells"] = occupancy.Provenance;

            if (cells is null)
            {
                Warn("unknownOccupiedCells",
                    $"{blockName}: no cell count available, so its mass cannot be computed.",
                    definition.RelativePath);
            }

            var density = ResolveDensity(definition, block, blockName);
            if (density is null)
            {
                provenance["density"] = Provenance.Unknown;
            }

            thrusters.Add(new Thruster
            {
                Id = BlockNaming.IdOf(blockName),
                Name = BlockNaming.DisplayNameOf(blockName),
                ThrustClass = thrustClass,
                SizeCm = BlockNaming.SizeCmOf(blockName) ?? 0,
                ThrustNewtons = definition.GetDouble("ThrustPower"),
                ConsumedResource = ReadConsumedResource(definition, block),
                Density = density,
                OccupiedCells = cells,
                Implemented = true,
                ProvenanceOverrides = provenance,
            });
        }

        return thrusters.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// A block's density reference, from its own definition or the template it inherits from.
    /// </summary>
    /// <remarks>
    /// Most thrusters do not restate <c>Density</c> — only Atmospheric 1 m and the ion family do —
    /// so without template fallback seven of twelve lose their mass entirely. Exactly the same
    /// inheritance that supplies hydrogen's <c>ThrustClass</c>.
    /// </remarks>
    private string? ResolveDensity(DefinitionFile anchor, DefinitionFile? block, string blockName)
    {
        var density = InheritedString(block, "Density");

        if (density is null)
        {
            Warn("unresolvedDensity",
                $"{blockName}: no Density on its block definition and none inherited, so its mass "
                + "cannot be computed.", anchor.RelativePath);
        }

        return density;
    }

    /// <summary>The block definition a component belongs to, whichever flavour it is.</summary>
    private DefinitionFile? FindBlockDefinition(DefinitionFile anchor)
    {
        foreach (var type in BlockDefinitionTypes)
        {
            if (_compositions.FindSibling(anchor, type) is { } block) return block;
        }

        return null;
    }

    private ConsumedResource? ReadConsumedResource(DefinitionFile thruster, DefinitionFile? block)
    {
        var rate = thruster.GetDouble("ResourcesRequiredToThrust");
        if (rate is null) return null;

        var resource = block?.GetElement("ConsumedResource") is { ValueKind: JsonValueKind.Object } c
                       && c.TryGetProperty("Type", out var type)
                       && type.ValueKind == JsonValueKind.String
            ? type.GetString()
            : null;

        return resource is null ? null : new ConsumedResource { Resource = resource, RatePerThrust = rate.Value };
    }

    /// <summary>
    /// Capacity above which a value is treated as "effectively unlimited" rather than real.
    /// </summary>
    /// <remarks>
    /// Trade terminals and contract blocks declare a capacity of about 9.2e12 kg — the maximum of
    /// the engine's fixed-point type, meaning unbounded. Left as-is it would dominate any total and
    /// read as a real number.
    /// </remarks>
    private const double UnboundedCapacityKg = 1e12;

    private List<Container> ExtractContainers()
    {
        var containers = new List<Container>();

        // A block can declare several inventories (an assembler has input and output), which is why
        // grouping matters: without it the same block appears twice with duplicate ids, and its
        // capacity is understated. Total capacity is what a mass calculation needs.
        foreach (var group in _definitions.OfType(InventoryType)
                     .Where(d => !d.IsTemplate && d.GetDouble("MaxMass") is not null)
                     .GroupBy(d => BlockNaming.BlockNameOf(d.RelativePath), StringComparer.Ordinal))
        {
            var blockName = group.Key;
            var anchor = group.First();
            var block = FindBlockDefinition(anchor);

            // Character, backpack and datapad inventories are not placeable blocks and have no
            // block definition behind them. Requiring one is what separates blocks from items,
            // without hardcoding a list of names.
            if (block is null) continue;

            var capacity = group.Sum(d => d.GetDouble("MaxMass") ?? 0);
            if (capacity >= UnboundedCapacityKg)
            {
                Warn("unboundedCapacity",
                    $"{blockName}: declares an effectively unlimited inventory "
                    + $"({capacity:E2} kg); treat with care in any total.", anchor.RelativePath);
            }

            var occupancy = OccupiedCellsFor(anchor, blockName);
            var cells = occupancy.Cells;

            containers.Add(new Container
            {
                Id = BlockNaming.IdOf(blockName),
                Name = BlockNaming.DisplayNameOf(blockName),
                MaxMassKg = capacity,
                Density = ResolveDensity(anchor, block, blockName),
                OccupiedCells = cells,
                ProvenanceOverrides = new Dictionary<string, Provenance>(StringComparer.Ordinal)
                {
                    ["occupiedCells"] = occupancy.Provenance,
                },
            });
        }

        return containers.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    private List<Tank> ExtractTanks()
    {
        var tanks = new List<Tank>();

        foreach (var group in _definitions.OfType(ResourceContainerType)
                     .Where(d => !d.IsTemplate && d.GetDouble("MaxCapacity") is not null)
                     .GroupBy(d => BlockNaming.BlockNameOf(d.RelativePath), StringComparer.Ordinal))
        {
            var blockName = group.Key;
            var anchor = group.First();
            var block = FindBlockDefinition(anchor);
            if (block is null) continue;

            var occupancy = OccupiedCellsFor(anchor, blockName);
            var cells = occupancy.Cells;

            // Hydrogen tanks omit ResourceType and inherit it, exactly as they do ThrustClass.
            var resource = InheritedString(anchor, "ResourceType");

            if (resource is null)
            {
                Warn("unresolvedTankResource",
                    $"{blockName}: no ResourceType and none inherited, so its contents cannot be "
                    + "identified.", anchor.RelativePath);
            }

            tanks.Add(new Tank
            {
                Id = BlockNaming.IdOf(blockName),
                Name = BlockNaming.DisplayNameOf(blockName),
                Resource = resource,
                MaxCapacity = group.Max(d => d.GetDouble("MaxCapacity") ?? 0),
                MaxDischargeRate = anchor.GetDouble("MaxDischargeRate"),
                Density = ResolveDensity(anchor, block, blockName),
                OccupiedCells = cells,
                ProvenanceOverrides = new Dictionary<string, Provenance>(StringComparer.Ordinal)
                {
                    ["occupiedCells"] = occupancy.Provenance,
                },
            });
        }

        return tanks.OrderBy(t => t.Id, StringComparer.Ordinal).ToList();
    }

    // ── planets ───────────────────────────────────────────────────────────────────────────────

    private List<Planet> ExtractPlanets()
    {
        var planets = new Dictionary<string, Planet>(StringComparer.OrdinalIgnoreCase);
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var info in _definitions.OfType(PlanetInfoType))
        {
            var rawName = info.GetString("Name") ?? Path.GetFileNameWithoutExtension(info.RelativePath);
            var milestone = MilestoneOf(info.RelativePath);
            var (displayName, _) = SplitMilestoneSuffix(rawName);
            var id = BlockNaming.IdOf(displayName.Replace(" ", string.Empty, StringComparison.Ordinal));

            var geometry = ReadPlanetGeometry(info);
            var provenance = new Dictionary<string, Provenance>(StringComparer.Ordinal)
            {
                // Surface gravity is world-instance data and is never in the shipped definitions
                // (Research.md §5.3), so it is always the user's to supply.
                ["surfaceGravity"] = Provenance.Unknown,
            };

            var atmosphere = geometry.Atmosphere;
            if (!geometry.Measured)
            {
                atmosphere = StandardAtmosphere;
                provenance["atmosphere"] = Provenance.Assumed;
            }
            else if (atmosphere is { } a && a.AffectDistance > ImplausibleAtmosphereExtent)
            {
                // Real, inherited, and almost certainly not meaningful: the legacy planet template
                // declares an atmosphere reaching 100 planet radii. Surface density is unaffected —
                // which is all v1 uses — but any altitude calculation would be nonsense.
                Warn("implausibleAtmosphere",
                    $"{displayName}: inherits an atmosphere extending to {a.AffectDistance:0.##} "
                    + "planet radii, which is unlikely to be meaningful. Surface density is "
                    + "unaffected; treat altitude results with suspicion.",
                    info.RelativePath);
            }

            if (geometry.GravityAffectDistance is null)
            {
                provenance["gravityAffectDistance"] = Provenance.Unknown;
            }

            var planet = new Planet
            {
                Id = id,
                Name = displayName,
                Milestone = milestone,
                SurfaceGravity = null,
                GravityAffectDistance = geometry.GravityAffectDistance,
                Atmosphere = atmosphere,
                ProvenanceOverrides = provenance,
            };

            // The game ships milestone-versioned duplicates (Verdure exists under VS2_0 and VS2_3).
            // Newest wins, so a consumer never sees the same planet twice.
            if (!planets.TryGetValue(id, out var existing)
                || string.CompareOrdinal(milestone, existing.Milestone) > 0)
            {
                planets[id] = planet;
                sources[id] = info.RelativePath;
            }
        }

        // Warn only for the planets that survive deduplication. Warning inside the loop above would
        // report on discarded milestone variants that never reach the config.
        foreach (var planet in planets.Values.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            if (planet.ProvenanceOf("atmosphere") == Provenance.Assumed)
            {
                Warn("assumedAtmosphere",
                    $"{planet.Name}: atmosphere geometry is not stated on this planet and is "
                    + "inherited from a base we cannot resolve; using the standard shape "
                    + "(full density to 1.08 R, zero at 1.15 R).",
                    sources[planet.Id]);
            }
        }

        return planets.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// Standard atmosphere shape, used when a planet's own is not recoverable.
    /// </summary>
    /// <remarks>
    /// Taken from the VS1_5 planets, the only ones that state it explicitly. Applied as an
    /// <see cref="Provenance.Assumed"/> value rather than left null, because a null atmosphere means
    /// <em>airless</em> to the calculator — which would silently zero every atmospheric thruster on
    /// planets that plainly have air. A visibly assumed value the user can correct beats a confident
    /// wrong one.
    /// </remarks>
    private static Atmosphere StandardAtmosphere =>
        new() { ConstantAffectDistance = 1.08, AffectDistance = 1.15 };

    /// <summary>
    /// Atmosphere extent, in planet radii, beyond which the value is reported as suspect.
    /// </summary>
    /// <remarks>
    /// Every planet that states its own geometry uses 1.15. The legacy planet template says 100,
    /// which the older VS1_5 planets inherit — real data, faithfully resolved, but not a number to
    /// build an altitude model on.
    /// </remarks>
    private const double ImplausibleAtmosphereExtent = 5.0;

    private readonly record struct PlanetGeometry(
        double? GravityAffectDistance, Atmosphere? Atmosphere, bool Measured);

    /// <summary>
    /// Follows <c>InfoDefinition -> Spawn -> prefab -> composite</c> looking for the gravity and
    /// atmosphere components.
    /// </summary>
    /// <remarks>
    /// Both are written inline in a delta — the values themselves rather than GUID references — so
    /// a shallow read gets them without implementing engine inheritance (Technic.md §7.3).
    /// <para>
    /// Only the VS1_5 planets state them on their own prefab. Newer planets inherit from a base
    /// composite with no traceable parent link, so nothing is found and the standard shape is
    /// substituted.
    /// </para>
    /// </remarks>
    private PlanetGeometry ReadPlanetGeometry(DefinitionFile info)
    {
        var prefab = _definitions.Resolve(info.GetString("Spawn"));

        double? gravity = null;
        double? affect = null;
        double? constant = null;

        foreach (var component in PlanetComponents(prefab))
        {
            var type = component.TryGetProperty("$Type", out var t)
                ? t.GetString() ?? string.Empty
                : string.Empty;

            if (type.Contains("GravityGeneratorObjectBuilder", StringComparison.Ordinal))
            {
                gravity ??= Number(component, "AffectDistance");
            }
            else if (type.Contains("AtmosphereGeneratorObjectBuilder", StringComparison.Ordinal))
            {
                affect ??= Number(component, "AffectDistance");
                constant ??= Number(component, "ConstantAffectDistance");
            }
        }

        return affect is { } a && constant is { } c
            ? new PlanetGeometry(gravity, new Atmosphere { AffectDistance = a, ConstantAffectDistance = c }, true)
            : new PlanetGeometry(gravity, null, false);
    }

    /// <summary>
    /// Inline component object builders reachable from a planet's prefab — its own delta first,
    /// then its composite's.
    /// </summary>
    private IEnumerable<JsonElement> PlanetComponents(DefinitionFile? prefab)
    {
        // Walk the prefab's inheritance chain, and for each link its composite's chain too.
        // Only the VS1_5 planets state gravity and atmosphere on their own prefab; the rest
        // inherit them, which is why nothing was found before the BaseGuid pointer existed.
        foreach (var link in BaseChain(prefab))
        {
            if (link.GetElement("_entity") is not { ValueKind: JsonValueKind.Object } entity)
            {
                continue;
            }

            if (entity.TryGetProperty("ObjectBuilders", out var builders))
            {
                foreach (var component in ChangedValues(builders)) yield return component;
            }

            var compositeGuid = entity.TryGetProperty("Definition", out var def)
                                && def.ValueKind == JsonValueKind.String
                ? def.GetString()
                : null;

            foreach (var composite in BaseChain(_definitions.Resolve(compositeGuid)))
            {
                if (composite.GetElement("Components") is { } components)
                {
                    foreach (var component in ChangedValues(components)) yield return component;
                }
            }
        }
    }

    private static IEnumerable<JsonElement> ChangedValues(JsonElement container)
    {
        if (container.ValueKind != JsonValueKind.Object
            || !container.TryGetProperty("Changed", out var changed)
            || changed.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var entry in changed.EnumerateArray())
        {
            if (entry.ValueKind == JsonValueKind.Object
                && entry.TryGetProperty("Value", out var value)
                && value.ValueKind == JsonValueKind.Object)
            {
                yield return value;
            }
        }
    }

    // ── helpers ───────────────────────────────────────────────────────────────────────────────

    private static string? MilestoneOf(string relativePath)
    {
        var parts = relativePath.Split('/');
        return Array.Find(parts, p => p.StartsWith("VS", StringComparison.Ordinal));
    }

    /// <summary>Strips a milestone suffix such as <c>Verdure_VS2-3</c> → <c>Verdure</c>.</summary>
    private static (string Name, string? Suffix) SplitMilestoneSuffix(string name)
    {
        var underscore = name.IndexOf("_VS", StringComparison.OrdinalIgnoreCase);
        return underscore > 0 ? (name[..underscore], name[underscore..]) : (name, null);
    }

    private static double? Number(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(field, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetDouble()
            : null;

    private static bool? Boolean(JsonElement element, string field) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(field, out var value)
            ? value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null,
            }
            : null;

    private void Warn(string code, string detail, string? file) =>
        _warnings.Add(new ExtractionWarning { Code = code, Detail = detail, File = file });
}
