# ThrusterCalculator SE2 — Research

Research date: **2026-08-04**. Game build observed: SE2 alpha, definition bundles stamped
`2.0.1.x` – `2.3.0.2798` (see §2.3 on why the stamp varies per file).

Goal: a standalone local app that calculates thruster requirements for Space Engineers 2 ships,
reading **live data from the installed game** rather than hardcoded tables — because the game is
in alpha and the numbers move every patch.

---

## 1. Constraint that shapes everything: no in-game scripting

SE2 has **no programmable block and no scripting/code API**. Keen's position is that "scripting and
code APIs are not included yet in Space Engineers 2 Alpha, they'll be released later." The published
roadmap (VS2.2 Survival Extensions Q1/Q2 2026, VS3 Water, VS4 NPCs & Co-op MP) does not commit
programmable blocks to a version.

Consequences:

- The SE1 pattern (a PB script reading the live grid) is **not available**. This must be an external
  desktop app.
- We cannot query a *running* game. Everything comes from **files on disk**.
- Modding exists (Alpha VS1.5) and there's a separate `Space Engineers 2 - Mod SDK` depot.
  **We deliberately do not depend on it** — the app must work with only the base game installed (§7).

---

## 2. The definition data

### 2.1 `.def` files are plain JSON

`GameData\Vanilla\Content\` contains **17,172 `.def` files**, and they are *plain, readable JSON*.
Much better than SE1's `.sbc` XML or SE2's binary `.vrb` saves.

Real example — `Blocks\Thrusters\Atmospheric\100\AtmosphericThruster100_ThrusterDefinition.def`:

```json
{
  "$Bundles": { "Game2": "2.3.0.2722", "System.Runtime": "1.0.0.0", "VRage": "2.3.0.2722" },
  "$Type": "Game2:Keen.Game2.Simulation.WorldObjects.CubeBlocks.Movement.ThrusterDefinitionObjectBuilder",
  "$Value": {
    "Guid": "b4c0770f-75e1-4be6-a426-5fce05a8875e",
    "ThrustPower": 40000,
    "ThrustClass": "Atmospheric",
    "ResourcesRequiredToThrust": 75
  }
}
```

Consistent envelope everywhere:

| Field | Meaning |
|---|---|
| `$Bundles` | Assembly-version stamps the file's schema was authored against |
| `$Type` | `bundle:FullyQualifiedClrTypeName` of the object builder |
| `$Value` | Payload, always carrying a `Guid` identity |

Same envelope as blueprint `.container-info` files (confirmed in `BlueprintHelperSE2`), so it's the
engine-wide serialization shape.

### 2.2 It is a GUID-keyed graph, not a file hierarchy

Nothing references anything by name or path — everything by GUID:

```json
"Density":          "d8adcfdc-f8e2-467e-9d27-78deae4057da",
"Recipe":           "136bb272-8077-4277-9da9-d0a1d8073cb9",
"BlockKind":        "93e882df-b11a-4379-97ec-4176d195480f",
"ConsumedResource": { "Type": "bcded093-f5c0-4997-af3a-a6fbd853ad66", "Amount": 0 }
```

**The first thing to build is a GUID → definition index** over the whole `Content` tree. Every
subsequent question is a graph walk. Folder layout is a human convenience, not the data model.

`ConsumedResource.Type` above resolves to `System\ResourceTypes\Electricity.def`:

```json
{ "Guid": "bcded093-...", "Name": "ResourceElectricity",
  "FlowRateUnits": "Kilowatts", "StorageUnits": "KilowattHours", "RequiresConveyors": false }
