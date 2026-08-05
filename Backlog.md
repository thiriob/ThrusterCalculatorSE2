# Backlog — deferred decisions and known gaps

Things we have deliberately **not** resolved, with enough context to pick each up cold. This is not
a wish list of features; it is the set of places where the app is knowingly imprecise or incomplete,
so none of them can quietly become folklore.

Companion to [Research.md](Research.md), [Design.md](Design.md), [Technic.md](Technic.md) and
[Schema.md](Schema.md).

**Rule for this file:** an entry earns its place by being *visible in the product* — a warning in the
extracted config, a marked value in the UI, or a stated limitation. If it is invisible to the user,
it is a bug, not a backlog item.

---

## Data gaps

### B1 — Geomeles has no atmosphere geometry

**Status:** extracted as `atmosphere: null`, provenance `unknown`, warning `unknownAtmosphere`.

Geomeles is the one planet with no atmosphere generator anywhere in its inheritance chain. Every
other planet either states its own geometry or inherits it (Research §4.4.2).

We previously substituted a standard shape (1.08 / 1.15). That has been **removed**: the only planet
reaching that path is one that is not in the game yet, so a fabricated atmosphere would be a guess
about content nobody can check. Unknown-and-warned is the honest state.

**Revisit when:** Geomeles ships in a playable milestone. Re-run `tc extract` and check whether the
warning clears on its own — if Keen adds the planet properly it will simply resolve.

**Consequence today:** `null` reads as *airless* to the calculator, so atmospheric thrusters report
"no thrust" on Geomeles. Visible, not silent, and the planet is unreachable in game regardless.

### B2 — Large blocks disagree with measured mass by about 2 %

**Status:** cell counts come from the game's own content cache; small blocks match exactly, the two
largest measured blocks do not.

| Block | V (cache) | Computed | Measured | |
|---|---:|---:|---:|---:|
| Cargo Container 1.5 m | 216 | 245.2 | 245 | −0.1 % |
| Cargo Container 2.5 m | 1 000 | 669.1 | 669 | −0.0 % |
| Hydrogen Tank 1.5 m | 216 | 382.4 | 382 | −0.1 % |
| **Cargo Container 7.5 m** | 26 912 | 5 092.1 | 4 982 | **−2.2 %** |
| **Hydrogen Tank 5 m** | 1 820 | 1 534.9 | 1 565 | **+2.0 %** |

Two things make this interesting:

- **The errors point in opposite directions**, so it is not a simple systematic bias in the formula
  or a constant offset.
- **The tank case has two independent sources agreeing against the game.** Its `V = 1820` comes from
  the content cache *and* independently from solving the published 1 534.87 kg mass — the two agreed
  exactly. Yet the game shows 1 565 kg, which implies `V ≈ 1877`. So either the published figure and
  the cache share a common origin that the running game does not use, or something else contributes
  ~30 kg on that block.

Both affected blocks are large. Note the 5 m tank readings carry ~10 kg of noise (see B3), which is
well below the 30 kg gap.

**How to investigate:** dump raw `CellGroups` for both blocks and check for overlapping groups —
`TryGetOccupiedCellCount` sums them, mirroring `ComputeMassAndHP`, so overlap would be double-counted
by both. Also worth measuring a third large block to see whether the sign correlates with anything.

**Consequence today:** two blocks are ~2 % out on their own mass. Small against their cargo capacity,
but wrong, and not detectable programmatically — hence this entry rather than a warning.

### B3 — ~~Gas mass~~ RESOLVED: gas is massless

**Closed.** Tank contents contribute nothing; only the tank's own block mass counts. The UI now
states this rather than warning about missing data.

Settled by the right experiment: fit an empty tank, watch it fill, watch the ship mass. It does not
move. Corroborated by a 5 m tank reading **10 223 kg at 100 %** against **10 233 kg at 0 %** — full
is 10 kg *lighter* than empty, i.e. the difference is reading noise, not gas.

**Worth recording how the earlier answer went wrong.** A first attempt differenced two separately
configured ships and produced a confident 202 kg for 8 000 L (~25 kg/m³) — a plausible-looking figure
for compressed hydrogen, which is exactly why it was believable. Differencing two builds picks up
anything else that changed between them; isolating one variable does not. The code search that
followed found no gas mass anywhere — no mass on `Hydrogen.def`, no gas among the 65 items with
`MassPerUnit`, and the resource container not implementing `IDynamicMassProvider` — and that absence
was the correct signal. It was read as "not found yet" when it should have been read as "not there".

### B4 — Legacy planets inherit a 100-radii atmosphere

**Status:** extracted faithfully, warning `implausibleAtmosphere` (3 planets).

