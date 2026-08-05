# `gamedata.json` — the contract

The artifact the **producer** (`tc`) writes and the **consumer** (GUI / web / `Core`) reads. Per
[Technic.md](Technic.md) §1 this is the project's real API: the two sides never link, so this file is
the entire interface between them.

Companion to [Research.md](Research.md) (where each value comes from) and [Technic.md](Technic.md)
(how it's produced and consumed).

---

## 1. Design rules

**R1 — Fully resolved.** No GUIDs, no cross-references, no engine concepts. The producer walks the
GUID graph so the consumer never has to. A consumer should be writable by someone who has never seen
a `.def` file.

**R2 — Store inputs, not outputs.** Block mass is *not* stored — `occupiedCells`, `massCurveModifier`
and `minBlockMass` are, and `Core` computes mass from them (Technic §5.4). Storing a derived value
alongside its inputs invites drift, and the formula is three lines that belong in one tested place.

**R3 — Named models, not formulas.** Where behaviour could change, the config names a model and
supplies parameters; `Core` implements a closed set (Technic §3.2). Never embed expression strings.
An unknown model name is a hard, actionable error — never a silent fallback.

**R4 — Provenance is local and explicit.** Values are plain scalars, implicitly `measured` (read from
game files). Anything that *isn't* measured is declared in the owning object's `provenance` map. This
keeps the file readable while making every soft number visible.

**R5 — Hand-editable.** A user must be able to open this and fix a gravity value. That means plain
scalars, meaningful key names, no base64, no packed arrays.

**R6 — Additive-safe.** Consumers ignore unknown fields. New optional fields are a minor version bump.

---

## 2. Top-level shape

```jsonc
{
  "schemaVersion": "1.0",

  "generator": {
    "tool": "tc",
    "version": "0.1.0",
    "extractedAt": "2026-08-04T14:22:31Z"
  },

  "source": {
    "gameBuild": "2.3.0.2798",          // max observed $Bundles version (Research §2.3)
    "fingerprint": "sha256:9f2c…",      // over (relpath, size, mtime) of Content/**/*.def
    "definitionCounts": {                // sanity signal — see §6
      "ThrusterDefinitionObjectBuilder": 12,
      "CubeBlockDensityDefinitionObjectBuilder": 4,
      "ResourceTypeDefinitionObjectBuilder": 4
    }
  },

  "models":       { /* §3 */ },
  "densities":    [ /* §4.1 */ ],
  "resources":    [ /* §4.2 */ ],
  "thrustClasses":[ /* §4.3 */ ],
  "thrusters":    [ /* §4.4 */ ],
  "containers":   [ /* §4.5 */ ],
  "tanks":        [ /* §4.5 */ ],
  "planets":      [ /* §4.6 */ ],

  "warnings":     [ /* §6 */ ]
}
```

`schemaVersion` is `major.minor`. **Major mismatch → refuse to load, with a clear message.** Minor
ahead → load, ignore unknown fields, note it. Configs outlive the app that wrote them; a user may
hand a newer file to an older build.

---

## 3. `models` — the parameterised behaviour (R3)

```jsonc
"models": {
  "blockMass": {
    "kind": "sqrtLog10CellCount",       // mass = modifier * sqrt(V) * log10(V) + minBlockMass
    "minBlockMass": 5.0                 // from CubeBlockMassConfiguration.def
  },
  "thrustEffectiveness": {
    "kind": "linearRampAirDensity"      // ramp between min/max ThrustAirDensity, clamped [0,1]
  },
  "atmosphereDensity": {
    "kind": "linearRampAltitude"        // 1.0 to constantAffectDistance, → 0 at affectDistance
  }
}
```

`kind` selects a `Core` implementation; sibling fields are its parameters. `minBlockMass` sits here
rather than on each block because it's a single global from one config file.

Both ramp models are **assumed linear** pending in-game verification (Research §8). If a ramp turns
out to be curved, that's a new `kind` plus a `Core` model — a contained change, not a rewrite. That
containment is the whole point of R3.

---

## 4. Entity collections

Every entity has a stable `id` (producer-generated — *not* a game GUID, per R1) and a `name` for
display.

Ids are derived, never copied from the graph:

| Collection | Id derived from | Example |
|---|---|---|
| `thrusters`, `containers`, `tanks` | block name | `AtmosphericThruster250` → `atmosphericThruster250` |
| `densities`, `resources` | definition filename | `Mostly Hollow.def` → `mostlyHollow` |
| `thrustClasses` | the configuration's `$Key` | `Ion` → `ion` |

Every reference (`density`, `consumedResource.resource`, `thrustClass`, a tank's `resource`) is one
of these ids. **A GUID appearing anywhere in the config is a bug** — `tc verify` checks for it — with
one deliberate exception: if two entries in the same collection would slug to the same id, the
producer falls back to the GUID and emits an `ambiguousId` warning, because a silent merge would give
blocks the wrong mass curve.

### 4.1 `densities`

```jsonc
{ "id": "mostlyHollow", "name": "Mostly Hollow", "massCurveModifier": 11.0 }
```

Four of these ship (7 / 11 / 20 / 35). Referenced by `density` id on any block.

### 4.2 `resources`

```jsonc
{ "id": "electricity", "name": "ResourceElectricity",
  "flowRateUnits": "Kilowatts", "storageUnits": "KilowattHours", "requiresConveyors": false }
```

Four ship: electricity, hydrogen, oxygen, water.

### 4.3 `thrustClasses`

Straight from `ThrustClassesConfiguration.def` (Research §3.3):

```jsonc
{ "id": "atmospheric", "maxThrustAirDensity": 0.8, "minThrustAirDensity": 0.2,
  "waterSubmersionTolerance": 1.0, "waterOnly": false }
```

⚠ **`min` may exceed `max`** — that's how ion is expressed (full thrust at *low* density). Consumers
must interpolate across the interval regardless of ordering. `minThrustAirDensity: -1` is the
sentinel for *no falloff* (hydrogen). Both rules are load-bearing; get them wrong and ion thrusters
silently invert.

### 4.4 `thrusters`

```jsonc
{
  "id": "atmosphericThruster250",
  "name": "Atmospheric Thruster 2.5m",
  "thrustClass": "atmospheric",        // null for hydrogen — see below
  "sizeCm": 250,
  "thrustNewtons": 287136.3,
  "consumedResource": { "resource": "electricity", "ratePerThrust": 650 },
  "density": "mostlyHollow",
  "occupiedCells": 288,                // V — Research §4.0.0
  "implemented": true,
  "provenance": { "occupiedCells": "measured" }
}
```

- `thrustClass` is **nullable** — hydrogen thrusters omit it in their own file and inherit it
  (Research §3.4). The producer resolves that, but consumers must still handle null rather than
  assuming a string.
- `occupiedCells` is normally `measured`, read from the game's own content cache (Research §4.0.0).
  It falls back to `derived` when extraction ran without the engine (`--no-engine`), where it comes
  from solving the mass formula against known masses. If it's `null`, the consumer must report
  *"mass unknown"* — **never substitute zero**, which would silently corrupt the self-weight solver
  (Technic §5.1).
- `implemented: false` covers blocks with art but no definition — underwater thrusters today
  (Research §3). They appear in the config so the UI can show "not in this build" (Design §4.4)
  rather than pretending they don't exist.
- `ratePerThrust` units come from the referenced resource — **not comparable across classes**
  (Research §3).

### 4.5 `containers` and `tanks`

```jsonc
// containers  — note: containers are Hollow (7), not Mostly Hollow; they inherit it (Research §4.4.1)
{ "id": "cargoContainer250", "name": "Cargo Container 2.5 m",
  "maxMassKg": 67200, "density": "hollow", "occupiedCells": 1000 }

// tanks
{ "id": "hydrogenTank500", "name": "Hydrogen Tank 5 m",
  "resource": "hydrogen", "maxCapacity": 32000, "maxDischargeRate": 4000,
  "density": "mostlyHollow", "occupiedCells": 1820 }
```

`maxMassKg` is the container's cargo *capacity*, directly from the game (Research §4.3) — distinct
from the block's own mass, which is computed. Both are needed: Design §3.2's load presets scale the
former, the self-weight solver uses the latter.

Tank `maxCapacity` is in the referenced resource's `storageUnits`, and it is **display information
only**. **Gas is massless in SE2** — measured in game by fitting an empty tank and watching it fill
with the ship's mass unchanged (Backlog B3) — so a full tank weighs exactly what an empty one does,
and `maxCapacity` never converts to kilograms. A tank contributes its own block mass and nothing
else.

### 4.6 `planets`

```jsonc
{
  "id": "verdure",
  "name": "Verdure",
  "milestone": "VS2_3",                       // newest variant wins — Research §5.1
  "surfaceGravity": 9.80665,                  // m/s² — GravityGenerator.GravitationalAcceleration
  "gravityAffectDistance": 1.5,               // × planet radius
  "atmosphere": {
    "affectDistance": 1.15,                   // density → 0 here
    "constantAffectDistance": 1.08            // density = 1.0 up to here
  }
}
```

Milestone-versioned duplicates exist in the game data (`Verdure` appears under VS2_0 *and* VS2_3).
**The producer resolves this — one entry per planet, newest milestone wins** — so the consumer never
sees a duplicate. `milestone` is retained for display and diagnostics.

**`surfaceGravity` is `measured`**, stated by the planet's gravity generator and usually inherited
from a legacy base template (Research §5.3). Verified against the game's own HUD: Verdure's
9.80665 m/s² is exactly the `G: 1.00 g` it reports on the surface.

A planet that resolves nothing gets `null` + `"unknown"` and a `unknownSurfaceGravity` warning, and
the UI shows an editable field rather than hiding the planet — which is what makes a future Keen
planet, or a player's custom one, usable the day it ships.

Consumers should still offer an **override**: a world can spawn a planet at a size of its own
choosing, so the extracted figure is the default, not a guarantee about *your* save.

`atmosphere` may also be `null` + `"unknown"` when no geometry exists anywhere in the planet's
inheritance chain. The consumer must read that as *unknown*, not as *airless*; today one unshipped
planet is affected (Backlog B1).

---

## 5. Provenance (R4)

Four values, on the owning object, keyed by field name:

| Value | Meaning |
|---|---|
| *(absent)* | **`measured`** — read from game files. The default; never written explicitly |
| `"derived"` | Computed by us from measured inputs (e.g. `occupiedCells`) |
| `"assumed"` | Curated guess or user edit (e.g. `surfaceGravity`) |
| `"unknown"` | Not available. The value **must** be `null`. Consumer shows a gap, never a zero |

Defaulting to `measured` keeps the file readable — most fields are plain scalars and the annotation
appears only where it matters. `unknown` is deliberately distinct from `assumed`: "we have no idea"
and "here's our best guess" produce different UI and different user actions.

---

## 6. `warnings` and `definitionCounts` — the anti-silent-failure machinery

```jsonc
"warnings": [
  { "code": "implausibleAtmosphere", "subject": "marsLike",
    "detail": "MarsLike: inherits an atmosphere extending to 100 planet radii…",
    "file": "Procedural/VS1_5/Planets/MarsLike/…def" },
  { "code": "missingDefinition", "detail": "Underwater thrusters: models present, no definition" }
]
```

**`subject` is the id of the entity a warning concerns**, or absent when it is about the extraction
as a whole. It exists so a consumer can show the warning *where it bites* — the note about MarsLike
belongs beside MarsLike, at the moment you select it, not in a list nobody opens. Matching a warning
to an entity by searching for its name inside `detail` would be exactly the sort of string heuristic
that has misfired in this project before, so the producer states it outright.

Extraction never throws on bad input (Technic §7.2) — it records and continues. Surfacing warnings in
the config means a degraded extraction is *visible* rather than silently producing confident wrong
answers.

`definitionCounts` is the blunt version of the same idea: "12 thrusters found" in the UI means a drop
to 8 after a patch is noticed immediately. This is the single cheapest defence against the failure
mode that actually matters here.

---

## 7. Worked example — end to end

Atmospheric Thruster 2.5 m on Verdure at sea level, from config to answer:

```
thrust        = 287136.3 N                                    (thrusters[].thrustNewtons)
airDensity    = 1.0                                            (atmosphereDensity model, surface)
effectiveness = ramp(1.0, min 0.2, max 0.8) → clamp → 1.0       (thrustEffectiveness model)
mass          = 11 × √288 × log₁₀(288) + 5 = 464.1 kg           (blockMass model)
                  ↑ densities[mostlyHollow]   ↑ occupiedCells  ↑ models.blockMass.minBlockMass
gravity       = 9.81 m/s²                                       (planets[verdure].surfaceGravity)
```

Feeding a 500 t hull into the sizing solver (Technic §5.1) gives n = 18, supported range
500–518.6 t. Every input traces to one config field, and three of them are named models.

---

## 8. Settled

1. **`sizeCm` is an integer.** Keen is not expected to introduce fractional block sizes; all shipped
   sizes are whole centimetres (50…1000). If that ever changes it's a minor version bump to a
   `sizeMetres` float, and the integer stays readable in the meantime.
2. **English only, no localisation.** `name` is the English display string the producer synthesises.
   The game ships `.loc-texts` files, but the app is English-only, so there's no `nameKey` indirection
   — one less join for the consumer and one less thing to keep in sync.
3. **`installPath` is deliberately omitted** from `source`. It would leak a local filesystem path into
   a file that may be hosted or shared, and nothing consumes it.
