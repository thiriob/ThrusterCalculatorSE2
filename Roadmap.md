# Roadmap

Where the project goes after v1. Companion to [Backlog.md](Backlog.md), which records what is
knowingly imprecise *today*; this file is what to build next and in what order.

**Shape of the releases.** v1 was the core and had to be complete to be worth anything. Everything
after it can be thinner — one idea per version, shipped when it works.

| | | |
|---|---|---|
| **v1** | The calculator | Shipped (`v0.2.0`) |
| **v2** | The configurator | Largely built |
| **v3** | The climb | Designed; mocked up in the app |
| **later** | Reading real ships | Needs `.vrb` grid decoding |

---

## v1 — the calculator ✅

Plan mode: departure planet plus ship mass in, thruster loadouts out. Every number extracted from
the installed game, a producer that regenerates it on demand, an automated release.

The parts worth remembering: thruster self-weight is a fixed point with a closed form (Technic §5.1),
mass comes from the game's own formula (Research §4.0), and surface gravity is stated in the
definitions after all (Research §5.3).

---

## v2 — the configurator

**Deliberately thin.** One idea: stop answering *"if you used only this thruster"* and start
answering *"given what I have placed, what finishes the job?"*.

### Mixed compositions (B8) — built

`Core.Loadout` carries what the user has placed; the sizing formula takes it as `M → M + m_p` with
the shortfall reduced by thrust already provided. **Single-family and mixed-family are the same
computation** — nothing in the solver distinguishes them, which is why "mixed types" needed no
second feature and no second code path.

The UI is three columns: inputs, the configurator, and two reference tables ("if you use one type",
"to cover what's left") that are one component rendered twice with a different requirement.

The figure that makes it honest is `NetContributionNEach` — what one more thruster *actually* buys
after its own weight raises the target. Without it on screen, adding a 100 kN thruster closes the
gap by 95 kN and reads as broken arithmetic.

### Still open for v2

- **B2 — the 2% on large blocks.** The 7.5 m container and 5 m tank disagree with measured mass in
  opposite directions. The only wrong number left on screen.
- **Polish and testing** of the configurator against real ships.

### Candidate, not committed: a power budget

Every loadout reports its draw (kW or L/s) but nothing says whether the ship can *supply* it. The
data is extracted already; what is missing is a reactor and battery model. Natural next question
after "what do I bolt on", but it is a new subsystem, not a finishing touch.

---

## v3 — the climb

**One idea: altitude.** v1 and v2 both answer "will it leave the ground". v3 answers "will it get
where I am going", which is a different question more often than it sounds.

### Why it matters

Four of twelve thrusters — the whole ion family — can currently only ever appear as a rejection row,
because at sea level on an atmospheric world they produce nothing. Altitude is what makes them the
*best* option rather than dead weight.

More importantly it catches a real failure the app is blind to. On Verdure the air starts thinning
**at ground level** (`constantAffectDistance = 1.0`), atmospheric thrust reaches zero at air density
0.2, and gravity is still around half its surface value there. So a ship can lift off comfortably on
pure atmospheric thrust and **be unable to leave the atmosphere** — it climbs, slows, and stops. That
is the vertical-axis twin of the failure Design §3.2 exists to prevent.

It is also what makes mixing *mean* something: at one altitude, mixing is a cost-and-mass trade. The
actual reason to mix is that different thrusters work at different heights.

### The shape of it

- **A ceiling**, as a plain figure: *"reaches the atmosphere edge — stalls there"*. No new inputs.
  The same honesty as the covered-mass range: not a yes/no, but the boundary of what a loadout buys.
- **A climb profile**, **altitude on the vertical axis** because the reader is following a climb.
  Mocked up in the app today, drawn natively with `DrawingContext` (no charting package — Avalonia
  has none, and one curve does not justify LiveCharts or ScottPlot).
