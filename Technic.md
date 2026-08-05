# ThrusterCalculator SE2 — Technical Design

Companion to [Research.md](Research.md) (what the game gives us) and [Design.md](Design.md) (what
the app does).

Much is inherited from `../BlueprintHelperSE2`, which already paid for several expensive lessons.
Where that's the case it's called out — those are constraints, not fresh decisions.

---

## 1. The central architectural decision: producer / artifact / consumer

The app splits into **two programs that never link to each other**, joined by a versioned JSON file.

```
   ┌─────────────────────────────┐          ┌──────────────────────┐
   │  PRODUCER   (tc.exe)        │          │  CONSUMER  (GUI)     │
   │                             │  writes  │                      │
   │  • scan .def files          │ ───────► │  • read gamedata.json│
   │  • decompiled/engine math   │  reads   │  • calculate         │
   │  • resolve GUID graph       │          │  • render            │
   │                             │ gamedata │                      │
   │  needs: SE2 install, Win    │  .json   │  needs: nothing      │
   └─────────────────────────────┘          └──────────────────────┘
```

**The consumer requires nothing but the JSON file.** No Space Engineers install, no game assemblies,
no Windows-specific API, no filesystem scanning. The producer holds *all* the mess — install
discovery, 17k-file scanning, delta decoding, engine calls.

This is the strongest available version of the decoupling, and it pays for itself several times:

- **The release needs no data in it.** `tc.exe` ships beside the app, so a user generates a config
  from their own install on first run — nothing of Keen's is redistributed, and no bundled config
  can be stale on arrival.
- **Users regenerate on their own schedule.** Patch day: click Rebuild, producer runs, new JSON.
  No app update needed for a retune.
- **Web host becomes possible** (§9). The consumer is pure computation over a data file, so it can
  target WASM, or a server, or anything else. The desktop GUI stops being the only option.
- **CI is trivial.** The consumer's entire test surface is "given this JSON, compute that." No game,
  no platform, no fixtures from Keen.
- **Failure is contained.** If the producer breaks after a patch, the app keeps working on the last
  good JSON. Nothing at runtime depends on the fragile path.

**Consequence to hold firmly: the JSON schema is the most important artifact in the project.** It's
the real API. Design it deliberately (§3), version it (§3.3), and don't let convenience leak
producer concerns into it.

## 2. Project layout

```
ThrusterCalculatorSE2/
  Research.md  Design.md  Technic.md  Schema.md  README.md
  .gitignore                          ignores **/gamedata.json and Keen .def copies
  tests/fixtures/synthetic-gamedata.json    committed, obviously fake (§7.1)
  src/
    ThrusterCalculatorSE2.slnx
    Directory.Build.props

    ── consumer side ──
    ThrusterCalculator.Model/            net9.0  JSON schema types + (de)serialization
    ThrusterCalculator.Model.Tests/      net9.0  contract tests against the synthetic fixture
    ThrusterCalculator.Core/             net9.0  domain + math. depends on Model only.
    ThrusterCalculator.Core.Tests/       net9.0  green on a clean clone, no SE2

    ── producer side (needs an SE2 install at runtime) ──
    ThrusterCalculator.Extraction/       net9.0          .def scan → Model
    ThrusterCalculator.Extraction.Tests/ net9.0          synthetic .def fixtures (§7.1)
    ThrusterCalculator.Engine/           net9.0-windows  hosts SE2 assemblies; reads contentcache.vrb
    ThrusterCalculator.Cli/              net9.0-windows  tc.exe — the producer

    ── frontend ──
    ThrusterCalculator.Gui/              net9.0  Avalonia. Model + Core only.
    ThrusterCalculator.Gui.Tests/        net9.0  view-model tests, headless
```

**Only `Engine` and `Cli` use the Windows TFM.** `Engine` hosts SE2's own assemblies to read the
content cache (§10.2.0) and therefore needs `net9.0-windows` plus `UseWPF`; `Cli` inherits that by
referencing it. Everything else — `Model`, `Core`, `Extraction`, `Gui` and all test projects — stays
plain `net9.0`.

`Extraction` in particular is deliberately kept platform-neutral and engine-free: it talks to
`IOccupancySource`, so it remains testable without a game install and the engine is an enrichment
rather than a dependency.

Two things that split protects:

- `Extraction.Tests` (net9.0) can reference `Extraction`, which a net9.0 project could not do if
  `Extraction` were Windows-targeted.
- The consumer half stays trivially WASM-eligible (§9) rather than needing untangling later.

Reference graph — note the **absence** of any arrow from `Gui` into the producer side:

```
   Model ◄── Core ◄── Gui
     ▲                 │ spawns tc.exe as a child process (no assembly reference)
     │                 ▼
     └── Extraction ◄── Cli ──► Engine
```

`Gui` references `Cli` only with `ReferenceOutputAssembly="false"`, purely to force build ordering
and to copy `tc.exe` into the output — exactly the pattern `BlueprintHelperSE2` uses. **No producer
type is ever visible to the GUI.**

### 2.1 `Model` — the contract

Plain DTOs plus `System.Text.Json` config. No logic, no I/O, no dependencies. Deliberately its own
project rather than living in `Core`, because both sides depend on it and neither should depend on
the other.

### 2.2 `Core` — pure

Domain types and math (§5). Depends on `Model`, nothing else. **No Avalonia, no SE2, no filesystem,
no Windows API.** Fully testable, and WASM-compatible by construction (§9).

Owns `Provenance` (`Measured | Derived | Assumed`, Design P2) — which now travels *in the JSON*
as well as through the calculation, so the consumer knows the confidence of every number without
asking the producer.

### 2.3 `Extraction` / `Engine` / `Cli` — the producer