```

Only four resource types ship: `Electricity`, `Hydrogen`, `Oxygen`, `Water`.

### 2.3 Per-file version stamps vary — useful for staleness detection

Observed `$Bundles.Game2` in the *same* install: `2.0.1.1811`, `2.0.1.4905`, `2.0.1.5005`,
`2.0.1.6909`, `2.2.0.108`, `2.2.0.540`, `2.3.0.936`, `2.3.0.971`, `2.3.0.1613`, `2.3.0.2099`,
`2.3.0.2722`, `2.3.0.2798`. Keen stamps each file with whichever build last touched it — a **per-file
authoring version**, not the game version. Take the max as a rough build indicator; use file metadata
hashing for actual staleness detection (Technic §4.2).

### 2.4 Delta encoding is real — and we mostly dodge it

Prefab/composition definitions are delta-encoded against a parent:

```json
"ObjectBuilders": {
  "$DeltaEncoded": true,
  "Keys": [ "b7bf405c-...", ... ],
  "Changed": [ { "Kind": "Update", "Index": 3, "Value": { ... } } ],
  "Removed": [ "832efb8e-..." ]
}
```

Resolving these properly means reimplementing engine inheritance semantics.

**The good news: the thruster numbers are not delta-encoded.** `_ThrusterDefinition.def` and
`_PowerableBlockDefinition.def` are flat, complete documents.

**The bad news: planet data *is* delta-encoded** (§5). So we can't dodge it entirely if we want
gravity/atmosphere from the game. Fortunately the payloads we need are inline in the `Changed`
array (`Value` objects carry their own `$Type` and fields), so a **shallow** delta reader — read the
`Changed` entries, ignore inheritance — extracts them without a full engine reimplementation. That's
a pragmatic middle path, and it's how §5's numbers below were obtained.

Base templates in `Content\Templates\Blocks\BaseDefinitions\` (e.g.
`ThrustersPowerableBlockDefinition.def`, carrying `PCU: 150`) mean some fields are inherited rather
than restated. The atmospheric thruster's own powerable def omits `PCU` while the template has it —
so **template fallback is required**, at least for fields we care about.

---

## 3. Thruster data — the complete table

Every `*ThrusterDefinition.def` under `Content\Blocks\Thrusters\`, all 12 of them:

| Block | ThrustClass | ThrustPower (N) | ResourcesRequiredToThrust |
|---|---|---:|---:|
| AtmosphericThruster100 | Atmospheric | 40 000 | 75 |
| AtmosphericThruster250 | Atmospheric | 287 136.3 | 650 |
| AtmosphericThruster500 | Atmospheric | 1 516 383 | 2 400 |
| AtmosphericThruster1000 | Atmospheric | 15 465 370 | 16 000 |
| IonThruster100 | Ion | 8 950.306 | 40 |
| IonThruster150 | Ion | 82 492.88 | 240 |
| IonThruster500 | Ion | 856 368.56 | 1 800 |
| IonThruster750 | Ion | 5 636 987 | 8 000 |
| HydrogenThruster50 | *(absent)* | 60 000 | 0.75 |
| HydrogenThruster200 | *(absent)* | 359 468.8 | 4 |
| HydrogenThruster250 | *(absent)* | 1 895 631 | 12 |
| HydrogenThruster750 | *(absent)* | 19 395 660 | 120 |

Notes:

- **The trailing numeral is block size in centimetres** (100 = 1 m, 1000 = 10 m), matching SE2's
  variable-size grid. Not a tier index. **True for thrusters, containers and tanks — everything the
  app lists — but not universally:** the game shows `Drill500` as 5.25 m and `GatlingTurret600` as
  5.5 m, so treat the numeral as an identifier rather than a promise (Backlog B2).
- **Hydrogen omits `ThrustClass` entirely**, and its files are named `*_HydrogenThrusterDefinition.def`
  — but the `$Type` is the same `ThrusterDefinitionObjectBuilder`. **The parser must key off `$Type`,
  never filename**, and treat missing `ThrustClass` as valid.
- **There are 14 `ThrusterDefinitionObjectBuilder` definitions, not 12.** The table above was built by
  globbing `Content\Blocks\Thrusters\`, which misses two **base templates** in
  `Content\Templates\Blocks\BaseDefinitions\`. Found by `tc dump-schemas`; see §3.4 — they resolve an
  open question rather than being noise.
- **`ResourcesRequiredToThrust` is not comparable across classes.** Atmospheric/Ion are Electricity
  (kW). Hydrogen's `0.75 … 120` against 60 kN … 19 MN is clearly a hydrogen flow rate in other units.
  Resolve each thruster's `ConsumedResource` GUID individually before computing any efficiency metric.
- **Underwater thrusters: art only, no data.** `Blocks\Thrusters\Underwater\{50,150,250,750}\` has
  models and materials but **zero `.def` files** — consistent with water being the unshipped VS3
  milestone (confirmed: water is a future update). Handle "folder exists, no definition" gracefully
  and surface it as "not implemented in this build."
- Thrust does not follow a clean power law against size (thrust ÷ size³ for atmospheric:
  0.040 / 0.018 / 0.012 / 0.015 — non-monotonic). **Treat the table as data, never interpolate.**

### 3.1 Cross-check against the community wiki — and why it validates the whole premise

[spaceengineers2.wiki.gg/wiki/Thruster_comparison](https://spaceengineers2.wiki.gg/wiki/Thruster_comparison)
publishes a thruster table. Comparing it against the game files above is *extremely* instructive:

| Block | Game `ThrustPower` | Wiki thrust | Verdict |
|---|---:|---:|---|
| Atmo 250 / 500 / 1000 | 287 136 / 1 516 383 / 15 465 370 | identical | ✅ match |
| **Atmo 100** | **40 000** | **16 273** | ❌ **wiki stale** (also 75 kW vs wiki 50 kW) |
| Hydrogen 50 / 200 / 250 / 750 | 60 000 / 359 469 / 1 895 631 / 19 395 660 | identical | ✅ match |
| **Ion 100** | **8 950** | 16 270 | ❌ mismatch |
| **Ion 150** | **82 493** | 287 130 | ❌ mismatch |
| **Ion 500** | **856 369** | 1 516 380 | ❌ mismatch |
| **Ion 750** | **5 636 987** | 15 465 370 | ❌ mismatch |

Two distinct failures, and both are worth understanding:

1. **Atmo 100 is genuinely stale** — the game was retuned (16 273 N → 40 000 N, 50 kW → 75 kW) and
   the wiki hasn't caught up.
2. **The entire ion thrust column is a copy-paste of the atmospheric column.** Wiki ion values
   (16 270 / 287 130 / 1 516 380 / 15 465 370) are the atmospheric values to within rounding. The
   *power* column is correct (40/240/1800/8000 all match the game). Real ion thrust is roughly
   **3.5–5× lower** than the wiki claims.

**This is the strongest possible argument for the app's core premise.** A player sizing an ion-thruster
ship off the wiki would under-build by a factor of ~4 and their ship would not fly. Reading the game
files is not a nice-to-have.

**Use the wiki as a cross-check oracle, never as a source.** It was used exactly that way here, by
hand, once — and it earned its keep, since its mass column is what the formula was verified against
(§4.0).

A `compare` CLI command diffing against a checked-in wiki snapshot was proposed and **rejected**: the
snapshot is hand-maintained, slow to update, and demonstrably error-prone, so it would generate
diffs that mostly mean "the wiki is stale again" (Technic §11). `tc verify` checks invariants against
the game instead, which is the check that can actually fail usefully.

### 3.2 The wiki *does* give us something the game files don't: mass

The wiki's mass column has no counterpart in the definition files (§4), so it can't be cross-checked
— but it's the only mass data we have, and mass is now a v1 blocker (Design: thruster self-weight).

| Size | Atmospheric | Ion | Hydrogen |
|---|---:|---:|---:|
| 0.5 m | — | — | 33 kg |
| 1 m | 58 kg | 58 kg | — |
| 1.5 m | — | 290 kg | — |
| 2 m | — | — | 464 kg |
| 2.5 m | 464 kg | — | 1 005 kg |
| 5 m | 1 552 kg | 1 576 kg | — |
| 7.5 m | — | 6 188 kg | 7 096 kg |
| 10 m | 8 343 kg | — | — |

**Treat every figure here as `Assumed` provenance.** Given §3.1 showed an entire wiki column was
copy-pasted wrong, and that Atmo 2.5 m and Hydrogen 2 m share a suspiciously identical 464 kg, these
need in-game verification before being trusted. They are a starting point and a sanity check on any
mass curve we derive, not ground truth.

### 3.3 Environmental effectiveness — **found, and fully in data**

A grep across `Blocks\Thrusters\` finds no effectiveness field, which initially looked like a dead
end. It isn't: the model is **global, keyed by thrust class**, in
`Content\System\Configurations\ThrustClassesConfiguration.def`:

```json
"$Type": "Game2:Keen.Game2.Simulation.GameSystems.Movement.ThrustClassesConfigurationObjectBuilder",
"ThrustClasses": [
  { "$Key": "Ion",
    "$Value": { "MaxThrustAirDensity": 0.2, "MinThrustAirDensity":  0.8,
                "WaterSubmersionTolerance": 1, "WaterOnly": false } },
  { "$Key": "Atmospheric",
    "$Value": { "MaxThrustAirDensity": 0.8, "MinThrustAirDensity":  0.2,
                "WaterSubmersionTolerance": 1, "WaterOnly": false } },
  { "$Key": "Hydrogen",
    "$Value": { "MaxThrustAirDensity": 0,   "MinThrustAirDensity": -1,
                "WaterSubmersionTolerance": 1, "WaterOnly": false } },
  { "$Key": "Water",
    "$Value": { "MaxThrustAirDensity": 0,   "MinThrustAirDensity": -1,
                "WaterSubmersionTolerance": 1, "WaterOnly": true  } }
]
```

**Note the `$Key` / `$Value` pair shape** — an earlier draft of this section flattened the fields up
onto the entry, which is not what the file says. A parser written from the flattened version reads
zero thrust classes and every thruster silently loses its environmental falloff.

This is the SE1 `MinPlanetaryInfluence` / `EffectivenessAtMinInfluence` analogue, and it is **exactly
the missing piece**. Reading it:

- **Atmospheric** — full thrust at air density **≥ 0.8**, ramping to zero at **≤ 0.2**. Dead in vacuum.
- **Ion** — inverted: full thrust at density **≤ 0.2**, ramping to zero at **≥ 0.8**. Note `Max` is
  attached to the *low* density; the field names describe "the density at which max thrust occurs,"
  not an ordering. **Do not assume `Min < Max`** when parsing.
- **Hydrogen** — `Min = -1`, `Max = 0`: constant thrust everywhere, confirming hydrogen as the
  environment-agnostic option. **This is not a sentinel, though it reads like one** and was
  implemented as one here for a while. The engine has no `min < 0` branch; it runs the ordinary
  ramp, `clamp((d − (−1)) / (0 − (−1)), 0, 1)` = `clamp(d + 1, 0, 1)`, which is 1 for every air
  density a planet can have. The endpoints simply sit below the physical range. Same answer, and a
  rule that would have been wrong for any class with `Min < 0` and `Max` inside `[0, 1]`.
- **Water** — `WaterOnly: true`. A fourth class **already exists in config** even though no underwater
  thruster ships a definition (§3). Direct confirmation that underwater thrusters are staged for the
  water milestone.

Combined with §5.2's per-planet atmosphere geometry, this closes the loop: planet gives air density
as a function of altitude, this config maps air density to a thrust multiplier per class. **The whole
environmental model is readable JSON — no engine code required.**

Between the two ramp points the interpolation is **linear** — originally assumed here from SE1
behaviour, since **confirmed** against `GridMovementCollectorComponent.GetThrustEfficiency`:

```csharp
if (max >= min) return Math.Clamp((d - min) / (max - min), 0f, 1f);
return 1f - Math.Clamp((d - max) / (min - max), 0f, 1f);
```

The two branches are the ordering rule above made explicit, and are algebraically the same as
interpolating the signed interval. Total grid thrust is then `Σ (class thrust × efficiency)`.

One correction the caveat above did *not* anticipate: the air density fed in is not purely
geometric. See §5.2.1.

The wiki's "Space = 0 / Atmosphere = 1" annotations are a lossy summary of this table — another
reason to treat the wiki as a cross-check, not a source (§3.1).

### 3.4 The base templates — and the answer to hydrogen's missing class

`Content\Templates\Blocks\BaseDefinitions\` holds two more thruster definitions, which concrete
blocks inherit from:

```jsonc
// HydrogenThrusterDefinition.def
{ "ThrustPower": 0,     "ThrustDirection": "Forward", "ThrustClass": "Hydrogen" }
// IonThrusterDefinition.def
{ "ThrustPower": 10000, "ThrustDirection": "Forward", "ThrustClass": "Ion" }
```

Three things follow, all previously open:

1. **Hydrogen thrusters are `ThrustClass: "Hydrogen"`** — they omit it in their own definition and
   inherit it from this template. That closes the question of what the engine defaults to; it is
   *measured*, not inferred. `ThrustClassesConfiguration` already defines a `Hydrogen` entry
   (§3.3), and this is what points at it.
2. **Template inheritance is real and must be implemented.** An early draft guessed it was a
   one-level fallback matched by shape; it is neither. The chain is explicit and arbitrarily deep,
   pointed to by `BaseGuid` — see §4.4.1, which is the section to read before touching any of this.
3. **`ThrustDirection` does exist in the data**, on the templates, as `"Forward"`.

Templates must be **excluded from the block catalogue**: `HydrogenThrusterDefinition` has
`ThrustPower: 0`, so counting it as a real thruster both inflates the count and looks like a thruster
that produces nothing. They are identified by living under `Templates/` — the one place in this data
where path carries meaning.

### 3.5 How a block's definitions are joined

A block is not one definition. A thruster's thrust lives in `*_ThrusterDefinition.def`, while its
density, PCU and name live in `*_PowerableBlockDefinition.def` — and **neither file names the other**.

The join is the block's **`EntityCompositeDefinitionObjectBuilder`** (`*_ServerComposition.def` /
`*_ClientComposition.def`), which lists the component definitions making up the entity:

```jsonc
"Components": {
  "$DeltaEncoded": true,
  "Keys": [ … ],                       // component type slots
  "Changed": [
    { "Kind": "Update", "Index": 4, "Value": { "Definition": "00516d6b-…" } },  // PowerableBlock
    { "Kind": "Update", "Index": 5, "Value": { "Definition": "b4c0770f-…" } }   // Thruster
  ]
}
```

This is the engine's own mechanism for deciding which components form an entity, so it is as durable
as the data format itself — **not** a heuristic about folder layout.

**Verified against the shipped data:** all 14 thruster definitions resolve to exactly one
`PowerableBlockDefinitionObjectBuilder` each, via 2 composites apiece (client + server). Matching by
shared directory was considered and rejected: it works on today's layout but would mispair if two
blocks ever shared a folder, and — worse — a fallback silently standing in for the real join would
hide exactly the breakage worth knowing about.

Note the GUIDs sit **inline** in the `Changed` array, so shallow delta decoding (§2.4) suffices.

**Caution for projection:** `UIData.Name` is *not* a per-block display name. All four atmospheric
thrusters report `ThrusterAtmo` and all four ions report `ThrusterIon`; hydrogen blocks inherit
`ThrusterHydro` from their template. It is a family key. Display names must be synthesised by the
producer (Schema.md §8).

---

## 4. Mass — not a stored value

No `Mass` field on a block. `PowerableBlockDefinition.Density` points at one of four shared
definitions in `Content\Blocks\Shared\Density\`:

| Definition | `MassCurveModifier` |
|---|---:|
| Hollow | 7 |
| Mostly Hollow | 11 |
| Mostly Solid | 20 |
| Solid | 35 |

Thrusters are `Mostly Hollow` (11).

So `mass = f(blockSize, MassCurveModifier)` where `f` is **not in the definition data** — a full-tree
grep for `MassCurve` finds only these four files. The curve lives in engine code.

Sanity check against §3.2's wiki masses: Atmo 1 m = 58 kg, Atmo 2.5 m = 464 kg. Volume ratio is
15.6×, mass ratio is 8.0×. So mass is **sub-linear in volume** — not a simple `density × volume`.
Consistent with "curve," and confirms we can't guess it.

One global config exists — `Content\System\Configurations\CubeBlockMassConfiguration.def` — but it
carries only `MinBlockMass: 5`. A floor, not the curve.

**This is a v1 blocker**, because the calculator must add proposed thrusters' own weight
(Design §4.2), and the container/tank mass path needs block masses too.

### 4.0 SOLVED — the formula, decompiled and verified

```csharp
public void ComputeMassAndHP()
{
    int num = 0;
    foreach (var group in OccupiedGridCellsGroups)
        num += group.GetSizeIncludingMax().Volume();

    if (Density == null)
        Mass = MassConfiguration.MinBlockMass;
    else
        Mass = (float)(Density.MassCurveModifier * Math.Sqrt(num) * Math.Log10(num)
                       + MassConfiguration.MinBlockMass);

    MaxHealth = (Fragility?.MaxHPMassMultiplier ?? 1f) * Mass;
}
```

Decompiled from `Game2.Simulation.dll`,
`Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockDefinition` (build path in the assembly
confirms `Stable_VS2.3`). So:

> **`mass = MassCurveModifier × √V × log₁₀(V) + MinBlockMass`**
>
> where **V** = total occupied grid-cell count, `MassCurveModifier` ∈ {7, 11, 20, 35} (§4), and
> `MinBlockMass` = 5 (`CubeBlockMassConfiguration.def`).

Two notes on edge behaviour: a block with no `Density` gets exactly `MinBlockMass`, and since
`log₁₀(1) = 0`, a single-cell block also lands on exactly `MinBlockMass` — which is what the constant
is *for*, not an arbitrary floor.

**Verification.** Solving the formula for `V` against all twelve wiki masses (§3.2), using the
thrusters' `Mostly Hollow` modifier of 11, recovers **exact integers in every case** — round-tripping
to within 0.2 kg:

| Block | Known kg | V (solved) | V | mass(V) |
|---|---:|---:|---:|---:|
| HydrogenThruster50 | 33 | 8.0 | **8** | 33.1 |
| AtmosphericThruster100 | 58 | 16.0 | **16** | 58.0 |
| IonThruster100 | 58 | 16.0 | **16** | 58.0 |
| IonThruster150 | 290 | 144.1 | **144** | 289.9 |
| AtmosphericThruster250 | 464 | 287.9 | **288** | 464.1 |
| HydrogenThruster200 | 464 | 287.9 | **288** | 464.1 |
| HydrogenThruster250 | 1 005 | 936.1 | **936** | 1 004.9 |
| AtmosphericThruster500 | 1 552 | 1 852.3 | **1 852** | 1 551.8 |
| IonThruster500 | 1 576 | 1 897.9 | **1 898** | 1 576.0 |
| IonThruster750 | 6 188 | 17 540.9 | **17 541** | 6 188.0 |
| HydrogenThruster750 | 7 096 | 22 031.4 | **22 031** | 7 095.9 |
| AtmosphericThruster1000 | 8 343 | 28 877.5 | **28 878** | 8 343.1 |

Twelve independent integer hits is not coincidence. The formula is right, and — worth noting — the
wiki's **mass** column is accurate even though its **thrust** column was badly wrong (§3.1). Different
contributors, different reliability; judge columns, not sources.

### 4.0.0 SOLVED — V comes straight from the game's own cache

**`contentcache.vrb` can be read, and it contains the game's precomputed block occupancy.** Verified
by spike against SE2 2.3.0.2798, reusing `../BlueprintHelperSE2`'s working `.vrb` stack
(`Se2Runtime` + `VrbSerializer` + `ContentCache`):

```
blob type  Keen.Game2.Simulation.GameSystems.BlockDataGenerators.GeneratedBlockData  →  1,454 entries