- **Spare acceleration, not thrust-to-weight**, on the horizontal axis. TWR is the right question
  beside a planet and a meaningless one away from it: weight tends to zero out of the gravity well,
  so *every* ship's ratio runs to infinity and a nimble ship reads identically to a sluggish one.
  Subtracting gravity instead of dividing by it — `thrust ÷ mass − gravity`, in m/s² — stays finite
  and keeps meaning something: zero is the hard floor, a dip below it is the stall, and the value it
  settles at up top is exactly how briskly the ship accelerates in space.
  **Consequence:** the target margin becomes a *curve*, not a line, because a target of 1.5 means
  "half a gravity spare" and half a gravity shrinks as you climb.

TWR remains the right *input* — "how much margin do I want at lift-off" is a ratio question — so the
spinner stays. Input and output simply want different units here.
- **Named heights, not radii.** Ground / atmosphere edge / space. Planet radii mean nothing to a
  player, and naming them sidesteps the radius problem below entirely.

### What has to land first

1. **Extract the gravity falloff.** `GravityGenerator` already carries `AccelerationDistance`,
   `AffectDistance`, `FallOffPower` and `GravityShape` beside the surface gravity we read
   (Research §5.3). A few lines in `ReadPlanetGeometry` plus schema fields — left out of v1 so the
   config carried nothing unused.
2. **Verify both ramps in game (B6).** Air density versus altitude, and thrust versus air density,
   are *assumed linear*. At the surface both clamp, so the assumption is free; the moment the curve
   is drawn, every point on it is an unchecked interpolation presented as fact. **A smooth line is a
   confident-looking artefact** — this is the gate, not the code.
3. **B4** — the legacy planets' 100-radii atmosphere only starts to matter here.

**Planet radius stays open**, and does not block v3: it is needed only to express altitude in
kilometres rather than named bands. If it is ever wanted, the lead is the shipped scenario worlds
under `GameData\Vanilla\Worlds\` — real saves, and `Engine` already reads two other `.vrb` files.

---

## Later — reading real ships

Everything here needs `.vrb` **grid** decoding, so they arrive together.

- **Check mode (B9)** — load a blueprint and analyse what is on it, rather than describing it.
  `Engine` already hosts the game's assemblies and `../BlueprintHelperSE2` has solved the grid
  format, so this is porting rather than research. Design §7 asks that Plan mode pre-fill Check
  mode's inputs, making it additive rather than a second app.
- **Six-axis (B11)** — falls out of Check mode: a real grid says where thrusters point, and
  `ThrusterDefinition.ThrustDirection` exists for exactly this.
- **Torque and centre of mass (B10)** — the big one. It changes the *input model*: thrusters stop
  being a bag per axis and become things with positions. Only sensible once Check mode supplies real
  placements; asking a user to type them would be absurd.

---

## Parallel track: the web build

Not tied to a version. The producer/consumer split was built for it (Technic §9): `Model` and `Core`
are platform-neutral by construction and the consumer needs nothing but a JSON file.

What it needs is a **host**, not a rewrite — plus a decision about who generates the config the
server serves, since the licence position is that we do not redistribute Keen's numbers
(`License.md`). That question is the real work, and it is a policy question rather than a technical
one.

---

## Waiting on Keen, not on us

Nothing to build; these resolve themselves when the game ships the content.

| | |
|---|---|
| **Underwater thrusters (B15)** | Art exists, no definitions. `ThrustClassesConfiguration` already defines the `Water` class. They appear on their own when water ships |
| **Water / VS3** | Brings Byblos, submersion, and the `WaterOnly` thrust class the solver already rejects |
| **Geomeles (B1)** | No atmosphere anywhere in its chain; not a playable planet yet |

The right response to all three is `tc extract` after the patch, then check whether the warning
cleared on its own.

---

## Continuous, every version

- **Patch day is a routine, not an event.** `tc dump-schemas` diffed against the previous output,
  then `tc verify`. CI can never catch "Keen changed the data format" — no runner has the game
  (Technic §7.1.1) — so this is the human half of the safety net.
- **Keep `OccupiedCellsTable` (B13).** Superseded by the content cache and kept deliberately as an
  independent cross-check; it is what caught the bounding-box-versus-cell-groups bug. If the two
  disagree again, trust the cache and treat the disagreement as a signal.
- **B12a** — `tc.exe` sits beside the GUI in a release but not in a dev build, so Rebuild explains
  itself instead of working locally. Deliberate; revisit only if it becomes annoying.