`Extraction` does install discovery (`libraryfolders.vdf`, app `1133870`, Research §6), walks
`Content\`, builds the GUID index, resolves the graph, and emits `Model` objects.

`Engine` is the quarantined piece (§10.2.0): it hosts SE2's own assemblies to read two things out of
`.vrb` — block occupancy from `contentcache.vrb`, and the `BaseGuid` inheritance graph from
`definitionsets.vrb` (§7.2.2). Copied from `../BlueprintHelperSE2`, whose comments carry the hard-won
details — which assemblies must *not* be loaded, and why the allocator is thread-local.

**`Extraction` never references it.** They meet at `IOccupancySource` and `IDefinitionInheritance`,
so a failure to host the game degrades to the built-in table and to no inheritance instead of
failing the run, and extraction stays testable with no game present.

`Cli` (`tc.exe`) is the producer host and the only thing that needs to exist for a rebuild.

---

## 3. The artifact: `gamedata.json`

> **Full specification: [Schema.md](Schema.md).** That document is the contract, written to stand
> alone (it suits a wiki page). This section covers only the decisions behind it; the field-by-field
> reference lives there, along with the worked example. The committed synthetic fixture is
> `tests/fixtures/synthetic-gamedata.json`.

### 3.1 What goes in

Everything the calculator needs, fully resolved — no GUIDs, no cross-references, no engine concepts:

- **Thrusters**: id, display name, class, size, thrust (N), consumed resource + rate, density,
  occupied cells.
- **Thrust classes**: the ramp endpoints from `ThrustClassesConfiguration.def` (Research §3.3).
- **Planets**: name, surface gravity, atmosphere geometry (`affectDistance`,
  `constantAffectDistance`), milestone.
- **Cargo containers / tanks**: capacity, density, occupied cells.
- **Densities / resources**: the small shared lookup tables.
- **Metadata**: schema version, game build, fingerprint, per-`$Type` counts (§7.2), warnings.

Two shape decisions, both settled in Schema.md and worth restating because they're easy to get wrong:

- **Store inputs, not outputs.** Block mass is *not* in the file. `occupiedCells`,
  `massCurveModifier` and `minBlockMass` are, and `Core` computes mass from them (§5.4). A derived
  value stored beside its inputs drifts; the formula is three lines and belongs in one tested place.
- **Provenance is sparse, not per-value.** Values are plain scalars, implicitly `measured`. Only
  non-measured fields are named in the owning object's `provenance` map. Wrapping every scalar in
  `{value, provenance}` would double the file size and destroy hand-editability (R5) for annotation
  that is absent on the large majority of fields.

```jsonc
{ "id": "verdure", "surfaceGravity": 9.81, "gravityAffectDistance": 1.5,
  "provenance": { "surfaceGravity": "assumed" } }
```

Note `unknown` is a distinct fourth value from `assumed`, and requires the value to be `null` — "we
have no idea" and "here's our best guess" drive different UI and different user action.

### 3.2 Calculation models: parameterise, don't embed formulas

You raised storing calculation models in the config. Worth being precise, because there are three
levels and only one is right:

| | Approach | Verdict |
|---|---|---|
| (a) | **Data only** — values and tables; all math hardcoded in `Core` | Too rigid: a retuned curve needs an app release |
| (b) | **Named models + parameters** — `{"model":"powerCurve","exponent":0.72,"min":5}`; `Core` implements a small closed set of named models | ✅ **This one** |
| (c) | **Formula strings** evaluated at runtime | Over-engineered; buys nothing real |

Why (c) loses despite being tempting: the argument for it is "if Keen changes the formula's *shape*,
users regenerate and stay correct without an app update." But the producer has to *read* that new
shape out of the engine — so the producer needs new code anyway. You cannot skip shipping a release.
Meanwhile (c) costs an expression evaluator, an eval surface in a file users hand-edit, and math
that can't be unit-tested statically.

(b) gets the real benefit — **retuned parameters flow through with no release** — which is the
common case in an alpha, since `MassCurveModifier` and `MinBlockMass` already live in `.def` and will
change far more often than the formula's shape. If the shape genuinely changes, that's a code change
and *should* be, because it needs new tests.

So: `Core` owns a small registry of named models (`linearRamp`, `powerCurve`, …); the JSON selects
and parameterises them. An unknown model name is a clear, actionable error — not a silent fallback.

### 3.3 Versioning and trust

- **`schemaVersion`** — consumer refuses to load a major mismatch and says so plainly. Configs
  outlive the app version that wrote them; a user may hand a newer config to an older app.
- **`gameBuild`** — max observed `$Bundles` version (Research §2.3), shown in the UI.
- **`sourceFingerprint`** — hash over `(relative path, size, mtime)` of `Content\**\*.def`. Metadata
  only: a directory enumeration, not 17k reads. Cheap enough to check on every launch, which is what
  makes Design §4.5's staleness banner honest rather than decorative.
- **Hand-editable on purpose.** It's where `Assumed` values (planet gravity, block masses) live until
  better sources land. Users correcting them shouldn't have to wait for us.

### 3.4 Distribution: the repo never contains real game data

**Decision: `gamedata.json` is not committed.** It is a build output, not source. `.gitignore`
carries `**/gamedata.json`, and the same reasoning as the `.def` files applies — we don't
redistribute Keen's numbers from the repo.

Who gets a config, and how:

| Audience | How they get `gamedata.json` |
|---|---|
| **Power users / self-hosters** | Run `tc extract` against their own install |
| **Web users** | The server already hosts one; they never think about it |
| **Desktop binary users** | Generated on first run by the `tc.exe` bundled beside the app |

The three paths converge on the same file, produced by the same tool.

### 3.5 Consequence: releases cannot be fully automated on CI

If the repo has no `gamedata.json`, something must produce one for the desktop release — and
**GitHub Actions runners don't have Space Engineers installed.** So packaging a binary requires a
step on a machine with the game.

Options, in order of preference:

1. **Manual release step.** Dev runs `tc extract` locally, attaches the output to the release / drops
   it into the packaging input. Simple, honest, one command. **Recommended for now.**
2. **First-run fetch.** Ship the binary without a config; on first launch it downloads from the same
   host serving the web version, falling back to a bundled copy. Elegant — the hosting already exists
   in this model — but adds a network dependency and an offline story to design. Worth revisiting if
   releases become frequent.
3. Self-hosted runner with SE2 installed. Over-engineering for this project's size.

Either way: **CI builds and tests the code; it does not build the data.** Keep that boundary explicit
so nobody wastes time trying to make the extraction job run on a hosted runner.

---

## 4. Inherited constraint: game assemblies cannot live in the GUI process

`BlueprintHelperSE2` learned this the hard way — its GUI shells out to `bph.exe` rather than loading
engine types in-process.

The reason is visible in the install: **SE2's `Game2\` folder ships its own `Avalonia.Base.dll`,
`Avalonia.Controls.dll` and the rest** — Keen built SE2's UI on Avalonia too. An Avalonia app loading
SE2's assemblies hits a version collision. Add SE2's demand for `Microsoft.WindowsDesktop.App` and
in-process hosting is a losing fight.

Under the producer/consumer split this stops being a workaround and becomes **structurally
inevitable**: the GUI couldn't reference the engine even if it wanted to. The Rebuild button spawns
`tc.exe`, which writes JSON and exits. Batch, not IPC chatter.

### 4.1 Target frameworks

`src/Directory.Build.props` is the single flip point:

```xml
<TcTargetFramework>net9.0</TcTargetFramework>
<TcWindowsTargetFramework>net9.0-windows</TcWindowsTargetFramework>   <!-- Engine and Cli only -->
<AvaloniaVersion>12.1.1</AvaloniaVersion>
```

net9 (not net10, though the SDK is installed) because `SpaceEngineers2.runtimeconfig.json` declares
`"tfm": "net9.0"` with `Microsoft.NETCore.App 9.0.0` and `Microsoft.WindowsDesktop.App 9.0.0`.
Matching the runtime the game was built against removes a variable.

That reasoning binds `Engine`, which actually loads those assemblies (§10.2.0), and `Cli`, which
references it. It's kept project-wide for a single flip point and to match the sibling repo.
**`Model` and `Core` must stay platform-neutral regardless** — that's what keeps the web target open
(§9). Don't let a Windows dependency drift into them, or into `Extraction`, which is what keeps
extraction testable with no game present.

---

## 5. The sizing math

Lives in `Core`, pure and tested.

### 5.1 Thruster self-weight is a fixed point with a closed form

Naive `n = requiredThrust / thrustPerUnit` is wrong: adding thrusters adds mass, raising the
requirement (Design §4.2).

With `M` = ship mass excluding thrusters, `T` = thrust per unit, `m` = mass per unit, `g` = surface
gravity, `R` = target TWR, `E` = environmental effectiveness ∈ [0,1] (§5.3):

```
        n·T·E  ≥  R·g·(M + n·m)
   n·(T·E − R·g·m) ≥  R·g·M

              R·g·M
   n  ≥  ─────────────────         n = ⌈ … ⌉
          T·E − R·g·m