AtmosphericThruster250   min=(-2,-2,-3) max=(3,3,4)   size=6x6x8   V=288   MATCH
CargoContainer150        min=(-2,-2,-2) max=(3,3,3)   size=6x6x6   V=216   MATCH
```

Both agree **exactly** with the values recovered by solving the mass formula backwards (§4.0). That
is a genuine independent confirmation from two unrelated directions — the formula transcription and
every recovered `V` are correct — and it settles the shape questions too: the 2.5 m thruster really
is elongated (6×6×8), the 1.5 m container really is a cube (6³).

Consequences:

- **The hand-maintained cell-count table stops being the source.** The cache covers 1,454 blocks
  against our 16, so containers, tanks and everything else are included. The table is *kept*, as a
  fallback for runs without the engine and as an independent cross-check — which is what caught the
  bug in the next bullet (Backlog B13).
- **`V` is the sum of `Occupancy.CellGroups`, *not* the volume of `Occupancy.Bounds`.** The first
  implementation read the bounding box, which is correct only for blocks that are a single box. The
  5 m hydrogen tank occupies **1,820** cells inside a 20×10×10 = **2,000** box — a 10% mass
  overstatement. `ComputeMassAndHP` sums the groups, so we must too. Caught purely because the
  recovered table disagreed; with one source it would have shipped silently.
- It requires hosting the game's assemblies, so it belongs in the quarantined producer-side
  `Engine` project that Technic §2.3 had reserved and left uncreated. **That reservation has since
  been taken up** — `ThrusterCalculator.Engine` exists and `tc extract` reports
  `Occupancy source: content-cache`.

**It does not solve density.** The cache holds *generated* data, not merged definitions, so §4.4's
container-density question stands unchanged.

### 4.0.1 Where V comes from in the source data

`OccupiedGridCellsGroups` is marked `SerializerFormatSet.None`: computed, never serialized. It's set
by `SetOccupancy(BlockOccupancyData)`, produced by
`Keen.Game2.Simulation.GameSystems.BlockDataGenerators.BlockOccupancyGenerator`:

```csharp
public class BlockOccupancyGenerator {
    public const float CELL_SIZE       = 0.25f;    // 25 cm voxel grid
    public const float MAX_BLOCK_VOLUME = 8000f;
    public const int   MAX_BLOCK_CELLS  = 512000;