MarsLike, Testerran and WaterPlanet declare no atmosphere and inherit from
`Templates/Legacy/PlanetWithAtmosphere`, which says `AffectDistance = 100`.

The resolution is correct — that is genuinely what the engine would do — but 100 planet radii is not
a usable boundary. Surface density is unaffected, which is all v1 uses; only an altitude model would
care. None of the three is playable.

**Revisit when:** an altitude control is built (B7), or these planets are retired.

### B5 — Two blocks declare unbounded inventory

`ContractBlock250` and `TradeTerminal250` declare ~9.22 × 10¹² kg — the engine's fixed-point maximum,
meaning unbounded. Warned as `unboundedCapacity`; they are excluded from the storage picker because
they are not cargo blocks. Harmless unless someone starts summing all inventories.

---

### B14 — ~~Curated gravity table~~ RESOLVED: gravity is extracted

**Closed, and the table is deleted.** `GravityGenerator.GravitationalAcceleration` states surface
gravity in the definitions; all ten planets extract as `measured` (Research §5.3).

Worth recording how the wrong answer nearly shipped. A hand-curated table was built, seeded from an
in-game HUD reading, and defended on the grounds that the value "is not in the files". It was — the
reader took one field off the gravity component and ignored the rest, and the base template that
supplies it encodes its components as a plain array rather than a delta, so the walk reached the
right file and read nothing. The in-game measurement was **verification of an extractable value**,
mistaken for the only available source.

The instinct that caught it was a principle, not evidence: reading the HUD is not in the spirit of
a tool whose premise is that the game files are the source of truth. That was right, and the data
agreed.

### B15 — Underwater thrusters are absent, not greyed out

**Status:** deliberately deferred until the water milestone ships.