```

**The denominator is the whole story.** If `T·E − R·g·m ≤ 0` the thruster cannot carry its own weight
at this gravity and TWR, and **no `n` is a solution** — the naive formula would return a confident
positive by dividing by a negative, or spin forever if solved iteratively. Guard it explicitly and
report it as Design §4.2 describes.

### 5.2 The supported range falls out of the same formula

```
   M_max  =  n·(T·E − R·g·m) / (R·g)
```

`[M, M_max]` is the range shown per configuration (Design §4.1). Worked example — 500 t hull,
Atmospheric 2.5 m (`T` = 287 136 N, `m` = 464 kg), `g` = 9.81, `R` = 1, `E` = 1:

```
   n ≥ 9.81·500 000 / (287 136 − 9.81·464) = 4 905 000 / 282 584 = 17.36  →  n = 18
   M_max = 18 · 282 584 / 9.81 = 518 559 kg
```

Check: 18 × 287 136 = 5 168 448 N vs (500 000 + 18×464) × 9.81 = 4 986 933 N → TWR 1.036. ✓

Arithmetic, not iteration. Note `m` currently carries `Assumed` provenance, so **every
configuration's range inherits it**.

### 5.1.1 Sizing around a loadout — the same formula, generalised

The configurator asks a different question: *given what I have already placed, what finishes the
job?* It needs no new maths. With `m_p` the mass of the placed thrusters and `T_p` the thrust they
already deliver here:

```
   n·T·E + T_p  ≥  R·g·(M + m_p + n·m)

              R·g·(M + m_p) − T_p
   n  ≥  ─────────────────────────────
                 T·E − R·g·m
```

That is §5.1 with `M → M + m_p` and the shortfall reduced by thrust already provided. The
denominator — the entire impossibility guard — is untouched. `Core.Loadout` carries the placed set,
`SizingRequest.Placed` defaults to empty, and an empty loadout reproduces v1's numbers exactly
(`AnEmptyLoadoutReproducesTheOriginalAnswer` asserts it, because if that drifts then every figure
the app has ever shown was wrong in one direction).

**Nothing here knows about families.** A partial loadout of one thruster type and a mix of several
are the same computation, which is why mixed compositions needed no second feature (B8).

Two things the generalisation forced into the open:

- **`NetContributionNEach`** — the denominator, surfaced. It is what one more thruster *actually*
  buys after its own weight raises the target. Without it on screen, adding a 100 kN thruster closes
  the gap by ~95 kN and reads as broken arithmetic rather than as physics. It is also the sign test:
  at or below zero no quantity ever works, and adding one makes the shortfall *worse*.
- **The shortfall is clamped at zero.** An over-provisioned loadout needs *none* more, not a
  negative number of them.

`ThrusterSizer.Evaluate` returns the loadout's totals — thrust delivered, mass added, and the
requirement *including* that mass. The requirement therefore rises as the loadout grows, which is
precisely why a budget cannot be computed once and counted down.

### 5.3 Environmental effectiveness

From `ThrustClassesConfiguration.def` (Research §3.3), per class, given air density `d`:

- `MinThrustAirDensity == -1` → `E = 1` always (hydrogen: no falloff).
- Otherwise `E` ramps between `MinThrustAirDensity` (E=0) and `MaxThrustAirDensity` (E=1), clamped.
  **`Min` may be numerically greater than `Max`** — that's how ion is expressed (full thrust at *low*
  density). Interpolate on the interval regardless of ordering; never assume `Min < Max`.
- `WaterOnly` classes excluded unless submerged (not modelled — water unshipped).

Air density comes from the planet's atmosphere geometry: `1.0` up to `ConstantAffectDistance`
(1.08 R), ramping to `0` at `AffectDistance` (1.15 R). v1 evaluates at the surface; the function
takes altitude so Design's v2 slider is free.

Both ramps are assumed linear, flagged for in-game verification (Research §8 Q3). Both are §3.2
named models, so a different shape is a config change plus a `Core` model, not a rewrite.

### 5.4 Block mass

Transcribed from the decompiled engine (Research §4.0), exact:

```csharp
public static float BlockMass(int occupiedCells, float massCurveModifier, float minBlockMass)
    => occupiedCells <= 0
        ? minBlockMass
        : (float)(massCurveModifier * Math.Sqrt(occupiedCells)
                  * Math.Log10(occupiedCells) + minBlockMass);