    public BlockOccupancyData Generate(BufferReference<IPhysicsCollider> colliders) { … }
    // voxelizes the block's physics colliders, then GeneratedBlockDataHelpers.BuildGroups(cells)
}
```

So **V is derived by voxelizing the block's physics colliders at 25 cm** — it depends on collision
mesh geometry, not on any definition field. `CELL_SIZE = 0.25` corroborates the recovered numbers: a
50 cm hydrogen thruster gives V = 8 = 2×2×2 cells exactly, and the 150 cm ion thruster's V = 144
factors as 6×6×4 = 1.5 m × 1.5 m × 1 m. Larger thrusters give V well below their bounding volume,
consistent with tapered nozzle colliders.

Generated block data is cached in `Content\contentcache.vrb` (26 MB) — binary, so reading it directly
means the `.vrb` path (§5.4).

**An earlier draft concluded "we don't need to", and that was wrong** — see §4.0.0, which does read
it. Solving the formula backwards works but only reaches blocks whose in-game mass someone has
measured by hand; the cache covers all 1,454. What survives from the original reasoning is the
storage decision: the per-block integer `V` goes into the extracted config, so the *consumer* never
touches the cache. And because `MassCurveModifier` and `MinBlockMass` still come from `.def`,
**retuning tracks automatically** — only a change to a block's collision mesh invalidates `V`, which
is rare and shows up immediately as a mismatch against in-game mass.

### 4.1 Assembly-level details (how the above was obtained)

The shipped assemblies are managed C# and **not obfuscated**. Inspecting `Game2.Simulation.dll` via
a `MetadataLoadContext` (metadata only, no code executed) gives the exact shape:

```
Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockDefinition
    [prop]   Single                     Mass                ← computed, never serialized
    [prop]   CubeBlockMassConfiguration MassConfiguration   ← supplies MinBlockMass = 5
    [prop]   BlockSizeDefinition        RelativeBlockSize
    [method] void                       ComputeMassAndHP()  ← instance, void

Keen.Game2.Simulation.WorldObjects.CubeBlocks.CubeBlockDensityDefinition
    [prop]   Single                     MassCurveModifier   ← 7 / 11 / 20 / 35
