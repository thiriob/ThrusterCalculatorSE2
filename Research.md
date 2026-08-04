# ThrustersHelper SE2 — Research

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

- The SE1 pattern (a PB script that reads the live grid) is **not available**. This must be an
  external desktop app.
- We cannot query a *running* game for a live grid state. Everything comes from **files on disk**:
  the shipped definition data, and the user's saved blueprints/worlds.
- Modding *does* exist (Alpha VS1.5 "Modding" release) and there is a separate
  `Space Engineers 2 - Mod SDK` Steam depot. **We deliberately do not depend on it** — the app must
  work with only the base game installed. See §6.

---

## 2. The definition data — the important discovery

### 2.1 `.def` files are plain JSON

`GameData\Vanilla\Content\` contains **17,172 `.def` files**, and they are *plain, readable JSON*.
This is a much better position than SE1's `.sbc` XML or SE2's binary `.vrb` saves.

Real example — `Blocks\Thrusters\Atmospheric\100\AtmosphericThruster100_ThrusterDefinition.def`:

```json
{
  "$Bundles": {
    "Game2": "2.3.0.2722",
    "System.Runtime": "1.0.0.0",
    "VRage": "2.3.0.2722"
  },
  "$Type": "Game2:Keen.Game2.Simulation.WorldObjects.CubeBlocks.Movement.ThrusterDefinitionObjectBuilder",
  "$Value": {
    "Guid": "b4c0770f-75e1-4be6-a426-5fce05a8875e",
    "ThrustPower": 40000,
    "ThrustClass": "Atmospheric",
    "ResourcesRequiredToThrust": 75
  }
}
```

The envelope is consistent across all definition files:

| Field | Meaning |
|---|---|
| `$Bundles` | Assembly-version stamps for the bundles this file's schema was authored against |
| `$Type` | `bundle:FullyQualifiedClrTypeName` of the object builder |
| `$Value` | The payload. Always carries a `Guid` identity |

This is the **same envelope** as the `.container-info` files in blueprint folders (confirmed in the
sibling `BlueprintHelperSE2` research), so it's the engine-wide serialization shape.

### 2.2 It is a GUID-keyed graph, not a file hierarchy

Nothing references anything by name or path. Everything is by GUID. From
`AtmosphericThruster100_ThrustersPowerableBlockDefinition.def`:

```json
"$Value": {
  "Guid": "00516d6b-93ad-496a-8b30-80c5d29c1072",
  "UIData": { "Name": "ThrusterAtmo", "Icon": "{G}5cd48f85-..." },
  "Density":          "d8adcfdc-f8e2-467e-9d27-78deae4057da",
  "Recipe":           "136bb272-8077-4277-9da9-d0a1d8073cb9",
  "RecipeEfficiency": "30e09afc-4437-49fe-aff5-54553d05d3c4",
  "BlockKind":        "93e882df-b11a-4379-97ec-4176d195480f",
  "ConsumedResource": { "Type": "bcded093-f5c0-4997-af3a-a6fbd853ad66", "Amount": 0 }
}
```

**Implication for the tool: the first thing to build is a GUID → definition index** over the whole
`Content` tree. Every subsequent question ("what does this thruster weigh?", "what does it burn?")
is a graph walk from that index. Folder layout is a convenience for humans, not the data model, and
we should not depend on paths beyond an initial scan.

Resolving that `ConsumedResource.Type` GUID lands on `System\ResourceTypes\Electricity.def`:

```json
{
  "Guid": "bcded093-f5c0-4997-af3a-a6fbd853ad66",
  "Name": "ResourceElectricity",
  "FlowRateUnits": "Kilowatts",
  "StorageUnits": "KilowattHours",
  "RequiresConveyors": false
}
```

Only four resource types ship: `Electricity`, `Hydrogen`, `Oxygen`, `Water`.

### 2.3 Per-file version stamps vary — useful for staleness detection

Observed `$Bundles.Game2` values across files in the *same* install: `2.0.1.1811`, `2.0.1.4905`,
`2.0.1.5005`, `2.0.1.6909`, `2.2.0.540`, `2.3.0.2722`, `2.3.0.2798`. Keen ships definition files
stamped by whichever build last touched them, so the stamp is a **per-file authoring version**, not
the game version.

Useful consequence: we can detect "the game patched and our cached extraction is stale" cheaply by
hashing file metadata rather than re-parsing 17k files every launch. See Technic.md §4.

### 2.4 Delta encoding exists — and we should avoid needing it

Prefab/composition definitions are delta-encoded against a parent. From
`AtmosphericThruster100_Client.def`:

```json
"ObjectBuilders": {
  "$DeltaEncoded": true,
  "Keys": [ "b7bf405c-...", "e58b8f69-...", ... ],
  "Changed": [ { "Kind": "Insert", "Index": 10, "Value": null }, ... ],
  "Removed": []
}
```

Resolving these properly means reimplementing the engine's delta/inheritance semantics — expensive
and fragile.

**The good news: the numbers we actually need are not delta-encoded.** `_ThrusterDefinition.def` and
`_PowerableBlockDefinition.def` are flat, complete documents. We should scope the reader to the flat
definition types and treat delta-encoded prefab/composition data as out of scope unless something
forces us in. There *are* base templates in `Content\Templates\Blocks\BaseDefinitions\` (e.g.
`ThrustersPowerableBlockDefinition.def`, which carries `PCU: 150` and a default `ConsumptionPriority`)
— so some fields may be inherited rather than restated per block. **Open question:** confirm whether
per-block definitions always restate the fields we care about, or whether we must fall back to the
template when a field is absent. The atmospheric thruster's own powerable def *omits* `PCU` while the
template has it, which suggests inheritance is real and we will need at least template fallback.

---

## 3. Thruster data as it exists today

Extracted by parsing every `*_ThrusterDefinition.def` under `Content\Blocks\Thrusters\`:

| Block | ThrustClass | ThrustPower | ResourcesRequiredToThrust |
|---|---|---:|---:|
| AtmosphericThruster100 | Atmospheric | 40 000 | 75 |
| AtmosphericThruster250 | Atmospheric | 287 136.3 | 650 |
| AtmosphericThruster500 | Atmospheric | 1 516 383 | 2 400 |
| AtmosphericThruster1000 | Atmospheric | 15 465 370 | 16 000 |
| IonThruster100 | Ion | 8 950.306 | 40 |
| IonThruster150 | Ion | 82 492.88 | 240 |
| IonThruster500 | Ion | 856 368.56 | 1 800 |
| IonThruster750 | Ion | 5 636 987 | 8 000 |
| HydrogenThruster250 | *(absent)* | 1 895 631 | 12 |

Notes and cautions:

- **The trailing numeral is the block size in centimetres** (100 = 1 m, 1000 = 10 m), matching SE2's
  variable-size grid. It is *not* a tier index.
- **Hydrogen thrusters omit `ThrustClass` entirely.** The file is
  `HydrogenThruster250_HydrogenThrusterDefinition.def` — a different *filename* convention, but the
  same `$Type` (`ThrusterDefinitionObjectBuilder`). The parser must therefore key off `$Type`, **not
  filename**, and must treat a missing `ThrustClass` as a valid case (engine default). **Open
  question:** what does the engine default to, and does hydrogen get a class later?
- **`ResourcesRequiredToThrust` is not comparable across classes.** Atmospheric/Ion consume
  Electricity (kW, per §2.2). Hydrogen's value of 12 against 1.9 MN of thrust is implausible as kW —
  it is almost certainly hydrogen flow in different units. Do not build a cross-class efficiency
  metric until each class's consumed-resource GUID is resolved individually.
- **Underwater thrusters exist as art but not as data.** `Content\Blocks\Thrusters\Underwater\`
  has size folders `50/150/250/750` with models and materials but **zero `.def` files** — consistent
  with water being the unshipped VS3 milestone. The tool must handle "block folder exists, no
  definition" without crashing, and ideally surface it as "not yet implemented in this build."
- Present thruster families: `Atmospheric` (4 sizes), `Ion` (4), `Hydrogen` (4: 50/200/250/750),
  `Underwater` (4, data-less). Only one hydrogen size was read in detail; the other three should be
  parsed the same way.
- Thrust does not follow a clean power law against size. Thrust ÷ size³ for atmospheric gives
  0.040 / 0.018 / 0.012 / 0.015 across 100/250/500/1000 — non-monotonic. **Treat the table as data,
  never interpolate a formula.** This is exactly why the app must read the game rather than model it.

### 3.1 What is *not* in the data — the real modelling gap

The community understanding of SE1 thrusters (atmospheric thrusters lose effectiveness with air
density; ion thrusters lose effectiveness inside atmosphere; hydrogen works everywhere) is **not
represented in any field we found**. There is no `MinPlanetaryInfluence` / `EffectivenessAtMinInfluence`
analogue in the SE2 `ThrusterDefinition`.

Either the curve is hardcoded in engine code, or lives in a definition type we have not identified,
or SE2 has not implemented atmospheric falloff yet. **This is the single most important open question**
— without it, "how much thrust do I get at 2 km altitude on Verdure" is unanswerable.

Also note Keen's own support forum has active *design* threads proposing substantial thruster
reworks (fans vs. jets, oxidizer boosters, xenon for ion). These are **proposals, not shipped
mechanics** — they contain no numbers and must not be treated as current behaviour. But they are a
strong signal that this data will churn.

---

## 4. Mass — not a stored value

There is no `Mass` field on a block. `PowerableBlockDefinition.Density` points at one of four shared
definitions in `Content\Blocks\Shared\Density\`:

| Definition | `MassCurveModifier` |
|---|---:|
| Hollow | 7 |
| Mostly Hollow | 11 |
| Mostly Solid | 20 |
| Solid | 35 |

Thrusters are `Mostly Hollow` (11).

So block mass is derived: `mass = f(blockSize, MassCurveModifier)`, where `f` is a curve that is
**not present in the definition data** — a full-tree grep for `MassCurve` finds only these four
files. The curve lives in engine code.

This matters a lot: **a thruster calculator needs ship mass, and ship mass is exactly the thing SE2
does not hand us in data.** Options, in order of preference:

1. **Read mass from a blueprint's own metadata** if the grid file carries a computed total (needs
   `.vrb` decoding — §5).
2. **Empirically derive the curve**: build test grids in-game, read displayed mass, fit against
   size and modifier. Tedious but gives a real model, and only needs redoing when Keen retunes.
3. **Have the user enter target mass manually.** Always available as a fallback and probably the
   right v1 behaviour regardless.
4. Recover the curve from engine assemblies by decompilation — highest fidelity, highest fragility,
   and it drags in the dependency we are trying to avoid.

**Recommendation: ship (3) first, pursue (2) as the differentiator, treat (1) as the stretch goal.**

---

## 5. Planet gravity — the awkward one

Gravity is **not** in `.def` data. A full grep for gravity fields returns only gravity *generator*
blocks, character gravity sets, and water-domain gravity proxies — no planet surface gravity.

Planets exist as named content: `Delfos`, `Kemik`, `Verdure`, and `Moons` (found under
`Content\System\ColonizationMap\Models\Planets\`, i.e. map *models*, not physics definitions).

Actual planet parameters appear to live inside world saves — `GameData\Vanilla\Worlds\<World>\`
contains `savegame.vrb`, `sessioncomponents.vrb`, `assetjournal.vrb` and a `Blobs\` directory of
GUID-named files. `.vrb` is the **binary** VRage container (magic bytes `VR3B`), the same format
that blocked the sibling `BlueprintHelperSE2` project.

Prior art from `BlueprintHelperSE2/RESEARCH.md`: the way to read `.vrb` is
[`divinci/vrage-binary-serialization`](https://github.com/divinci/vrage-binary-serialization), which
does **not** reimplement the format — it loads SE2's own assemblies and drives the engine's real
serializer. That makes it Windows-only, install-dependent, and fragile across patches. That research
also flags inconsistent package naming (`Bjornabe.Vrbe.Core` vs `Bjornabe.Vrb.Core`) and very early
maturity — verify against real source before depending on it.

**Design consequence (this is the key one):** the "touches the game" concern is really **two**
concerns with completely different risk profiles.

| | `.def` JSON reading | `.vrb` binary reading |
|---|---|---|
| Needs game assemblies | **No** | Yes |
| Fragility across patches | Low (JSON, tolerant parsing) | High |
| Third-party dependency | None | Early-stage, unverified |
| Covers | Thruster stats, power, density, resources | Planets/gravity, blueprint grids, real ship mass |

They must not live in the same project. The calculator has to be fully usable with only the JSON
path working. See Technic.md.

**Pragmatic v1:** ship a small curated gravity table (user-editable) for the known planets, sourced
by reading the in-game HUD, and treat `.vrb` planet extraction as a later capability. A wrong-but-
editable number beats a blocked feature.

---

## 6. Filesystem layout (verified on this machine)

```
<SteamLibrary>\steamapps\common\SpaceEngineers2\
  Game2\  GameData\  redist\  VRage\  Licenses.txt
  GameData\Vanilla\
    Vanilla<...>.def                  (root manifest, 1589 B)
    Content\                          20 top-level dirs; 17,172 .def, 4,588 .dds, 1,396 .vrm ...
      Blocks\Thrusters\{Atmospheric,Hydrogen,Ion,Underwater}\<sizeCm>\*.def
      Blocks\Shared\Density\*.def
      Templates\Blocks\BaseDefinitions\*.def
      System\ResourceTypes\{Electricity,Hydrogen,Oxygen,Water}.def
    Worlds\<WorldName>\{savegame.vrb, sessioncomponents.vrb, Blobs\, .container-info}