```

Match the engine's edge cases exactly, and unit-test them:

- **No `Density` → `minBlockMass`.** Not an error, not zero.
- **`V == 1` → `minBlockMass`**, because `log₁₀(1) = 0`. Falls out naturally, but assert it so a
  future "optimisation" doesn't break it.
- **Compute in `double`, cast to `float` at the end** — the engine does, and matching the rounding
  matters when we compare against in-game values.

Provenance is `Derived`: our formula, over `Measured` inputs (`MassCurveModifier` and `MinBlockMass`
from `.def`) plus a recovered `occupiedCells` (§5.5).

### 5.5 Where `V` comes from

`V` (occupied 25 cm cells) is voxelized from physics colliders and cached in binary
`contentcache.vrb` — not in any `.def` (Research §4.0.1). It is stored per block in `gamedata.json`
as a plain integer, so the consumer never touches the cache.

**Producing it went through two stages, and both still matter.** Twelve thruster values were first
recovered by solving the formula against known in-game masses (Research §4.0); the content cache was
then read directly and agreed exactly (§10.2.0), which is what confirmed both the formula and the
recovered values. Today the cache is the source (1,454 blocks) and `OccupiedCellsTable` is the
fallback and cross-check (16 blocks, forced with `--no-engine`).

Keep both. The disagreement between them is what caught the bounding-box-versus-`CellGroups` bug —
a silent 10% mass overstatement on the 5 m tank (Backlog B13).

Why this is stable rather than a hack:

- `V` changes **only if Keen changes a block's collision mesh** — far rarer than a stat retune.
- `MassCurveModifier` and `MinBlockMass` still come from `.def`, so **retuning tracks automatically**.
- A stale `V` is **self-announcing**: computed mass stops matching what the game shows, and `tc verify`
  can check it directly.

Mark the table `Derived` with a note recording how each value was obtained. If a block's `V` is
missing, the app must say "mass unknown for this block" rather than silently substituting zero — an
absent thruster mass would quietly break the self-weight solver (§5.1).

### 5.6 Designed for mixed compositions later

Single-type sizing is closed-form; mixed is a small integer optimisation (minimise added mass subject
to the thrust constraint). Keep the solver a **pure function over a set of thruster types** returning
candidate configurations, so the mixed solver is a sibling sharing the same constraint evaluation —
not a rewrite (Design §3.3).

---

## 6. Frontend

- **Avalonia 12.1.1**, Fluent theme, Inter fonts.
- **`CommunityToolkit.Mvvm`** for observable properties and commands. Nothing heavier until something
  demands it.
- ViewModels depend on `Core` and `Model` only. The producer split makes this structural rather than
  a convention — there is no `GameData` type to accidentally reach for.
- Development runs against a **hand-written `gamedata.json` fixture**, so the GUI is buildable and
  demoable before `Extraction` exists at all. That's now trivially true rather than requiring a fake
  data-source abstraction.
- **No logic in code-behind.** Views bind.
- Calculations are closed-form arithmetic; recompute synchronously on property change. Only the
  producer subprocess is async, with progress.
- **No charting package.** The climb profile draws itself with `DrawingContext` (`ClimbProfileChart`).
  Avalonia ships no chart control; LiveCharts, ScottPlot and OxyPlot all exist, and none is worth a
  dependency for one curve with two reference lines. Revisit if zoom, hover-readout or legends are
  ever wanted.

### 6.1 Numeric input: three rules, learned the hard way

Every numeric field goes through `MassInput` or `CountInput` rather than a bare `NumericUpDown` with
attributes set per usage. Ad-hoc configuration is how all three of these got missed, each on *every*
field at once:

| | |
|---|---|
| **Bound properties must be nullable** | Clearing the text writes `null`. Bound to a non-nullable `int`/`double` the conversion throws and Avalonia paints `System.InvalidCastException` **into the box**. Clearing a number to retype it is ordinary, so the model has to admit the empty state |
| **`ClipValueToMinMax = true`** | Defaults to **false**, so out-of-range input is neither clamped nor rejected — typing `345678` into a 0–9999 field left it reading `3456` |
| **Coerce on `LostFocus`, not on change** | A blank box left behind reads as broken, but coercing the moment the text empties fights the keystroke that produced it. Resolve on the way out |

The first two are silent: nothing logs, nothing fails a test, and the field simply misbehaves. The
view model's own coercion is a belt-and-braces second line, not the fix — the control owns its rules.

**Avalonia has no `OnLostFocus` override to match**; subscribe to the `LostFocus` event instead.

---

## 7. Testing

- `Gui.Tests` — the view models, headless. They touch no Avalonia type, so the whole Plan-mode
  behaviour — load presets, environment effects, ordering, unknown-mass handling — is testable
  without a display or a game. What they cannot catch is a broken *binding*, which is silent in
  Avalonia; for that, run the app with a trace listener attached and confirm it logs no warnings.
- `Model.Tests` — the contract. Reads the committed synthetic fixture and asserts every edge case it
  carries survives: null `thrustClass`, the `-1` no-falloff sentinel, inverted `min > max` ramp
  ordering, null `occupiedCells`, explicit nulls, provenance defaulting, schema-version refusal, and
  a write→read→write fixed point. The schema is the interface between the two halves, so it gets its
  own suite rather than riding along in `Core.Tests`.
- `Core.Tests` — the math (§5). Pure functions, fast. Where correctness lives.
- `Extraction.Tests` — parsing, against **synthetic fixtures** (§7.1).
- All three green on a clean clone with no SE2 installed.
- `tc verify` — on-demand invariant checks against a real local install. The canary for "the game
  patched and broke our assumptions," available without committing Keen's files. It checks **two
  levels**, and the second is the one that earns its keep:
  - *the raw definitions* — thrusters found, all thrust > 0, every thruster pairs with its block;
  - *the extracted config* — every thruster resolves its consumed resource, density and cell count;
    every tank resolves its resource; every reference lands in the table it names; no GUID leaks.

  The split matters because **an inherited field is present in the raw data and absent from the
  config when resolution breaks**, so only a check on the output can see it. That is not
  hypothetical: `ConsumedResource.Type` is inherited by most thrusters, was read without walking the
  base chain, and silently vanished for 8 of 12 — no exception, no warning, just an empty column.

### 7.1 Fixtures: synthetic and committed, real data never

Neither Keen's `.def` files nor a real `gamedata.json` is committed. That collides with "tests green
on a clean clone / on CI," and the resolution is the same on both sides of the split:

| Kind | Committed? | Purpose |
|---|---|---|
| **Synthetic `.def` fixtures** — hand-written JSON in the real envelope shape, our own GUIDs and numbers | **Yes** | `Extraction.Tests`: envelope handling, `$Type` dispatch, missing `ThrustClass`, malformed files, unknown types |
| **Synthetic `gamedata.json`** — small, obviously-fake, hand-authored | **Yes** | `Core.Tests` + GUI development. The consumer's whole world |
| **Real `.def` / real `gamedata.json`** | **No** — gitignored | Local verification only; tests skip when absent |

**Synthetic, not "old or modified real."** The instinct to commit a stale real config for testing is
right in spirit but wrong in detail, for four reasons:

1. **Still Keen's numbers.** Age doesn't change the redistribution question — it just makes it less
   defensible, not more.
2. **Non-deterministic tests.** A real config is a snapshot of a moving game. Tests asserting
   "18 thrusters needed" would break on a retune that has nothing to do with our code.
3. **Can't cover what we need.** The interesting cases aren't in real data: a thruster that *can't
   lift its own weight* (§5.1's impossibility guard), a fifth thrust class, a planet with no gravity
   entry, `Min > Max` ramp ordering, a truncated file. Synthetic fixtures encode all of these.
4. **Someone will mistake it for real.** A file called `gamedata.json` in the repo *will* eventually
   be shipped or trusted by accident.

So make it structurally unmistakable:

- Name it `tests/fixtures/synthetic-gamedata.json` — **not** `gamedata.json`, so the `.gitignore`
  rule for the real file can't accidentally cover it, and nobody confuses the two.
- Give it a sentinel `"gameBuild": "0.0.0-synthetic"` and `"provenance": "synthetic"` at the root.
- Round, obviously-invented numbers (thrust `100000`, mass `1000`) — never real values.
- Add a CI check that fails if any file matching the real-config shape lands in the repo.

Small enough to read in one screen: three or four thrusters, two planets, one container, one tank.

### 7.1.1 What CI can and cannot catch

Worth being explicit, because it's the real cost of this decision:

| | Covered by CI? |
|---|---|
| Math correctness (§5) | ✅ Synthetic config, fully deterministic |
| Parser tolerance (§7.2) | ✅ Synthetic `.def` fixtures |
| Schema round-trip, `Model` serialization | ✅ |
| Fixture still matches current schema | ✅ — validate on every run, catches schema drift |
| **"Keen changed the game data format"** | ❌ **Never.** No SE2 on the runner |

That last row is a genuine gap and no fixture strategy closes it. The mitigation is human and
manual: on patch day, run `tc dump-schemas` and diff against the previous output, plus `tc verify`
for the invariant checks (§7). Budget for that as a routine maintenance action rather than hoping CI
will catch it.

### 7.1.2 The pipelines

`.github/workflows/ci.yml` and `.gitlab-ci.yml` run the same three things:

| Job | Where | Why |
|---|---|---|
| **Test** | Linux | `Model`, `Core`, `Extraction` and `Gui` tests. All plain `net9.0` and need no game — which is the producer/consumer split paying out (§1) |
| **Build all** | Windows | `Engine` and `Cli` are `net9.0-windows`, so *nothing* on Linux compiles them. Without this job they rot unnoticed until someone runs `tc` by hand |
| **No game data** | Linux | `scripts/check-no-game-data.sh` |

Two details that are easy to get wrong:

- **Test individual projects, not the solution.** `dotnet test` on `ThrusterCalculatorSE2.slnx`
  fails on Linux, because restoring it pulls in the two Windows-targeted projects.
- **The data guard is a script, not YAML.** Both pipelines call the same file, so the check that
  actually matters cannot drift between them — and it can be run locally before committing.

`.gitignore` is advisory: `git add -f`, a well-meaning rule edit, or a tool writing into a tracked
directory all bypass it silently. The script is the part that fails loudly, and it is verified to
fail — not merely to pass — against a committed `gamedata.json` and a stray `.def`.

GitLab's Windows build is opt-in (`RUN_WINDOWS_BUILD`), because shared Windows runners are not
available on every gitlab.com plan and a job that can never be scheduled would fail every pipeline.

### 7.1.3 Branches, and what publishes

| Branch | What happens |
|---|---|
| **`dev`** | Work lands here. CI tests and build-checks it |
| **`main`** | What ships. Merging builds the release |

**The version is `<Version>` in `src/Directory.Build.props`, and it is the only switch.** Pushing to
`main` reads it, and publishes only if no release of that version exists yet — so merging to `main`
is safe and repeatable, while *shipping* stays the deliberate act of bumping one line. A re-run with
no bump is a notice, not a failure.

That same property stamps the assemblies, so `generator.version` in a `gamedata.json` traces back to
the build that produced it.

Two artifacts ship, both portable zips, neither containing game data (§3.4):

| | Size | Needs |
|---|---|---|
| self-contained | ~260 MB | nothing |
| runtime-dependent | ~30 MB | .NET 9 **Desktop** runtime — `tc.exe` carries WPF and WinForms |

**Both are published single-file**, so a release folder is two executables and one sample config
rather than 45 or 514 loose DLLs. That is a usability decision, not a technical one: a user opening a directory
of 514 files cannot tell which starts the app.

It costs the self-contained build ~60 MB, because single-file means each executable carries its own
runtime instead of the two sharing one folder. Free for the runtime-dependent build.

Where the size actually goes, for anyone tempted to attack it:

| | |
|---|---|
| ~18 MB native | Skia, ANGLE, HarfBuzz — Avalonia renders every pixel itself, so it ships a graphics engine |
| ~51 MB | WPF + WinForms, which **the GUI never touches**. Present only because `tc.exe` hosts SE2's assemblies and they bind `Microsoft.WindowsDesktop.App` |
| the rest | base runtime and BCL |

**Splitting them into separate folders to shed the 51 MB does not work** — each self-contained app
then carries its own ~120 MB runtime, and the total goes up, not down. `PublishTrimmed` would cut
more, but the Engine reflects into assemblies the trimmer cannot see and Avalonia is reflection-heavy
too: the realistic outcome is a build that passes CI and fails on a user's machine, which is exactly
what the `UseWindowsForms` bug already demonstrated.

**Debug symbols are embedded, not shipped** (`DebugType=embedded`). Loose `.pdb` files were most of
the download — `libSkiaSharp.pdb` alone is 80 MB — and packaging deletes every one. Ours live inside
the assemblies instead, because a user's install is the one thing that cannot be reproduced here, so
a pasted stack trace with line numbers is worth ~100 KB.

### 7.2 Tolerant parsing is a hard requirement

Alpha game, 17 172 files, schemas stamped from a dozen builds in one install (Research §2.3):

- **Key off `$Type`, never filename.** Research §3 caught this — hydrogen thrusters live in
  `*_HydrogenThrusterDefinition.def` but carry the standard `ThrusterDefinitionObjectBuilder` type.
  Filename dispatch would silently drop a third of the catalogue.
- **Unknown `$Type` → skip silently.** We consume a handful of hundreds.
- **Unknown field on a known type → ignore.** Forward compatibility.
- **Missing expected field → `null` + recorded warning**, not an exception. `ThrustClass` is already
  legitimately absent on hydrogen.
- **A malformed file must never abort extraction.**

The failure mode to design against is *silent* loss — dropping a thruster family and producing a
confident wrong config. So the artifact records **counts per `$Type`**, and the UI surfaces
"12 thrusters found": a drop to 8 after a patch becomes visible.

### 7.2.1 Joining a block's definitions

A block's data is split across files that do not reference each other. The join is its
`EntityCompositeDefinition`, which lists the component definitions forming the entity
(Research.md §3.5) — the engine's own mechanism, not a guess about folder layout.

`BlockCompositionIndex.FindSibling` implements it, and `tc verify` asserts every thruster still
pairs, so a restructuring by Keen fails loudly instead of the app quietly losing every thruster's
mass and name.

**Deliberately no fallback.** Matching by shared directory was considered and rejected on the same
principle as the unknown-model-kind exception (§3.2): a weaker method silently substituting for the
real one masks the breakage worth knowing about, and could mispair outright if two blocks shared a
folder. A miss returns `null` and becomes a recorded warning. An earlier draft of this document
proposed same-directory matching as the primary method; the composite graph is strictly better and
supersedes it.

### 7.2.2 Template inheritance — read the parent pointer, never infer it

Concrete blocks routinely omit fields and inherit them from base definitions under `Templates/`.
This is not an edge case: hydrogen thrusters inherit `ThrustClass`, **seven of twelve thrusters
inherit `Density`**, no cargo container declares one at all, and most planets inherit their
atmosphere geometry. Without resolving inheritance, none of those values exist.

**The parent pointer is `BaseGuid`, and it is not in the `.def` files** — it lives in
`definitionsets.vrb` under `DefinitionLoadingData` (Research §4.4.1). 9,142 of 17,196 definitions
declare one. `Engine.DefinitionSetInheritance` reads it; `Extraction` consumes it through
`IDefinitionInheritance`, so a run without the engine degrades to no inheritance rather than to a
guess. Resolution is then trivial: read the field, and if absent follow `BaseGuid` and repeat
(`GameDataExtractor.InheritedString` / `BaseChain`, capped at 16 links against a cycle).

#### The two rules that came before it, and why they are gone

Both **inferred** the parent from component-slot signatures, because the real pointer had not been
found yet. Both are deleted. `BlockComposition.SlotSignature` survives as a descriptive field only.

| Rule | Result |
|---|---|
| Exact slot-set equality | Missed blocks carrying extra components — the 5 m and 10 m atmospheric thrusters silently lost their density |
| All subset matches as equals | Too many templates matched and disagreed; unanimity then resolved *nothing* (7 densities and 4 classes lost) |
| Slot containment, most specific wins | Thrusters and tanks resolved; containers unresolved and warned |
| Overlap ≥ 75%, best match wins | Containers resolved — **to the wrong template**. Tanks silently became Mostly Solid (20) instead of Mostly Hollow (11), breaking three masses that had matched exactly, **with zero warnings** |

The last row is the one to remember: the only case in this project where a heuristic produced
confident wrong numbers *and* suppressed the warning that would have exposed them, because the
matcher believed it had succeeded. It was caught only by re-running the known-mass regression.

**Rule for anything similar: if the engine has an explicit pointer, find it. A shape-based guess
that "mostly works" is worse than no answer, because no answer warns.**

### 7.3 Shallow delta decoding

Planet data is delta-encoded (Research §2.4, §5.2). We do **not** implement engine inheritance
semantics. The payloads sit inline in the `Changed` array as objects carrying their own `$Type`, so a
shallow reader — scan `Changed`, match `$Type`, take the fields — extracts `GravityGenerator` and
`AtmosphereGenerator` without the full machinery. That's how Research §5.2's numbers were obtained.
Hold that boundary.

Block templates need only simple fallback: field absent → look at the
`Templates\Blocks\BaseDefinitions\` parent (Research §2.4).

---

## 8. Sequencing

1. **`tc dump-schemas`** — walk all `.def`, group by `$Type`, emit distinct field sets. Answers
   Research §8 Q6–Q8 at once and becomes the **patch-diffing tool** for every future update.
2. **`Model`** — design the schema (§3). It's the contract; do it before either side.
3. **`Core`** — domain, effectiveness curve, sizing solver (§5), against a hand-written JSON. Tests.
4. **`Extraction` + `tc extract`** — produce a real `gamedata.json`. Commit the first output.
5. **`Gui`** — against the committed JSON. Validates the split end-to-end.
6. Rebuild button wiring: GUI spawns `tc.exe`, progress, staleness banner.
7. Then: mixed compositions, altitude slider, Check mode, web target.

**No unresolved research blockers remain.** The mass spike that used to be step 7 is done
(§10.2.1) — its output is 3 lines of arithmetic in step 3 and an integer table in step 4.

---

## 9. Web host later — what it requires

The producer/consumer split makes this genuinely reachable rather than aspirational. To keep it open,
two rules, both cheap to follow now and expensive to retrofit:

1. **`Model` and `Core` stay platform-neutral.** No Windows TFM, no `System.IO` in the calculation
   path, no P/Invoke. Anything needing the filesystem belongs in the producer.
2. **The consumer treats the config as an opaque asset**, not a file path — load from a stream. Then
   "read from disk" and "fetch over HTTP" are the same code.

Given those, options later: Avalonia targets WASM via `Avalonia.Browser`, so the *same* GUI could
ship to the web with a different host; or a separate web frontend consuming `Core` compiled to WASM;
or a server-side API. The producer stays a desktop tool — it needs the game install — so the flow
becomes: run `tc` locally, upload or host the JSON, anyone uses the web calculator.

Not committing to any of this now. Just not foreclosing it.

---

## 10. Engine hosting — what it is, and what it would buy

### 10.1 The idea

Load SE2's own .NET assemblies into a process we control and **call the game's code**, rather than
reimplementing it. The game is managed C#, ships its assemblies, and is **not obfuscated**.

Needed for exactly one thing: **block mass**. Everything else is plain JSON (Research §3.3, §4.3).

### 10.2 What the game actually does — verified by metadata inspection

Via `MetadataLoadContext` over `Game2.Simulation.dll` (metadata only, no code executed):

```
CubeBlockDefinition
    [prop]   Single                     Mass                ← computed, never serialized
    [prop]   CubeBlockMassConfiguration MassConfiguration   ← MinBlockMass = 5
    [prop]   BlockSizeDefinition        RelativeBlockSize
    [method] void                       ComputeMassAndHP()  ← instance, void