```

`VRage.Voxels.dll` likewise contains `SurfaceGravity`, so planet gravity (§5.3) is reachable the same
way.

Two consequences:

1. **`Mass` has no backing `.def` field** — it's produced by `ComputeMassAndHP()`. That confirms the
   grep result above wasn't a search failure; the value genuinely isn't in the data.
2. **`ComputeMassAndHP()` is an instance method returning `void`**, not a pure
   `ComputeMass(size, modifier)`. Calling it requires a fully constructed `CubeBlockDefinition`,
   which means standing up the engine's definition-loading pipeline.

The inputs are few and all visible: `RelativeBlockSize`, `MassCurveModifier`, `MinBlockMass`. That
smallness is what makes a **transcribe-the-formula** approach attractive as a lighter alternative to
full engine hosting — see Technic §10 for the three-way comparison and recommendation.

### 4.2 Resolution

No tiering, no engine hosting, no empirical curve fitting. The formula is exact and its inputs are
either in `.def` (`MassCurveModifier`, `MinBlockMass` — auto-tracking) or a small table of per-block
integers (`V`) recovered exactly in §4.0.

Block mass is `Derived` provenance: computed by our own transcribed formula from `Measured` inputs
plus a recovered `V`. Fully testable, no runtime dependency, works on a machine with no game
installed. See Technic §5.5 and §10.

### 4.3 Cargo and tank capacity — no curve needed

Good news for the "describe your loadout" mass path (Design §3): capacities are stored directly.

**Cargo containers** — `*_InventoryDefinition.def` carries `MaxMass` in kg:

| Block | `MaxMass` |
|---|---:|
| CargoContainer150 | 16 800 kg |
| CargoContainer250 | 67 200 kg |
| CargoContainer750 | 2 150 400 kg |

So "half-full cargo" is directly computable — no mass curve involved. (The 750's 2 150 400 kg is a
32× jump over the 250; verify in-game that it isn't a placeholder.)

**Tanks** — `*_TanksResourceContainer.def` (`ResourceContainerDefinitionObjectBuilder`) carries
`MaxCapacity`, plus charge/discharge rates and, on oxygen tanks, an explicit `ResourceType` GUID:

| Block | `MaxCapacity` | `MaxDischargeRate` |
|---|---:|---:|
| HydrogenTank150 | 8 000 | 2 000 |
| HydrogenTank500 | 32 000 | 4 000 |
| HydrogenTank1250 | 1 280 000 | 100 000 |
| OxygenTank150 | 8 000 | 2 000 |

Units come from the referenced resource type (`Hydrogen.def` etc.), so resolve the GUID rather than
assuming litres or kg. **Gas is massless** — measured in game by watching a tank fill with the ship
mass unchanged (Backlog B3) — so a full tank weighs exactly what an empty one does and capacity never
needs converting to kilograms.

Note tank *block* mass still needs the mass curve; only the *contents* are free.

### 4.4 Recovered cell counts for tanks — and where containers stall

Applying §4.0's method to published block masses:

| Block | Density | V | Mass reproduced |
|---|---|---:|---:|
| HydrogenTank150 | Mostly Hollow (11) | 216 | 382.40 kg |
| HydrogenTank500 | Mostly Hollow (11) | 1 820 | 1 534.87 kg |
| HydrogenTank1250 | Mostly Hollow (11) | 36 244 | 9 552.79 kg |

Tighter evidence than the thrusters: those reference masses are published to two decimals and each
`V` reproduces its mass to that precision. `V = 216 = 6³` for the 1.5 m tank is exactly a full cube
of 25 cm cells, which independently corroborates the cell size.

**Cargo containers stall on density, not on V.** Their base definition
`Templates/Blocks/BaseDefinitions/CargoContainersFunctionalBlockDefinition.def` gives
`Density = 3dca2cf9` — **Hollow (7)** — and with that modifier CargoContainer150's published
245.17 kg solves to `V = 216`, the same 6³ cube as the tank. But at the time the extractor could not
*reach* that template: it is a **standalone base definition with no template composite**, and the
slot-signature inheritance then in use could only match templates that had one.

That dead end is what forced the search for a real parent pointer — and §4.4.1 found it.

### 4.4.1 SOLVED — the parent pointer lives in `definitionsets.vrb`

**Definitions carry an explicit `BaseGuid`, and it is not in the `.def` files.** It lives in
`definitionsets.vrb`, in `DefinitionLoadingData`, alongside `IsAbstract`, `PartialDefinitions` and
`PriorityOverrides` — a full inheritance system that no amount of JSON inspection could reveal.

Verified on SE2 2.3.0.2798:

```
CargoContainer150_...FunctionalBlockDefinition   own Density (none)
  base[1]  1f272188  CargoContainersFunctionalBlockDefinition  Density = Hollow (7)   ← abstract
  base[2]  ea3505ef  FunctionalBlockDefinition                                        ← abstract
```

9,142 of 17,196 definitions declare a base, so this is pervasive rather than an edge case.
Resolution is simply: read the field; if absent, follow `BaseGuid` and repeat.

**Independently confirmed by in-game measurement.** Measured block masses (cockpit-only baseline
subtracted) against Hollow (7) and the cache's cell counts:

| Container | Measured | Predicted | |
|---|---:|---:|---|
| 1.5 m | 245 kg | 245.17 | ✅ |
| 2.5 m | 669 kg | 669.08 | ✅ |
| 7.5 m | 4 982 kg | 5 092.09 | ❌ 2.2% out |

Two exact matches settle the density. The 7.5 m gap is **not** a density error — the same modifier
is right for its siblings — but a cell-count one: the cache reports 26,912 cells where the measured
mass implies ~25,946. Deferred with the evidence and leading hypothesis in Backlog B2.

#### Two rules that were tried first, and were wrong

Both inferred the parent from component-slot signatures, because the real pointer had not been found:

| Rule | Outcome |
|---|---|
| Slot containment (template ⊆ block) | Thrusters and tanks resolved; containers unresolved and warned |
| Overlap ≥ 75%, best match wins | Containers resolved — **to the wrong template**. Tanks silently became Mostly Solid (20) instead of Mostly Hollow (11), breaking three masses that had matched exactly. **Zero warnings**, because the matcher believed it had succeeded |

The second is the instructive one: the only case in this project where a heuristic produced
confident wrong numbers *and* suppressed the warning that would have exposed them. It was caught
only by re-running the known-mass regression check. Both are now deleted in favour of `BaseGuid`.

### 4.4.2 The same pointer fixes planet atmospheres

Planet composites are delta-encoded against a parent too, so the `BaseGuid` walk applies there
unchanged. Applying it took **assumed atmospheres from 8 planets down to 1**.

Verified against what each planet's own definition states:

| Planet | Own definition | Extracted | |
|---|---|---|---|
| EarthLike | const 1.08, edge 1.15 | same | ✅ |
| Verdure | const 1, edge 1.15 | same | ✅ |
| Palatine | const 1, edge 1.15 | same | ✅ |
| Byblos | const 1, edge 1.15 | same | ✅ |
| Kemik | edge 1.15, const absent | const 1 inherited | ✅ |
| Caligo | edge 1.15, const absent | const 1 inherited | ✅ |

**Both playable planets — Verdure and Kemik — are now measured rather than assumed**, which was the
gap that mattered (§4.5).

Three VS1_5 planets (MarsLike, Testerran, WaterPlanet) declare no atmosphere at all and inherit from
`Templates/Legacy/PlanetWithAtmosphere`, which says **`AffectDistance = 100`** — an atmosphere
reaching 100 planet radii. That resolution is faithful to the game's own chain, but the number is
not usable, so it is extracted *and* warned (`implausibleAtmosphere`). Surface density is unaffected,
which is all v1 uses; only an altitude model would care. Geomeles has no atmosphere anywhere in its chain; it is now
extracted as unknown rather than assumed, and deferred until the planet ships (Backlog B1).

### 4.5 Which planets actually matter

**Verdure, Kemik, Palatine and Caligo** are reachable in the current build; the other six ship as
data but are not playable. (An earlier draft said only Verdure and Kemik — corrected from in-game
observation; either the roster grew or the first count was wrong.)

An earlier draft noted that both playable planets were among those whose atmosphere geometry we could
not read — which made the gap matter far more than a count of 8-of-10 suggested. **§4.4.2 closed
it:** the `BaseGuid` walk resolves both from the game's own chain, so Verdure and Kemik are now
`measured`. One planet remains unknown (Geomeles, Backlog B1) and it is not in the game.

Surface gravity was assumed here too, and that also turned out to be wrong — the planet's own gravity
generator states it outright (§5.3), as does its radius (§5.3.1.2). Both are extracted and `measured`.
They remain user-overridable, because a world may spawn a planet at a size of its own choosing, but
nothing in the UI has to be typed before it gives an answer.

---

## 5. Planets — corrected: they *do* have definitions

**An earlier draft of this document said planet data lives only in world saves. That was wrong.**
Planet definitions exist under `Content\Procedural\<Milestone>\Planets\<Name>\`.

### 5.1 The planet roster

| Milestone | Planets |
|---|---|
| VS1_5 | EarthLike, MarsLike, Testerran, WaterPlanet |
| VS2_0 | Verdure, Kemik |
| VS2_2 | Caligo (files say `Titan_`), Geomeles, Palatine |
| VS2_3 | Verdure, Kemik, Caligo, Palatine (re-tuned `_VS2-3` variants) |
| VS3_0 | Byblos (the water milestone) |

Note the **milestone-versioned duplicates** — `VerdureInfoDefinition.def` (VS2_0) *and*
`VerdureInfoDefinition_VS2-3.def` both exist. The app must not show "Verdure" twice, and must pick
the newest variant. This versioning also means older milestone folders are effectively dead data.

### 5.2 The reference chain, and what's at the end of it

`<Planet>InfoDefinition.def` is only a **debug-screen** entry
(`Game2:Keen.Game2.Client.Debugging.Screens.Voxels.PlanetInfoDefinitionObjectBuilder`) with
`Name` / `Preview` / `Spawn`. Following `Spawn`:

```
<Planet>InfoDefinition.def  ──Spawn──▶  <Planet>_Server.def  (PrefabDefinition)
                                          └──_entity.Definition──▶  <Planet>_ServerComposition.def
                                                                      (EntityCompositeDefinition, delta-encoded)
