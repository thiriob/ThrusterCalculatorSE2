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
    private string? InheritedString(DefinitionFile? definition, string field) =>
        BaseChain(definition)
            .Select(d => d.GetString(field))
            .FirstOrDefault(v => v is { Length: > 0 });

    /// <summary>A numeric field from a definition or the nearest ancestor stating it.</summary>
    private double? InheritedDouble(DefinitionFile? definition, string field) =>
        BaseChain(definition)
            .Select(d => d.GetDouble(field))
            .FirstOrDefault(v => v is not null);

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

    /// <summary>
    /// GUID → config id, for the lookup tables blocks point at.
    /// </summary>
    /// <remarks>
    /// The game references densities and resources by GUID; the config must not (Schema.md R1), so
    /// the producer resolves them here and every block emits a readable id. Built before the block
    /// collections, because those depend on it.
    /// </remarks>
    private readonly Dictionary<string, string> _idByGuid =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Thrust-class game key (<c>"Ion"</c>) → config id (<c>"ion"</c>).</summary>
    private readonly Dictionary<string, string> _thrustClassIdByKey =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Ids already handed out, per collection, so a clash cannot pass unnoticed.</summary>
    private readonly Dictionary<string, HashSet<string>> _idsByKind =
        new(StringComparer.Ordinal);

    public GameData Extract(string toolVersion, string fingerprint)
    {
        // Order matters: these three populate the id maps that every block below resolves through.
        var densities = ExtractDensities();
        var resources = ExtractResources();
        var thrustClasses = ExtractThrustClasses();

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
            ThrustClasses = thrustClasses,
            Thrusters = ExtractThrusters(),
            Containers = ExtractContainers(),
            Tanks = ExtractTanks(),
            Planets = ExtractPlanets(),
            Limits = ExtractLimits(),
            Warnings = _warnings,
        };
    }

    /// <summary>
    /// Engine-wide physics limits, from the single <c>PhysicsSessionConfiguration</c>.
    /// </summary>
    /// <remarks>
    /// Left <c>null</c> rather than defaulted if the definition is missing or malformed. A guessed
    /// speed limit would change what the climb profile says a ship can reach, and inventing a
    /// number that governs an answer is exactly what this producer does not do.
    /// </remarks>
    private WorldLimits? ExtractLimits()
    {
        var configuration = _definitions
            .OfType("PhysicsSessionConfigurationObjectBuilder")
            .FirstOrDefault();

        if (configuration is null)
        {
            Warn("missingPhysicsConfiguration",
                "No PhysicsSessionConfiguration found, so the engine speed limit is unknown. The "
                + "climb profile will not claim a ship coasts through a stretch it cannot hover in.",
                null);

            return null;
        }

        if (configuration.GetDouble("MaximumSpeedLinear") is not { } maxSpeed || maxSpeed <= 0)
        {
            Warn("missingSpeedLimit",
                $"{configuration.RelativePath} states no usable MaximumSpeedLinear.",
                configuration.RelativePath);

            return null;
        }

        return new WorldLimits { MaxSpeedMetresPerSecond = maxSpeed };
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
            .Select(d =>
            {
                var name = Path.GetFileNameWithoutExtension(d.RelativePath);
                return new Density
                {
                    Id = RegisterId(d.Guid!, name, "density"),
                    Name = name,
                    MassCurveModifier = d.GetDouble("MassCurveModifier") ?? 0,
                };
            })
            .ToList();

    private List<Model.Resource> ExtractResources() =>
        _definitions.OfType(ResourceType)
            .Where(r => r.Guid is not null)
            .Select(r => new Model.Resource
            {
                // Slugged from the filename, not from Name: the latter is "ResourceElectricity",
                // which would give an id of resourceElectricity. Schema.md §4.2 wants "electricity".
                Id = RegisterId(r.Guid!, Path.GetFileNameWithoutExtension(r.RelativePath), "resource"),
                Name = r.GetString("Name") ?? Path.GetFileNameWithoutExtension(r.RelativePath),
                FlowRateUnits = r.GetString("FlowRateUnits") ?? "unknown",
                StorageUnits = r.GetString("StorageUnits") ?? "unknown",
                RequiresConveyors = r.GetBoolean("RequiresConveyors") ?? false,
            })
            .ToList();

    /// <summary>
    /// Maps a definition's GUID to a readable config id, guarding against collisions.
    /// </summary>
    /// <remarks>
    /// A collision would be quietly destructive — two densities sharing an id means every block
    /// pointing at one of them silently gets the other's mass curve — so it falls back to the GUID
    /// and warns. Ugly in the file, but correct, and visible.
    /// <para>
    /// Uniqueness is per <paramref name="kind"/>, not global: densities and resources are separate
    /// collections in the config, so a density and a resource sharing a slug is not a conflict and
    /// must not be reported as one.
    /// </para>
    /// </remarks>
    private string RegisterId(string guid, string name, string kind)
    {
        var taken = _idsByKind.TryGetValue(kind, out var existing)
            ? existing
            : _idsByKind[kind] = new HashSet<string>(StringComparer.Ordinal);

        var slug = BlockNaming.SlugOf(name);

        if (slug.Length == 0 || !taken.Add(slug))
        {
            Warn("ambiguousId",
                $"{kind} '{name}' does not yield a unique readable id; falling back to its GUID.",
                null);
            slug = guid;
        }

        _idByGuid[guid] = slug;
        return slug;
    }

    /// <summary>
    /// Resolves a GUID reference to the config id of the thing it points at.
    /// </summary>
    /// <remarks>
    /// A GUID that resolves to nothing is left as-is rather than dropped: the consumer then fails to
    /// look it up and reports "mass unknown", which is the honest outcome. Substituting a default
    /// here is what produced 8-tonne thrusters weighing 5 kg (see <c>ThrusterSizer.BlockMassKg</c>).
    /// </remarks>
    private string? ResolveId(string? guid) =>
        guid is not null && _idByGuid.TryGetValue(guid, out var id) ? id : guid;

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

            var gameKey = key.GetString() ?? "unknown";
            var id = BlockNaming.SlugOf(gameKey);
            _thrustClassIdByKey[gameKey] = id;

            classes.Add(new Model.ThrustClass
            {
                Id = id,
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
                    + "density and consumed resource are unavailable.", definition.RelativePath,
                    BlockNaming.IdOf(blockName));
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
                        definition.RelativePath, BlockNaming.IdOf(blockName));
                }
            }

            var occupancy = OccupiedCellsFor(definition, blockName);
            var cells = occupancy.Cells;
            provenance["occupiedCells"] = occupancy.Provenance;

            if (cells is null)
            {
                Warn("unknownOccupiedCells",
                    $"{blockName}: no cell count available, so its mass cannot be computed.",
                    definition.RelativePath, BlockNaming.IdOf(blockName));
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
                ThrustClass = ResolveThrustClassId(thrustClass),
                SizeCm = BlockNaming.SizeCmOf(blockName) ?? 0,
                ThrustNewtons = definition.GetDouble("ThrustPower"),
                ConsumedResource = ReadConsumedResource(definition, block, blockName),
                Density = ResolveId(density),
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
                + "cannot be computed.", anchor.RelativePath, BlockNaming.IdOf(blockName));
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

    /// <summary>
    /// What a thruster burns, and how fast.
    /// </summary>
    /// <remarks>
    /// The rate sits on the thruster definition; the resource <em>identity</em> sits on the block
    /// definition's <c>ConsumedResource.Type</c> — and is almost always inherited, so this walks
    /// <c>BaseGuid</c> like every other field (§7.2.2). Reading only the block's own file resolved
    /// just 4 of 12 thrusters, silently, leaving both hydrogen and most of the atmospheric family
    /// with no fuel figure at all.
    /// <para>
    /// The catch that makes this more than a one-line walk: a child <b>restates</b>
    /// <c>ConsumedResource</c> carrying only <c>Amount</c>, so the object is present while the
    /// <c>Type</c> inside it is not. The merge therefore has to happen on the inner field — a
    /// field-level "is it present? then stop" walk still finds nothing.
    /// </para>
    /// </remarks>
    private ConsumedResource? ReadConsumedResource(
        DefinitionFile thruster, DefinitionFile? block, string blockName)
    {
        var rate = InheritedDouble(thruster, "ResourcesRequiredToThrust");
        var resource = InheritedConsumedResourceType(block);

        if (rate is null || resource is null)
        {
            Warn("unresolvedConsumedResource",
                $"{blockName}: {(rate is null ? "no consumption rate" : "no resource type")} "
                + "found on the block or anywhere in its inheritance chain, so its fuel or power "
                + "draw cannot be reported.", thruster.RelativePath, BlockNaming.IdOf(blockName));

            return null;
        }

        return new ConsumedResource
        {
            Resource = ResolveId(resource) ?? resource,
            RatePerThrust = rate.Value,
        };
    }

    /// <summary>
    /// A thrust class's config id, from the game key the block states.
    /// </summary>
    /// <remarks>
    /// An unmapped key is passed through rather than nulled, so a class the configuration file does
    /// not declare shows up as a dangling reference the consumer reports, not as a thruster that
    /// silently loses its environmental falloff.
    /// </remarks>
    private string? ResolveThrustClassId(string? gameKey) =>
        gameKey is not null && _thrustClassIdByKey.TryGetValue(gameKey, out var id) ? id : gameKey;

    /// <summary>The nearest <c>ConsumedResource.Type</c> in a block's inheritance chain.</summary>
    private string? InheritedConsumedResourceType(DefinitionFile? block)
    {
        foreach (var link in BaseChain(block))
        {
            if (link.GetElement("ConsumedResource") is { ValueKind: JsonValueKind.Object } consumed
                && consumed.TryGetProperty("Type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() is { Length: > 0 } guid)
            {
                return guid;
            }
        }

        return null;
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
                    + $"({capacity:E2} kg); treat with care in any total.", anchor.RelativePath,
                    BlockNaming.IdOf(blockName));
            }

            var occupancy = OccupiedCellsFor(anchor, blockName);
            var cells = occupancy.Cells;

            containers.Add(new Container
            {
                Id = BlockNaming.IdOf(blockName),
                Name = BlockNaming.DisplayNameOf(blockName),
                MaxMassKg = capacity,
                Density = ResolveId(ResolveDensity(anchor, block, blockName)),
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
                    + "identified.", anchor.RelativePath, BlockNaming.IdOf(blockName));
            }

            tanks.Add(new Tank
            {
                Id = BlockNaming.IdOf(blockName),
                Name = BlockNaming.DisplayNameOf(blockName),
                Resource = ResolveId(resource),
                MaxCapacity = group.Max(d => d.GetDouble("MaxCapacity") ?? 0),
                MaxDischargeRate = anchor.GetDouble("MaxDischargeRate"),
                Density = ResolveId(ResolveDensity(anchor, block, blockName)),
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
            var provenance = new Dictionary<string, Provenance>(StringComparer.Ordinal);

            if (geometry.SurfaceGravity is null)
            {
                provenance["surfaceGravity"] = Provenance.Unknown;

                Warn("unknownSurfaceGravity",
                    $"{SplitMilestoneSuffix(rawName).Name}: no GravitationalAcceleration on its "
                    + "gravity generator or anywhere in its inheritance chain, so the user must "
                    + "supply it.", info.RelativePath, id);
            }

            var atmosphere = geometry.Atmosphere;
            if (!geometry.Measured)
            {
                // Left unknown rather than filled in with a plausible default. Since the BaseGuid
                // walk landed, the only planet that reaches here is one not yet in the game, so a
                // fabricated atmosphere would be a guess about content that does not exist to be
                // checked against. Revisit when the planet ships (Backlog.md).
                provenance["atmosphere"] = Provenance.Unknown;
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
                    info.RelativePath, id);
            }

            if (geometry.GravityAffectDistance is null)
            {
                provenance["gravityAffectDistance"] = Provenance.Unknown;
            }

            if (geometry.RadiusMetres is null)
            {
                provenance["radiusMetres"] = Provenance.Unknown;
            }

            var planet = new Planet
            {
                Id = id,
                Name = displayName,
                Milestone = milestone,
                SurfaceGravity = geometry.SurfaceGravity,
                GravityAffectDistance = geometry.GravityAffectDistance,
                GravityAccelerationDistance = geometry.GravityAccelerationDistance,
                GravityFallOffPower = geometry.GravityFallOffPower,
                GravityShape = geometry.GravityShape,
                RadiusMetres = geometry.RadiusMetres,
                GroundOffsetInRadii = geometry.GroundOffsetInRadii,
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
            if (planet.ProvenanceOf("atmosphere") == Provenance.Unknown)
            {
                Warn("unknownAtmosphere",
                    $"{planet.Name}: no atmosphere geometry anywhere in its inheritance chain. "
                    + "Left unknown rather than assumed; the planet is not in the game yet.",
                    sources[planet.Id], planet.Id);
            }

            // Surprising enough to state outright, so nobody reads it as a bug in the app: the
            // planet has an atmosphere's full geometry and no air in it.
            if (planet.Atmosphere is { Density: <= 0.0 })
            {
                Warn("airlessAtmosphere",
                    $"{planet.Name}: its atmosphere generator states a density of 0, so there is no "
                    + "air at any altitude and atmospheric thrusters produce nothing there — "
                    + "despite the planet carrying a normal set of atmosphere distances.",
                    sources[planet.Id], planet.Id);
            }
        }

        return planets.Values.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
    }

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
        double? GravityAffectDistance, double? SurfaceGravity, Atmosphere? Atmosphere, bool Measured,
        double? GravityAccelerationDistance = null, double? GravityFallOffPower = null,
        string? GravityShape = null, double? RadiusMetres = null,
        double? GroundOffsetInRadii = null);

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
        double? surfaceGravity = null;
        double? affect = null;
        double? constant = null;
        double? density = null;
        double? radius = null;
        double? groundOffset = null;
        double? accelerationDistance = null;
        double? fallOffPower = null;
        string? shape = null;

        foreach (var component in PlanetComponents(prefab))
        {
            // A composite's component entry names the definition it uses rather than restating its
            // values, and the atmosphere splits across the two: the distances are set on the
            // component, the density on the definition. Resolving the reference is the only way to
            // reach the second half.
            density ??= AtmosphereDensityOf(component);

            if (radius is null && PlanetSizeOf(component) is var (metres, ground))
            {
                radius = metres;
                groundOffset = ground;
            }

            var type = component.TryGetProperty("$Type", out var t)
                ? t.GetString() ?? string.Empty
                : string.Empty;

            if (type.Contains("GravityGeneratorObjectBuilder", StringComparison.Ordinal))
            {
                gravity ??= Number(component, "AffectDistance");

                // Surface gravity in m/s², stated outright. An earlier draft of the research
                // concluded this was absent from the definitions and had to be supplied by the
                // user — that was wrong, and only looked true because this reader took
                // AffectDistance and ignored every other field on the same component.
                surfaceGravity ??= Number(component, "GravitationalAcceleration");

                // The rest of the falloff model, sitting on the same component and left unread
                // until the climb profile needed it (Roadmap v3).
                accelerationDistance ??= Number(component, "AccelerationDistance");
                fallOffPower ??= Number(component, "FallOffPower");

                shape ??= component.TryGetProperty("GravityShape", out var s)
                          && s.ValueKind == JsonValueKind.String
                    ? s.GetString()
                    : null;
            }
            else if (type.Contains("AtmosphereGeneratorObjectBuilder", StringComparison.Ordinal))
            {
                affect ??= Number(component, "AffectDistance");
                constant ??= Number(component, "ConstantAffectDistance");
            }
        }

        var atmosphere = affect is { } a && constant is { } c
            ? new Atmosphere
            {
                AffectDistance = a,
                ConstantAffectDistance = c,

                // Every generator but Palatine's resolves to 1.0; the fallback only covers a
                // planet whose generator reference we could not follow at all.
                Density = density ?? 1.0,
            }
            : null;

        return new PlanetGeometry(
            gravity, surfaceGravity, atmosphere, atmosphere is not null,
            accelerationDistance, fallOffPower, shape, radius, groundOffset);
    }

    /// <summary>
    /// A planet's radius in metres and the height of its ground above the reference sphere, from
    /// the voxel generator a component entry points at.
    /// </summary>
    /// <remarks>
    /// <b>The radius is in the game files after all</b> — two hops off the planet's own composition,
    /// which is why an earlier pass concluded it was not. The chain is
    /// <c>PlanetGeneratorDefinition -&gt; DetailCubemap -&gt; TargetPlanetRadius</c>: 60 000 m for
    /// planets, 20 000 m for moons.
    /// <para>
    /// <c>ZeroGround</c> comes back with it because the two are useless apart. It is the terrain's
    /// sea level as a fraction of the radius, so the ground is at <c>1 + ZeroGround</c> and not at
    /// <c>1</c> — 0.015 on Verdure, or 900 m. Ignoring it makes every altitude read 18 % low, which
    /// is exactly the error that made <c>TargetPlanetRadius</c> look like a rendering parameter
    /// rather than the answer (Research.md §5.3.1.1).
    /// </para>
    /// </remarks>
    private (double Metres, double GroundOffset)? PlanetSizeOf(JsonElement component)
    {
        if (!component.TryGetProperty("Definition", out var reference)
            || reference.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var generator = _definitions.Resolve(reference.GetString());
        if (generator?.TypeName != "PlanetGeneratorDefinitionObjectBuilder") return null;

        var cubemap = _definitions.Resolve(Dereference(generator.GetString("DetailCubemap")));
        if (cubemap?.GetDouble("TargetPlanetRadius") is not { } metres || metres <= 0) return null;

        // Absent on some planets, and zero is the right reading: their terrain sits on the
        // reference sphere.
        return (metres, InheritedDouble(generator, "ZeroGround") ?? 0.0);
    }

    /// <summary>Strips the <c>{G}</c> prefix the content uses on some GUID references.</summary>
    private static string? Dereference(string? reference) =>
        reference is { Length: > 0 } && reference.StartsWith("{G}", StringComparison.Ordinal)
            ? reference[3..]
            : reference;

    /// <summary>
    /// The <c>Density</c> of the atmosphere generator a component entry points at, if it points at
    /// one.
    /// </summary>
    /// <remarks>
    /// Identifies the generator by the <em>resolved definition's</em> type rather than by the type
    /// named on the component entry, because the entry frequently does not name one: Verdure's
    /// composite overrides the atmosphere with a bare
    /// <c>{ "Kind": "Update", "Index": 14, "Value": { "Definition": "…" } }</c>, where the type is
    /// carried only by the base composite's slot at that index. Matching on what the GUID resolves
    /// to sidesteps having to replay the engine's delta indices.
    /// </remarks>
    private double? AtmosphereDensityOf(JsonElement component)
    {
        if (!component.TryGetProperty("Definition", out var reference)
            || reference.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var definition = _definitions.Resolve(reference.GetString());
        return definition?.TypeName == "AtmosphereGeneratorDefinitionObjectBuilder"
            ? InheritedDouble(definition, "Density")
            : null;
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

    /// <summary>
    /// Inline component payloads, in either encoding the game uses.
    /// </summary>
    /// <remarks>
    /// A container is either delta-encoded — an object with a <c>Changed</c> array — or a plain
    /// array of the same entries. <b>Both occur, and nothing marks which to expect.</b> The legacy
    /// planet templates use the plain form, so a reader that handled only the delta form walked
    /// straight past <c>PlanetWithoutAtmosphere</c>'s gravity generator and reported that Verdure
    /// and Kemik state no surface gravity — which was then written up as "gravity is not in the
    /// game files at all". It is; we were not looking in the second shape.
    /// <para>
    /// <see cref="BlockCompositionIndex.ReadComponentGuids"/> already handled both, for exactly
    /// this reason. This is the same lesson, learned twice.
    /// </para>
    /// </remarks>
    private static IEnumerable<JsonElement> ChangedValues(JsonElement container)
    {
        var entries = container.ValueKind switch
        {
            JsonValueKind.Object when container.TryGetProperty("Changed", out var changed)
                                      && changed.ValueKind == JsonValueKind.Array => changed,
            JsonValueKind.Array => container,
            _ => default,
        };

        if (entries.ValueKind != JsonValueKind.Array) yield break;

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) continue;

            // Delta entries wrap the payload in Value; a plain array may hold it directly.
            if (entry.TryGetProperty("Value", out var value)
                && value.ValueKind == JsonValueKind.Object)
            {
                yield return value;
            }
            else if (entry.TryGetProperty("$Type", out _))
            {
                yield return entry;
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

    /// <summary>Records a warning, optionally against the entity it concerns.</summary>
    private void Warn(string code, string detail, string? file, string? subject = null) =>
        _warnings.Add(new ExtractionWarning
        {
            Code = code,
            Detail = detail,
            File = file,
            Subject = subject,
        });
}