CubeBlockDensityDefinition
    [prop]   Single                     MassCurveModifier   ← 7 / 11 / 20 / 35
```

Two consequences:

1. **`Mass` has no backing `.def` field** — confirming the grep wasn't a search failure.
2. **`ComputeMassAndHP()` is an instance method returning `void`**, not a pure
   `ComputeMass(size, modifier)`. Calling it needs a fully constructed `CubeBlockDefinition`, i.e.
   the engine's definition-loading pipeline — the machinery `BlueprintHelperSE2` already built
   (`Se2Runtime.cs`, `AbstractDefinitionActivators.cs`). Copying from it (§11) matters more than it
   first appeared.

Inputs are few and visible: `RelativeBlockSize`, `MassCurveModifier`, `MinBlockMass`.

### 10.2.0 Revisited — engine hosting is now worth doing, for a different reason

§10.2.1 below concluded that transcribing the mass formula removed any need to host game assemblies.
That conclusion was right about the *formula* and is unchanged. But a later spike (Research §4.0.0)
showed engine hosting buys something the formula does not: **`contentcache.vrb` yields the game's
precomputed occupancy for 1,454 blocks**, verified to agree exactly with our recovered values.

That replaces `OccupiedCellsTable` — the project's one hand-maintained input — with extracted data,
and extends coverage from 16 blocks to every block. `../BlueprintHelperSE2` already has the working
stack (`Se2Runtime`, `VrbSerializer`, `ContentCache`) to copy, per §11's copy-don't-share decision.

Scope of the change:

- **Create `ThrusterCalculator.Engine`** (net9.0-windows, `UseWPF`), the reserved slot in §2.3.
- `Cli` references it and moves to `net9.0-windows`. The producer already requires a game install, so
  this costs nothing it was not already paying.
- **`Model`, `Core` and `Gui` are untouched** and stay platform-neutral. The consumer still needs
  nothing but JSON, and the §4 rule that game assemblies never enter the Avalonia process still
  holds — the CLI is not an Avalonia app.
- Occupancy stays `Derived` in the config only until this lands; afterwards it is `Measured`.

**Implemented.** `tc extract` now reports `Occupancy source: content-cache`, and all 46 extracted
blocks carry `measured` occupancy where 16 previously carried `derived`.

Two things worth recording from building it:

- **The occupancy bounding box is not the cell count.** The first implementation read
  `Occupancy.Bounds`, which is right only for blocks that are a single box. The 5 m hydrogen tank
  occupies 1,820 cells inside a 20×10×10 = 2,000 box — a 10% mass overstatement. `ComputeMassAndHP`
  sums `CellGroups`, so we must too. This was caught purely because the recovered table disagreed;
  with no second source it would have shipped.
- **The table stays, as a fallback and a cross-check.** `--no-engine` forces it (16 of 46 blocks),
  which is how the two sources get compared. Three of its entries were corrected from the cache by
  1–2 cells, all on the largest blocks, where a mass published to the whole kilogram cannot resolve
  individual cells.

### 10.2.1 RESOLVED for the *formula* — superseded for the *project*

> **Read §10.2.0 first.** Its conclusion about the mass formula stands unchanged and is why `Core`
> has no game dependency. Its conclusion that `Engine` need not exist is **obsolete** — the project
> hosts the engine today, for occupancy and inheritance rather than for mass.

The spike is done (Research §4.0). `ComputeMassAndHP()` was decompiled, the formula is three lines,
and all twelve thruster `V` values were recovered as exact integers. **Transcribe wins outright** for
the mass curve — the comparison in §10.3 is kept for the record, but that decision is made and has
not changed.

What *did* change is that two later findings needed the engine anyway:

| Finding | What it buys | Where |
|---|---|---|
| `contentcache.vrb` holds precomputed occupancy | `V` for 1,454 blocks instead of a 16-entry hand table | §10.2.0 |
| `definitionsets.vrb` holds `BaseGuid` | The real parent pointer, replacing two failed shape heuristics | §7.2.2 |

So `Engine` **is created** and `Cli` targets `net9.0-windows`. The consumer half is untouched: the
producer needs a game install (it always did), and `Model`, `Core` and `Gui` still need nothing but
JSON. The §4 rule that game assemblies never enter the Avalonia process still holds — the CLI is not
an Avalonia app, and the GUI reaches it by spawning a process, never by reference.

Still-open escalation path for later features: `VRage.Voxels.SurfaceGravity` (planet gravity
magnitude, Research §5.3) and `.vrb` blueprint decoding (Check mode, Backlog B9). Neither is in v1,
but both are now much cheaper — the hosting stack they need already exists.

### 10.3 The comparison that led there

| | Engine hosting | **Transcribe formula** | Empirical fit |
|---|---|---|---|
| Accuracy | Exact | Exact | Approximate |
| Runtime dependency | SE2 install, Windows | **None** | None |
| Testable in CI | No | **Yes** | Yes |
| Survives a retune | **Automatic** | Needs re-check | Full re-measurement |
| Effort | High | **Low** | Medium |

Transcribe won, and took about an hour rather than the estimated few (§10.2.1).

Note the producer/consumer split had already softened the "survives a retune" column:
`MassCurveModifier` and `MinBlockMass` are read from `.def` by the producer, so retuned values track
automatically under any option. Only a change to the formula's *shape* — or to a block's collision
mesh — needs work, and both are detectable (§5.5).

### 10.4 Incidental finding — corrected

`ThrusterDefinition` exposes a **`ThrustDirection`** property. An earlier draft said it appears in no
`.def` file; running `tc dump-schemas` against the real install showed otherwise — it is present on
**2 of 14** thruster definitions, both base templates, with the value `"Forward"`. Concrete blocks
inherit it. Not needed for Plan mode; it's the field per-axis analysis will want in Check mode.

Worth noting *how* that was corrected: the schema dump was built precisely so claims about the data
stop depending on how thorough a manual grep happened to be. It earned its keep on first run.

---

## 11. Settled decisions

- **CLI name is `tc`** (`AssemblyName`, so the binary is `tc.exe`) — no conflict on the dev machine.
- **`Engine` and `Cli` target `net9.0-windows`; everything else targets plain `net9.0`** (§2). Only
  `Engine` loads SE2's assemblies, and `Cli` inherits the TFM by referencing it. `Extraction` meets
  `Engine` at `IOccupancySource` / `IDefinitionInheritance` and never references it.
- **Inheritance comes from `BaseGuid`, never from a shape heuristic** (§7.2.2). Two slot-signature
  rules were tried and deleted; one of them produced confident wrong masses with no warning.
- **Avalonia 12.1.1**, with `AvaloniaUI.DiagnosticsSupport` 2.2.3 for dev tools — the old
  `Avalonia.Diagnostics` package stops at 11.x and does not exist for Avalonia 12.
- **Producer/consumer split** with `gamedata.json` as the contract (§1). The consumer needs nothing
  but the JSON.
- **Named-model parameterisation** for calculation models, not embedded formula strings (§3.2).
- **Copy from the sibling project**, don't extract a shared package. Two small projects don't justify
  the coupling; revisit if a third appears.
- **Nothing derived from Keen's data is ever committed** — neither `.def` files nor `gamedata.json`.
  Tests run on **synthetic** fixtures, deliberately named so they can't be confused with real data
  (§7.1).
- **Three distribution paths** (§3.4): web users get a server-hosted config, desktop binary users get
  one generated on first run by the bundled `tc.exe`, power users run `tc extract`. **Releases ship
  no `gamedata.json` at all**, which is what makes packaging fully automatable — CI builds code,
  never data (§3.5).
- **The desktop GUI keeps a thin rebuild affordance** that shells out to `tc.exe` (Design §4.5.1) —
  desktop-only, absent from the web build, degrading to an explanation rather than a dead button.
- **No wiki cross-check command.** Hand-maintained, slow to update, demonstrably error-prone
  (Research §3.1 found an entire column copy-pasted wrong). Its mass table stays in the research
  notes as a one-off sanity check and becomes irrelevant once the mass curve lands.

## 12. Open technical questions

1. ~~Mass formula shape~~ — **resolved** (§10.2.1, Research §4.0).
2. ~~Schema design details~~ — **resolved**; the contract is [Schema.md](Schema.md) and `Model`
   implements it.
3. ~~Recover `V` for cargo containers and tanks~~ — **resolved**, and better than planned: the
   content cache supplies it for every block rather than a handful solved by hand (§10.2.0).
4. **Web hosting specifics** — deferred entirely, but §3.5 option 2 (binary fetches config from the
   same host on first run) becomes attractive the moment a host exists. Don't design for it yet;
   don't foreclose it either.

Everything else that is knowingly unfinished lives in [Backlog.md](Backlog.md), which is the single
list to read before starting work.