```

Inside the composition's `Changed` array (from `VS1_5\Planets\EarthLike\Data\Earthlike_Server.def`):

```json
{ "$Type": "Game2:...RangedAffectGenerators.Gravity.GravityGeneratorObjectBuilder",
  "AffectDistance": 1.5 },

{ "$Type": "Game2:...RangedAffectGenerators.Atmosphere.AtmosphereGeneratorObjectBuilder",
  "AffectDistance": 1.15,
  "ConstantAffectDistance": 1.08 },

{ "$Type": "VRage:Keen.VRage.Water.Components.PlanetaryWaterComponentObjectBuilder" }
```

This is a real find:

- **`GravityGenerator.AffectDistance: 1.5`** — the gravity well extends to 1.5× planet radius.
- **`AtmosphereGenerator.AffectDistance: 1.15` / `ConstantAffectDistance: 1.08`** — atmosphere is at
  **full density out to 1.08 R**, then **falls off to nothing by 1.15 R**. This is the SE1
  `MinPlanetaryInfluence`/`MaxPlanetaryInfluence` analogue, and it's the **atmospheric falloff model**
  §3.3 couldn't find on the thruster side. The environment carries the curve; the thruster just has a
  class.
- `PlanetaryWaterComponent` on EarthLike, consistent with water being staged for VS3.

These are **multipliers of planet radius**, all dimensionless.

#### 5.2.1 The atmosphere's strength is not in this file

The component above gives the atmosphere's *shape* and nothing about how much air is in it. The
strength sits on the generator **definition** the component points at, and the engine joins the two
in `AtmosphereGeneratorComponent`:

```csharp
SetData(new AtmosphereGeneratorData {
    Density              = _definition.Density,   // from the referenced definition
    AffectDistance       = ob.AffectDistance,     // from the component above
    ConstantAffectDistance = ob.ConstantAffectDistance,
    Resource             = _definition.Resource?.Name ?? StringId.NullOrEmpty });
```

Reading only the component therefore yields an atmosphere whose density is *assumed* to be 1.0 —
which held for every planet checked until it didn't. **Palatine's generator states `Density: 0`**:
a moon carrying a full set of atmosphere distances with no air inside them, where atmospheric
thrusters produce nothing. There are exactly three atmosphere generator definitions in the game;
that is the only one below 1 (Backlog B16).

The general lesson is the one §7.3 keeps re-teaching from a different direction: **a value's absence
from a `.def` file means nothing on its own.** Verdure's generator omits `Density` and the field's
object-builder default is `0f`, so read literally the game's flagship atmospheric planet is airless
— but the real `BaseGuid` graph in `definitionsets.vrb` has it inheriting `1` from the shared base.
`tc def <guid> [field ...]` answers this directly, printing the chain and naming the ancestor that
states each field.

### 5.3 SOLVED — surface gravity is in the definitions after all

> **This section's original conclusion was wrong.** It is kept below, struck through, because *how*
> it was wrong is the useful part. The corrected finding comes first.

**`GravityGeneratorObjectBuilder` states surface gravity outright**, and carries the whole falloff
model beside it:

```json
{ "$Type": "…RangedAffectGenerators.Gravity.GravityGeneratorObjectBuilder",
  "GravitationalAcceleration": 9.80665,   // m/s² at the surface
  "AccelerationDistance": 1.05,           // constant out to here
  "AffectDistance": 1.35,                 // zero beyond here
  "FallOffPower": -1,                     // exponent; -1 IS a sentinel here — linear falloff
  "GravityShape": "Spherical" }