Design §4.4 says not-yet-implemented content should be *shown greyed* — "does this exist yet?" is a
real question in alpha — and Schema §4.4 specifies `implemented: false` for exactly that. The
synthetic fixture exercises it; **real extraction emits nothing.** `Blocks\Thrusters\Underwater\
{50,150,250,750}\` holds models and materials but **zero `.def` files**, so the producer never sees
a block to emit.

Implementing it would mean inferring blocks from art folders — inventing catalogue entries from the
absence of data, which is the opposite of how everything else here works.

**Revisit when:** water ships (VS3, the Byblos milestone). The thrusters will then have definitions
and appear on their own with no code change. The one piece already in place is
`ThrustClassesConfiguration`'s `Water` class, `WaterOnly: true`, which `ThrusterSizer` already
rejects as producing no thrust while submersion is unmodelled.

**Consequence today:** the catalogue shows 12 thrusters and the four underwater sizes are simply not
listed. Honest — they do not exist in this build in any usable sense — but it is a stated design
behaviour that is not live, so it is recorded here rather than left to be rediscovered.

## Modelling assumptions not yet verified in game

### B6 — Both effectiveness ramps are assumed linear

`ThrustClassesConfiguration` gives two endpoints per thrust class, and the atmosphere gives two
distances. We interpolate linearly between each pair (Technic §5.3). That is the SE1 behaviour and
what a two-point parameterisation implies, but it is **not confirmed**.

**How to test:** hover at several altitudes on Verdure and compare displayed thrust against the
model. Only matters once altitude is exposed (B7); at the surface both ramps are clamped anyway.

Both are named models in the config, so a different curve is a new `kind` plus a `Core`
implementation — contained by design.

---

## Deferred product scope

### B7 — Altitude, and the two things it needs first

The model already takes distance in planet radii; v1 evaluates at the surface because Plan mode
answers the lift-off question. An altitude slider needs B4 and B6 resolved to mean anything — and
two further things that only became clear once gravity was measured in game (Research §5.3.1):

**The gravity falloff model is already in the data** — it just is not extracted yet, because v1
never leaves the surface. The same component that states surface gravity carries
`AccelerationDistance` (constant out to here), `AffectDistance` (zero beyond), `FallOffPower` (the
exponent, with `-1` as a sentinel exactly like thrust classes) and `GravityShape`. Extracting them
is a few lines in `ReadPlanetGeometry` plus schema fields; **do it when altitude lands, not
before**, so the config carries nothing unused.

In-game readings will still be worth taking as a check: Verdure reads 1.00 g on the ground and
0.33 g near the boundary of space, and an inverse square would give 0.756 at the atmosphere edge —
so whatever the model is, it is much steeper than Newton, and the extracted `FallOffPower` should
say why.

**Altitude needs planet radius, which we do not have.** Every distance in the data is expressed in
planet radii, so turning "5 km up" into `r/R` requires `R` — and radius is per-world instance data
(Research §5.3). A radius input was considered and **deliberately not added**: with v1 evaluating
at the surface, every value a user typed would change no output at all, and a field that visibly
does nothing implies the app is using it. It belongs with this entry, not before it.

**Consequence today:** none. Surface gravity is measured, sea-level air density is 1.0 regardless,
and the sizing answer does not depend on either falloff curve.

### B8 — ~~Mixed thruster compositions~~ RESOLVED: same computation, no second feature

**Closed.** `Core.Loadout` carries what the user has already placed and the solver sizes around it
(Technic §5.1.1). **Nothing in it distinguishes families**, so a partial loadout of one thruster and
a mixed-family loadout are the same arithmetic — "mixed types" needed no separate code path, and
`MixingFamiliesIsTheSameComputation` asserts it.

The v1 design decision that made this cheap was keeping the solver a pure function over a set of
thruster types (Technic §5.6). It cost nothing then and paid here.

What it exposed is that mixing is only half a feature without altitude: at a single height, mixing
is a cost-and-mass trade. The reason to actually mix — different thrusters work at different
heights — needs the climb (Roadmap v3).

### B9 — Check mode

Load a blueprint and analyse what is actually on it, rather than describing it. Needs `.vrb` grid
decoding — the `Engine` project now exists and already hosts the game's assemblies, so the remaining
work is the grid format itself, which `../BlueprintHelperSE2` has already solved.

### B10 — Torque and centre of mass

Explicitly out of scope for v1: thrusters are a bag per axis. Placement effects are a substantially
larger problem and would change the input model, not just the maths.

### B11 — Six-axis analysis

Plan mode answers "up". Lateral axes matter for handling and belong with B9, where a real grid says
where the thrusters actually point — `ThrusterDefinition.ThrustDirection` exists for exactly this.

---

## Housekeeping

### B12a — `tc.exe` is not beside the GUI in a *dev* build

**Status:** resolved for releases, still true when running from `bin/Debug`.

The release workflow publishes the GUI and the CLI into one folder, so Rebuild and the staleness
check work in a packaged build. A dev build has them in separate `bin` directories, so the Data
panel explains itself and names the command instead — a degradation, not a dead button
(Design §4.5.1).

Wiring it for dev would mean a post-build copy of the whole `Cli` output into the GUI's, dragging
`Engine` and the game-hosting stack along on every incremental build. Not worth it to save one
`tc extract` during development.

### B12 — ~~Release packaging needs a manual step~~ RESOLVED: fully automated

**Closed.** `.github/workflows/release.yml` builds the whole release on a tag, with no step on a
machine that has the game.

That was possible only by **dropping the bundled `gamedata.json`**. The earlier plan shipped one,
which forced a manual `tc extract` — and, less comfortably, meant redistributing Keen's numbers,
which `License.md` says this project does not do. The release now ships `tc.exe` instead of the data
it would produce: first run shows the sample banner, one click on Rebuild generates a config from
the user's own install.

Better on three counts, not just convenience: nothing manual, nothing redistributed, and no config
that is stale the day Keen patches.

**Decided: a self-contained portable zip**, not an installer.

Why portable suits *this* app specifically: it is not one binary but a GUI, a CLI and a config the
user is explicitly invited to regenerate, so a visible folder containing all three is an asset
rather than clutter — and the audience already navigates Steam library folders. Uninstalling is
deleting the folder; settings live in `%LocalAppData%` and orphan nothing. An installer would also
make code signing effectively mandatory, since an unsigned installer reads as malware to SmartScreen
far more loudly than an unsigned zip does.

Self-contained over framework-dependent: ~70–150 MB against ~10 MB, which is nothing beside the game
itself, and "install a runtime first" loses people.

**Two frictions to document in the release notes:**

- **Mark-of-the-Web.** A downloaded zip marks its contents blocked; users may need right-click →
  Properties → Unblock on the archive *before* extracting.
- **SmartScreen** still warns on first run of an unsigned executable. Signing is the only real fix
  and costs money annually.

**Revisit an installer** when releases are frequent enough that people miss updates. Velopack
(`vpk`) is the natural route — per-user install, no UAC, auto-update — and does not solve signing.

### B13 — One hand-maintained table remains, deliberately

`OccupiedCellsTable` is now only a fallback for when the game's assemblies cannot be hosted; the
content cache supersedes it and covers ~1 450 blocks against its 16. It is kept deliberately as an
independent cross-check — it is what caught the bounding-box-versus-cell-groups bug — but it will
drift as the game is retuned. If it ever contradicts the cache again, trust the cache and treat the
disagreement as a signal.