%AppData%\SpaceEngineers2\
  AppData\Blueprints\<Name>\{.container-info (JSON), grid.json.vrb (binary), icon.png}
  AppData\SaveGames\, AppData\SE1GridsToImport\
  Settings\, Temp\{Logs, LocalMods, CrashReports, ...}
```

**Locating the install:** do not hardcode. Parse
`C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf`, find the library whose `apps` block
contains **`1133870`** (SE2's app id), then append `steamapps\common\SpaceEngineers2`. On this
machine that resolves to `G:\SteamLibrary\...`, *not* the default C: library — so the naive path
would have failed. Always allow a manual override.

The Mod SDK is a **separate** depot (`Space Engineers 2 - Mod SDK`). We are not depending on it.
Worth noting though: Keen's own Mod SDK `Editor` ships `Avalonia.*.dll` — **Keen built their modding
editor in Avalonia**. Good validation of the chosen UI stack, and their binaries are a reference for
what a mature SE2-adjacent Avalonia app looks like.

---

## 7. Prior art

- **`../BlueprintHelperSE2`** — our own sibling project. Structure (`Core` / `Gui` / `Vrage` / `Cli` /
  `Core.Tests`) is the template we're following here, and its `RESEARCH.md` is the reference for the
  `.vrb` problem. Reuse the install-discovery and `.vrb` access work rather than redoing it.
- **SE2 tools surveyed in that project** (none do thruster analysis):
  [InflexCZE/SpaceEditor](https://github.com/InflexCZE/SpaceEditor),
  [MerabyLabs/SE-Block-Exchanger](https://github.com/MerabyLabs/SE-Block-Exchanger),
  [charleyah/BlueprintBreakdown](https://github.com/charleyah/BlueprintBreakdown).
- **SE1 lineage** — community thruster calculators and the wiki thrust-per-MW tables. Useful for
  *what questions players ask*, useless for SE2 numbers. Explicitly do not port SE1 constants.

---

## 8. Open questions, ranked

1. **Atmospheric/ion effectiveness curve** (§3.1) — where does environment modulate thrust? Blocks
   the headline feature. Investigate: grep engine assemblies for the curve; or measure empirically
   in-game at varying altitude.
2. **The mass curve** (§4) — `MassCurveModifier` → kg. Blocks automatic ship mass.
3. **Planet gravity source** (§5) — `.vrb` world decoding, or curated table.
4. **Template inheritance** (§2.4) — must we fall back to `Templates\Blocks\BaseDefinitions\` when a
   field is absent? Cheap to answer by diffing a few blocks against their template.
5. **`ResourcesRequiredToThrust` units per class** (§3) — resolve each thruster's `ConsumedResource`
   GUID; confirm hydrogen's `12`.
6. **Hydrogen's missing `ThrustClass`** (§3) — engine default?
7. Do the other three hydrogen sizes (50/200/750) and the ion/atmo sets all parse identically?

## 9. Suggested next investigation (before writing app code)

Write one throwaway script that walks all 17,172 `.def` files, groups by `$Type`, and dumps the
distinct schema per type. That single artefact answers Q4, Q5, Q7 at once and tells us exactly which
`$Type`s the real reader must support — for a few minutes' work, before committing to any parser
design.