```

**`FallOffPower` decompiled** (`GravityGeneratorComponent.CalculateGravitationalAccelerationMagnitude`):

```csharp
if (fallOffPower >= 0f) num2 = Math.Pow(AccelerationDistance / r, fallOffPower);   // r != 0, else 1
else /* asserts == -1 */ num2 = Math.Clamp(1.0 - (r - AccelerationDistance) / (AffectDistance - AccelerationDistance), 0.0, 1.0);
return GravitationalAcceleration * num2;
```

A non-negative value is a genuine exponent — inverse square would be `2` — while `-1` selects a
**linear** ramp, guarded by an assert reading "Currently only linear falloff is supported". And
planets are all linear: `DefaultGravityGenerator.def` pins `MinFallOffPower` and `MaxFallOffPower`
both to `-1`.

Note the trap in the old comment above, now corrected: `-1` here is a real sentinel, while the
identical-looking `-1` in a thrust class is not (§3.3). Two fields, same magic-looking value,
opposite meanings — the resemblance was assumed and was wrong.

All ten planets now extract as `measured`:

| Planet | m/s² | g | | Planet | m/s² | g |
|---|---:|---:|---|---|---:|---:|
| Verdure | 9.80665 | 1.000 | | Caligo | 4.09 | 0.417 |
| Kemik | 9.80665 | 1.000 | | Palatine | 3.27 | 0.333 |
| EarthLike, Byblos, Geomeles, MarsLike, Testerran, WaterPlanet | 9.80665 | 1.000 | | | | |

**Independently confirmed in game:** Verdure's HUD reads exactly `G: 1.00 g` on the surface, against
the file's 9.80665 m/s² = 1.000 g (§5.3.1). Two unrelated routes, same answer.

#### Why it looked absent for so long — two compounding mistakes

1. **The reader took one field off a component and ignored the rest.** `ReadPlanetGeometry` pulled
   `AffectDistance` from the gravity generator and never looked at `GravitationalAcceleration`
   sitting next to it. A grep for `Gravity` in the planet folders "confirmed" the absence, because
   the files that *do* state it — Caligo and Palatine — were never printed in full.
2. **`_entity.ObjectBuilders` has two encodings, and we handled one.** A planet's own prefab
   delta-encodes its components (`{ "$DeltaEncoded": true, "Changed": [...] }`); the legacy base
   templates list them as a **plain array**. Verdure and Kemik inherit gravity from
   `Templates/Legacy/PlanetWithoutAtmosphere`, so the walk reached the right file and then read
   nothing out of it. `BlockCompositionIndex.ReadComponentGuids` had already learned to handle both
   shapes — the same lesson, missed a second time.

**The rule this earns:** when a component is found, dump the *whole* payload before concluding a
field is absent, and never assume one container encoding. `tc planet <name>` exists for exactly
this — it walks the chain and prints every gravity and atmosphere payload it meets, so "where did
the walk stop?" is answerable without guessing.

### 5.3.0 ~~What's still missing: radius, and therefore surface gravity~~ (superseded)

Neither surface gravity nor planet radius appears in any `.def`. Checked:
`PlanetGeneratorDefinitionObjectBuilder` (heightmaps, `HillParams`, `MaxAltimeter`, `IsMoon` — no
radius), `PlanetEnvironmentDataDefinition`, and a `Radius|Mass|Gravity` grep across all of
`Content\Procedural\` (only flora/voxel-scatter hits).

**Interpretation:** planet radius is *instance* data, set when a planet is spawned into a world — the
same generator can be instantiated at different sizes. It lives in the world save `.vrb`. Surface
gravity is then presumably derived from radius by the engine.

**So, answering the question directly:** your instinct was right that planets carry their own data,
and the gravity/atmosphere **field shape** is per-planet, per-milestone, in readable JSON, so a
custom or modded planet shipping its own `.def` files **is picked up automatically** by a
GUID-index-based reader. That extensibility works.

~~But the **surface gravity magnitude** is not there~~ — **it is** (§5.3). What survives from the
plan below is only point 3:

1. ~~Read `AffectDistance` / `ConstantAffectDistance` per planet~~ — done, plus the magnitude.
2. ~~Ship a curated, user-editable gravity table marked `Assumed`~~ — **abandoned.** One was
   briefly built and then deleted: a hand-maintained table of numbers the files already state is a
   second source of truth that drifts, and it violates the project's first principle. The
   measurement that "justified" it turned out to be verification of a value we could extract.
3. **Auto-discover new planets from the scan**, and where one genuinely resolves nothing, show it
   with an editable field rather than hiding it. Still right, and now the rare case rather than the
   norm.

A **user override** remains, because a world can spawn a planet at a size of its own choosing — but
it is off by default and the extracted value is what shows.

### 5.3.1 Measured in game — and what the HUD tells us

The HUD reports gravity bottom-right as `G:` (natural) and `A:` (artificial, from gravity
generator blocks — `0.00` on a ship carrying none). Standing on **Verdure**:

| Where | `G:` |
|---|---:|
| On the ground | **1.00 g** |
| At the boundary of space | 0.33 g |
| Well into space | 0.00 g |

**The surface reading is the one that settles anything.** Verdure is exactly 1.00 g = 9.81 m/s², on
a default-sized world, and it is now the first entry in `Core.CuratedPlanetGravity`. That table is
the concrete form of point 2 above.

The other two readings are suggestive but **not usable on their own**, because the altitude they were
taken at is not known precisely.

> **The power-law speculation that stood here was wrong, and it is instructive that it fit.** This
> section previously reasoned: if "boundary of space" is the atmosphere edge at 1.15 R, then
> `g = g₀·(R/r)ⁿ` fits at n ≈ 7–8, since `(1/1.15)⁸ = 0.327` against the observed 0.33. It was
> hedged — "do not build on that", "a lead, not a result" — and the hedge earned its keep, because
> **the falloff is linear, not a power law** (§5.3 above). `FallOffPower: -1` is the engine's
> sentinel for a linear ramp, and every planet uses it.
>
> A curve with one free parameter will pass through one data point. That is not evidence; it is
> arithmetic. Two decimals of agreement made a wrong model look confirmed.

**What the real model predicts**, for Verdure — surface gravity out to 1.05 R, then linear to zero at
1.35 R:

| Where | predicted `G:` |
|---|---:|
| Ground → 1.05 R | 1.00 g |
| Atmosphere edge, 1.15 R | **0.67 g** |
| 1.25 R | 0.33 g |
| 1.35 R and beyond | 0.00 g |

So the observed 0.33 g was taken at roughly **1.25 R**, not at the atmosphere edge — consistent with
"boundary of space" meaning something higher than where the air runs out, and with the third reading
(0.00 g "well into space") being anywhere past 1.35 R.

**This is now a testable prediction rather than a fit**, and it is the natural check on the climb
profile: gravity should read about two thirds of a g where the atmosphere ends. If it reads 0.33
there instead, the extracted parameters are wrong and the curve is wrong with them.

#### 5.3.1.1 CONFIRMED in game — and the test needed no planet radius

A **2 671 kg ship with two Atmospheric Thruster 1 m** was flown straight up on Verdure until it could
climb no further. Everything about where it stops is fixed by numbers we already have — 40 kN each,
the air-density ramp, the effectiveness ramp and the falloff — so the model predicts the stop at
**r = 1.0950 R**, and predicts that gravity *there* reads **0.850 g**.

In game it hovered at **4.81 km, reading 0.84 g**.

| model | gravity at the hover point |
|---|---:|
| linear falloff (extracted) | **0.850 g** |
| power law n = 8 (the old fit) | 0.484 g |
| inverse square | 0.834 g |
| **measured** | **0.84 g** |

**The comparison is radius-free**, which is what makes it worth anything: both the stopping height
and the gravity there are expressed in planet radii, so `R` cancels and no assumption about planet
size enters. It also discriminates — the power law that once "fit" the single 0.33 g reading is out
by a factor of 1.75 here.

Two further predictions from the same session held: a mixed atmospheric/ion ship **reached space**
(the profile said it coasts through the handover dip for any planet under 278 km radius), and the
atmospheric-only ship **did not**.

**Planet radius, and the correction that came with it.** Inverting the measurement first gave ~50 km
against the 60 000 m the files state, and that 18 % gap was written up here as evidence that the
stated figure was a rendering parameter to be distrusted. **That was wrong, and the error was ours.**

`PlanetGeneratorDefinition` carries **`ZeroGround`** — the terrain's sea level as a fraction of the
radius, 0.015 on Verdure, or 900 m. Altitude is measured from the ground, so the surface is at
`r = 1 + ZeroGround`, not at `r = 1`. Putting it back:

| | |
|---|---|
| ground at `1 + 0.015`, R = 60 km, h = 4.81 km | **r = 1.09517** |
| predicted stop, from thrust and mass alone | **r = 1.09495** |
| gravity there | 0.849 g, against **0.84 g** measured |

Agreement to 0.0002 radii — **13 metres**. Radius, ground offset, gravity falloff, air ramp and
effectiveness ramp all reconcile at once, which no single fudge could do.

#### 5.3.1.2 The radius is in the files, two hops off the planet

Not world data after all. The chain is
`PlanetInfoDefinition → Spawn → composition → PlanetGeneratorDefinition → DetailCubemap`:

| | radius | `ZeroGround` |
|---|---:|---:|
| Verdure, Kemik, Byblos, EarthLike, MarsLike, Testerran, WaterPlanet | **60 000 m** | 0.015 / 0.01 / 0 |
| Caligo, Geomeles, Palatine (moons) | **20 000 m** | 0 |

**Why an earlier pass concluded otherwise.** It followed
`GravityGeneratorProcessorComponent`'s `planetRadius` back to `PlanetConfiguratorComponent.Radius`,
found `PlanetSpawnerPrefab.def` shipping `"Radius": 0`, and stopped — a placeholder read as proof of
absence. The generator branch was never walked. Two hops further on, every planet states its size.

A world can still spawn a planet at a size of its own choosing, so this is the shipped default and
not a promise about a particular save — exactly the standing surface gravity has, and overridable
for the same reason.

Gravity still falls off far faster than a point mass would suggest — Newton gives `(1/1.15)² = 0.756`
at the atmosphere edge — but the reason is that it is not a Newtonian field at all.

### 5.4 If we ever do want `.vrb`

`GameData\Vanilla\Worlds\<World>\` holds `savegame.vrb`, `sessioncomponents.vrb`, `assetjournal.vrb`
and a `Blobs\` directory. `.vrb` is the binary VRage container (magic `VR3B`) that blocked the
sibling `BlueprintHelperSE2` project. Per its `RESEARCH.md`, the only known route is
[`divinci/vrage-binary-serialization`](https://github.com/divinci/vrage-binary-serialization), which
loads SE2's own assemblies and drives the engine serializer — Windows-only, install-dependent,
fragile, early-stage, inconsistent package naming. **Not needed for v1.**

---

## 6. Filesystem layout (verified on this machine)

```
<SteamLibrary>\steamapps\common\SpaceEngineers2\
  Game2\  GameData\  redist\  VRage\  Licenses.txt
  GameData\Vanilla\
    Content\                            17,172 .def, 4,588 .dds, 1,396 .vrm ...
      Blocks\Thrusters\{Atmospheric,Hydrogen,Ion,Underwater}\<sizeCm>\*.def
      Blocks\Shared\Density\*.def
      Templates\Blocks\BaseDefinitions\*.def
      System\ResourceTypes\{Electricity,Hydrogen,Oxygen,Water}.def
      Procedural\VS{1_5,2_0,2_2,2_3,3_0}\Planets\<Name>\Data\*.def
    Worlds\<WorldName>\{savegame.vrb, sessioncomponents.vrb, Blobs\}

