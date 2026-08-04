# ThrustersHelper SE2 — Technical Design

Companion to [Research.md](Research.md) (what the game gives us) and [Design.md](Design.md) (what
the app does). This document is **architecture and technical decisions**.

Much of this is inherited from the sibling project `../BlueprintHelperSE2`, which has already paid
for several expensive lessons. Where that's the case it's called out — those are not fresh
decisions, they're hard-won constraints.

---

## 1. The decision that drives the architecture

Research §5 established that "touching the game" is **two different problems**, and conflating them
would be the main architectural mistake available to us:

| | **Definition data** (`.def`) | **Save data** (`.vrb`) |
|---|---|---|
| Format | Plain JSON | Binary `VR3B` container |
| Needs SE2 assemblies loaded | **No** | **Yes** |
| Third-party dependency | None | `vrage-binary-serialization` (early, unverified) |
| Can run in-process in an Avalonia app | **Yes** | **No** — see §3 |
| Fragility across patches | Low | High |
| Gives us | Thrust, power, density, resources, block catalogue | Planet gravity, blueprint grids, real ship mass |

**Everything the v1 calculator needs is in the left column.** That's the whole reason this project is
tractable in a way `BlueprintHelperSE2` wasn't: the primary data path needs **no game assemblies at
all**, so it's just JSON parsing.

**Decision: `.def` reading and `.vrb` reading live in separate projects, and the `.vrb` one is
optional at runtime.** The app must build, run, and fully calculate with the `.vrb` project absent or
failing. That's Design P5 (degrade, don't block) expressed in the project graph.

## 2. Project layout

Mirrors `BlueprintHelperSE2`'s conventions — `src/`, `.slnx`, `Directory.Build.props` — so moving
between the two repos is frictionless.

```
ThrustersHelperSE2/
  Research.md  Design.md  Technic.md  README.md
  src/
    ThrustersHelperSE2.slnx
    Directory.Build.props
    ThrustersHelper.Core/          net9.0          pure domain + math. no deps.
    ThrustersHelper.GameData/      net9.0          .def JSON reader + GUID index. no game asms.
    ThrustersHelper.Vrage/         net9.0-windows  OPTIONAL. .vrb / game assemblies. isolated.
    ThrustersHelper.Cli/           net9.0-windows  headless entry point + subprocess host
    ThrustersHelper.Gui/           net9.0-windows  Avalonia
    ThrustersHelper.Core.Tests/    net9.0          green with no SE2 installed
    ThrustersHelper.GameData.Tests/net9.0          green with no SE2 installed (fixtures)
```

Reference direction is strictly one-way. Nothing references the GUI; `Core` references nothing.

```
        Core  ◄── GameData ◄── Gui
          ▲         ▲           │  (spawns, does not link)
          └── Vrage ┘           ▼
                               Cli
```

### 2.1 `ThrustersHelper.Core` — pure

Domain model and math. **No Avalonia, no SE2, no filesystem, no JSON.** Everything unit-testable on a
machine with no game installed.

Contents: `Thruster`, `ThrustClass`, `Environment` (gravity + atmosphere), `ShipMass`,
`Direction`/`Axis`, `ThrustBudget`, the TWR/acceleration solver, and later the loadout optimiser.

Critically, Core also owns the **`Provenance` type** implementing Design P2 —
`Measured | Derived | Assumed` travels *with* each value through the calculation rather than being
reconstructed at the UI layer. If provenance is bolted on at the view level it will drift and lie;
attached to the value it cannot.

Core defines the interfaces it needs (`IGameDataSource`, `IEnvironmentCatalog`) and never the
implementations. This is what makes the frontend genuinely decoupled — the GUI can be driven by an
in-memory fake with no game present, which is also how we'll develop it.

### 2.2 `ThrustersHelper.GameData` — the primary data path

Reads `.def` JSON. Plain `System.Text.Json`, no game assemblies, no third-party packages.

Responsibilities:
1. **Install discovery** — parse `libraryfolders.vdf`, locate app `1133870` (Research §6). Manual
   override always available. Reuse `BlueprintHelperSE2`'s `Se2Installation.cs`.