%AppData%\SpaceEngineers2\
  AppData\Blueprints\<Name>\{.container-info (JSON), grid.json.vrb (binary)}
  AppData\SaveGames\, Settings\, Temp\
```

**Locating the install:** do not hardcode. Parse
`C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf`, find the library whose `apps` block
contains **`1133870`**, append `steamapps\common\SpaceEngineers2`. On this machine that resolves to
**`G:\SteamLibrary\...`, not the C: default** — the naive path would have failed. Always allow manual
override.

---

## 7. Prior art and environment notes

- **`../BlueprintHelperSE2`** — our sibling project. Structure (`Core`/`Gui`/`Vrage`/`Cli`/`Tests`),
  `Se2Installation.cs`, `BlockDefinitionIndex*` are all reusable. Its `RESEARCH.md` is the `.vrb`
  reference.
- **Keen's Mod SDK `Editor` ships `Avalonia.*.dll`** — Keen built their modding editor in Avalonia.
  Good validation of the UI stack. We still don't depend on the SDK.
- **SE2's own `Game2\` folder also ships Avalonia** — which is *why* game assemblies can't be hosted
  in an Avalonia process (Technic §3). Important, not incidental.
- SE2 targets **`net9.0`** with `Microsoft.WindowsDesktop.App` (`SpaceEngineers2.runtimeconfig.json`).
- SE2 tools surveyed previously, none do thruster analysis:
  [SpaceEditor](https://github.com/InflexCZE/SpaceEditor),
  [SE-Block-Exchanger](https://github.com/MerabyLabs/SE-Block-Exchanger),
  [BlueprintBreakdown](https://github.com/charleyah/BlueprintBreakdown).

---

## 8. Open questions, ranked

**All v1 blockers are now closed** — the mass formula (§4.0), environmental effectiveness (§3.3),
cargo/tank capacity (§4.3), atmosphere geometry (§5.2), block occupancy (§4.0.0) and definition
inheritance (§4.4.1). What remains is refinement, not blocking.

Still open:

1. **Verify the 750 cargo container's 2 150 400 kg** (§4.3) — plausibly a placeholder.
2. **B2's 110 kg gap on large blocks** — both ends of the mass chain check out and the middle
   (native `ComputeTotalMass`) is unexamined.

Closed, kept because the answers are load-bearing:

3. ~~Both effectiveness ramps' curve shape~~ — **answered by decompiling both, no measurement
   needed** (Backlog B6). Air density and thrust effectiveness are each linear and clamped, exactly
   as modelled. The same read found a third atmosphere parameter we were not extracting (§5.2.1) and
   disproved a sentinel we had invented for negative `MinThrustAirDensity`.
4. ~~Recover `V` for cargo containers and tanks~~ — **superseded** (§4.0.0). The content cache gives
   occupancy for 1,454 blocks, so nothing needs solving by hand.
5. ~~Hydrogen mass per capacity unit~~ — **answered**: gas is massless, measured in game by watching
   a tank fill (§4.3, Backlog B3). Tank contents never convert to kilograms.
6. ~~`ResourcesRequiredToThrust` units per class~~ — **answered** (§3): resolve each block's
   `ConsumedResource.Type` GUID. Electricity is kW; hydrogen is L/s. Not comparable across classes.
   Note the GUID is usually **inherited**, not stated on the block.
7. ~~Template inheritance~~ — **confirmed real, and the mechanism is `BaseGuid`** (§4.4.1), not the
   slot-signature matching an earlier draft described. More pervasive than first thought: besides
   hydrogen's `ThrustClass`, seven of twelve thrusters inherit `Density`, no container states one,
   and most planets inherit their atmosphere.
8. ~~Hydrogen's missing `ThrustClass`~~ — **answered** (§3.4): the `HydrogenThrusterDefinition`
   template supplies `"Hydrogen"`. Measured, not assumed.

## 9. The tooling this research produced

**`tc dump-schemas`** — walks all 17,172 `.def` files, groups by `$Type`, emits the distinct field
set per type. Built first, and it earned its keep immediately: it found the two base templates §3.4
turns on, and it corrected a claim in this document that `ThrustDirection` appeared in no `.def`
(Technic §10.4). **It is the patch-diffing tool** — on patch day, dump and diff against the previous
output.

**`tc block [NAME]`** — dumps a block's occupancy boxes from the content cache, counting the cells
**two ways**: the engine's sum of group volumes, and a true union built by enumerating every cell
into a set. With no name it surveys the whole catalogue. Built to settle Backlog B2, and it did —
by killing the hypothesis rather than confirming it: **no block in the game has overlapping
occupancy groups**, so the double-counting theory was wrong and the cell counts are sound.

A diagnostic that disproves its own hypothesis is doing its job. Keep it: "is the occupancy sane?"
recurs whenever a mass looks wrong.

**`tc def <guid> [field ...]`** — prints a definition's real base chain out of `definitionsets.vrb`
and, for each named field, the nearest ancestor that states it — or says outright that none does, so
the type's default applies. Built because a `.def` that omits a field is genuinely ambiguous and the
two readings can be opposite: Verdure's atmosphere generator omits `Density`, whose object-builder
default is `0f`, but the graph shows it inheriting `1`. Reading the file alone would have made the
game's main atmospheric planet airless (§5.2.1, Backlog B16).

Keep it for the same reason as `tc block`: "where does this value actually come from?" is the
question this data punishes you for guessing at, and the `.def` files cannot answer it — the parent
pointer is not in them.

**The decompiler itself**, because most of what §3.3, §5.2.1 and §5.3 now assert came out of it and
the corpus is disposable:

```sh
dotnet tool install -g ilspycmd            # 10.1.1 at time of writing
ilspycmd -p -o <outdir> "<install>/Game2/Game2.Simulation.dll"
```

`-p` writes the whole assembly as a project — around 3 300 files, a few minutes — after which the
questions are `grep`. Single types are quicker via `ilspycmd -t <FullTypeName> <dll>`, but only if
you already know the name; the full dump is what lets you find `GetThrustEfficiency` without knowing
it exists. Not every type lives in `Game2.Simulation.dll` — `PlanetConfiguratorComponent` is in
`VRage.Voxels.dll` — so when a type will not resolve, find its assembly with
`grep -l <TypeName> *.dll` first.

**Reading the engine beat measuring the game, repeatedly.** B6 was written as "verify both ramps
in game"; both were settled in an afternoon by reading the two methods, and the same pass turned up
`atmosphere.Density` and disproved a sentinel we had invented. Measurement is still what confirms
the whole chain end to end (§5.3.1.1) — but it is the check, not the route.

**`tc verify`** — invariant checks against a real local install: every thruster pairs to a block
definition, every referenced GUID resolves, all thrust positive (templates excluded — they carry
`ThrustPower: 0`, and an early version of this check failed on them, which was the check being wrong,
not the data).

Together they are the answer to Technic §7.1.1's genuine gap: **CI can never catch "Keen changed the
data format", because no runner has the game.** These two commands are the manual substitute, and
running them is a routine patch-day action rather than something to hope CI covers.