2. **Index building** — walk `GameData\Vanilla\Content\`, parse the `$Bundles`/`$Type`/`$Value`
   envelope, build the **GUID → definition** map (Research §2.2).
3. **Projection** — resolve the thruster graph into Core's domain types.

`BlueprintHelperSE2` already has `BlockDefinitionIndexBuilder` / `BlockDefinitionIndex` /
`BlockDefinitionIndexStore`. **Review those before writing new ones** — but note theirs sits in the
`Vrage` project because it went through game assemblies. Ours doesn't need to, which should make it
both simpler and more robust.

### 2.3 `ThrustersHelper.Vrage` — quarantined

Only project permitted to touch SE2 assemblies. Windows-only, `net9.0-windows`, `<UseWPF>true</UseWPF>`
(§3.1). Needed **only** for planet gravity and blueprint grids — both deferred past v1.

**Decision: do not create this project until a feature actually requires it.** The layout above
reserves the slot. Creating it early invites the calculator to quietly grow a hard dependency on the
fragile path, which is exactly the failure mode §1 exists to prevent.

---

## 3. Inherited constraint: game assemblies cannot live in the GUI process

`BlueprintHelperSE2` discovered this the hard way. Its GUI **shells out to `bph.exe`** rather than
loading engine types in-process (`ExternalGridDecoder`, plus an MSBuild target copying the CLI into
a `cli/` subfolder of the GUI output).

The reason is visible in the install: **SE2's `Game2\` folder ships its own `Avalonia.Base.dll`,
`Avalonia.Controls.dll`, and the rest** — Keen built SE2's UI on Avalonia too. An Avalonia app that
loads SE2's assemblies into its own `AssemblyLoadContext` gets a version collision between our
Avalonia and theirs. Add SE2's demand for `Microsoft.WindowsDesktop.App`, and in-process hosting is a
losing fight.

**Decision: same pattern here, if and when we need `.vrb` at all.** `Gui` spawns `Cli` as a child
process and exchanges JSON over stdout. `Gui` references `Cli` with
`ReferenceOutputAssembly="false"` purely to force build ordering.

The happy consequence of §1: **v1 never pays this cost**, because `GameData` is pure JSON and runs
in-process quite happily.

### 3.1 Target framework: net9.0, not net10.0

`Directory.Build.props` pins:

```xml
<ThTargetFramework>net9.0</ThTargetFramework>
<ThWindowsTargetFramework>net9.0-windows</ThWindowsTargetFramework>
<AvaloniaVersion>12.1.1</AvaloniaVersion>
```

.NET 10 SDK is installed on this machine and net9 is out of support, so this needs justifying:
`SpaceEngineers2.runtimeconfig.json` declares `"tfm": "net9.0"` with `Microsoft.NETCore.App 9.0.0`
and `Microsoft.WindowsDesktop.App 9.0.0`. Matching the runtime the game was built and tested against
removes a variable from an already-fragile interop story.

**However** — that reasoning only truly binds `Vrage` and `Cli`. Since our core path has no game
interop, there's a legitimate case for `Core`/`GameData`/`Gui` on **net10.0**, leaving only the
quarantined projects on net9. **[OPEN]** I lean toward pinning everything to net9.0 for now to match
the sibling project and keep one flip point; revisit if we want .NET 10 features. Either way it's a
one-line change in `Directory.Build.props`.

---

## 4. The definition index: parsing, caching, staleness

### 4.1 Tolerant parsing is a hard requirement

Alpha game, 17,172 files, schemas that change per patch (Research §2.3 shows definitions stamped
from six different builds *in one install*). Non-negotiable rules:

- **Key off `$Type`, never filename.** Research §3 caught this: hydrogen thrusters are in
  `*_HydrogenThrusterDefinition.def` but carry the same `ThrusterDefinitionObjectBuilder` type as
  everything else. Filename-based dispatch would silently drop them.
- **Unknown `$Type` → skip silently.** We care about a handful of types out of hundreds.
- **Unknown *field* on a known type → ignore, don't fail.** Forward compatibility.
- **Missing expected field → `null`, recorded as a warning, surfaced in a diagnostics view.** Not an
  exception. `ThrustClass` is already legitimately absent on hydrogen.
- **A malformed file must never prevent startup.** Log it, skip it, keep going.

The failure mode to design against is *silent* data loss — dropping a thruster family and showing a
confident wrong answer. Hence: the index records counts per `$Type`, and the app surfaces
"9 thrusters found" so a drop from 9 to 5 after a patch is visible.

### 4.2 Cache with a cheap staleness check

Walking 17k files on every launch is wasteful. Two-tier, following `BlueprintHelperSE2`'s
ship-a-default-index-overridable-in-`%LOCALAPPDATA%` pattern:

1. **Shipped index** — a `thruster-index.json` committed to the repo, built from a known game build.
   Guarantees the app is useful on first launch and testable with no game installed.
2. **User index** — built from the local install on first run, cached to
   `%LOCALAPPDATA%\ThrustersHelperSE2\`, keyed by a **fingerprint** of the game data.

Fingerprint = hash over `(relative path, file size, last-write-time)` of `Content\**\*.def`, **not**
file contents — metadata-only keeps it to a directory enumeration rather than reading 17k files.
Cheap enough to run on every launch, which is what makes Design §4.4's staleness banner honest.

Store the observed `$Bundles` versions alongside the cache so the UI can show "build 2.3.0.2798".

### 4.3 Scope the walk

We do not need all 17k files. Restrict to the `$Type`s we consume — thruster definitions, powerable
block definitions, density, resource types, and the block templates. But **discover them by scanning
and filtering on `$Type`**, not by hardcoding paths, per Research §2.2 (folder layout is a human
convenience, not the data model).

---

## 5. Frontend: Avalonia, decoupled

- **Avalonia 12.1.1** (matching the sibling project), Fluent theme, Inter fonts.
- **`CommunityToolkit.Mvvm`** for observable properties and commands. No heavier framework —
  no Prism, no ReactiveUI, no DI container until something demands one.
- **Strict MVVM, and the decoupling must be real**: ViewModels depend only on `Core` abstractions.
  `Gui` may reference `GameData` for composition-root wiring, but no ViewModel takes a `GameData`
  type. The test of success: **the whole GUI runs against an in-memory fake data source with SE2 not
  installed.** Build that fake early — it's the development path, not just a test fixture.
- **No code-behind logic.** Views bind; they don't compute.
- Calculations are synchronous and fast (arithmetic over a handful of thrusters), so results
  recompute on property change with no async machinery. Only **index building** is async, with
  progress — it's the one slow operation.

### 5.1 Why a CLI project at all

Three reasons, in order: (1) it's the subprocess host if `.vrb` ever lands (§3); (2) it makes the
data-extraction path scriptable, which is how we'll answer Research §9's schema-dump question and
diff definitions across patches; (3) it keeps `GameData` honest — if it's usable headlessly, it's
genuinely decoupled from the UI.

---

## 6. Testing

- `Core.Tests` — the calculation math. Pure functions, no I/O, fast. This is where correctness lives.
- `GameData.Tests` — parsing, against **committed fixture `.def` files** copied from a real install
  (they're small JSON documents; the thruster def is 396 bytes). Includes deliberately malformed and
  unknown-`$Type` fixtures to prove §4.1's tolerance rules.
- **Both suites must run green on a machine with no SE2 installed**, matching the sibling project's
  rule. Anything requiring a live install is a manual/integration concern, not CI.
- A useful extra: a test that parses the *real* install if present and asserts invariants (≥1
  thruster per known family, all thrust values > 0), skipped when absent. That's the canary for
  "the game patched and broke our assumptions."

## 7. Sequencing

1. **Schema dump script** (Research §9) — walk all `.def`, group by `$Type`, dump distinct schemas.
   Answers several open questions at once and tells us exactly which types the reader must support.
   Throwaway; do it before designing the parser.
2. `Core` domain types + TWR math, with tests. No I/O.
3. `GameData` — install discovery, index, thruster projection, with fixture tests.
4. `Gui` skeleton against the **fake** data source. Validates the decoupling early.
5. Wire `GameData` into the GUI; add the staleness/provenance strip.
6. Curated editable gravity table (Design §7 Q3).
7. *Then* re-evaluate: optimiser, `.vrb` blueprint import, atmospheric falloff.

Steps 1–3 have **no unresolved research blockers**. The unknowns (mass curve, falloff, planet
gravity) all sit behind explicit `Assumed`-provenance values, which is precisely why Design P2 is
load-bearing rather than decorative.

## 8. Open technical questions

1. **net9 vs net10** for the non-interop projects (§3.1). Low stakes, one line.
2. **Reuse vs. rewrite** of `BlueprintHelperSE2`'s `Se2Installation` / `BlockDefinitionIndex*` — copy
   the files, or extract a shared package? Two small projects don't justify a shared package yet;
   I'd copy and accept the duplication, revisiting if a third project appears.
3. **Fixture licensing** — committing Keen's `.def` files as test fixtures. They're tiny config
   documents, but if this ever goes public, consider hand-authored equivalents instead.
4. **Does anything actually need `Vrage`?** If the curated gravity table (Design §7 Q3) is accepted
   and mass stays user-entered, v1 may ship with no game-assembly dependency whatsoever — which
   would make this dramatically more robust than the sibling project. Worth protecting.
