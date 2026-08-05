# Roadmap

Where the project goes after v1. Companion to [Backlog.md](Backlog.md), which records what is
knowingly imprecise *today*; this file is what to build next and in what order.

**v1 shipped** (`v0.2.0`): Plan mode — departure planet plus ship mass in, thruster loadouts out —
with every number extracted from the installed game, a producer that regenerates it on demand, and
an automated release.

---

## v2 — "Will it get there?", not just "will it lift?"

v1 answers the lift-off question. v2 answers the flight.

### The headline: mixed compositions (B8)

One loadout per thruster type is the limitation people will hit first. Real ships fly atmospheric
for the climb and ion once they are out of it, and v1 cannot express that.

The maths is already shaped for it: single-type sizing is closed-form, mixed is a small integer
optimisation over **the same constraint the solver already evaluates** (Technic §5.6). It is an
added function, not a rewrite — that was a deliberate v1 design decision, and v2 is where it pays.

The interesting part is UX, not solving: a mixed answer has to stay comparable against the
single-type rows rather than becoming a separate mode.

### Altitude (B7), and the two things it needs first

The model already takes distance in planet radii; v1 evaluates at the surface because that is the
lift-off case. Three dependencies, in order:

1. **Extract the gravity falloff model.** Confirmed present and unextracted:
   `GravityGenerator` carries `AccelerationDistance`, `AffectDistance`, `FallOffPower` and
   `GravityShape` beside the surface gravity we already read (Research §5.3). A few lines in
   `ReadPlanetGeometry` plus schema fields. Deliberately left out of v1 so the config carries
   nothing unused.
2. **Verify both ramps in game (B6).** Air density versus altitude, and thrust versus air density,
   are both *assumed linear*. Until that is checked, an altitude slider produces confident numbers
   nobody has validated — the exact failure this project exists to avoid.
3. **Planet radius — the open research question.** Every distance in the data is in planet radii, so
   "5 km up" needs `R`, and radius is per-world instance data (Research §5.3).
   **Lead worth chasing:** the shipped scenario worlds under `GameData\Vanilla\Worlds\` are real
   saves, and the `Engine` project already reads two other `.vrb` files. Reading planet radii out of
   `savegame.vrb` would make altitude real rather than expressed in radii.
   Fallback: a user-supplied radius, exactly as gravity is overridden today.

B4 (legacy planets inheriting a 100-radii atmosphere) only starts to matter here.

### Correctness: the 2% on large blocks (B2)

The 7.5 m container and 5 m tank disagree with measured mass by ~2%, **in opposite directions**.
Small against their cargo capacity, but it is a wrong number on screen and the only one left.
Investigation route is written up in B2.

### Candidate, not yet committed: a power budget

Every loadout reports its draw (kW or L/s), but nothing says whether the ship can *supply* it. The
data is already extracted. It would answer "and what powers this?", which is the natural next
question after "what do I bolt on" — but it needs a reactor/battery model that does not exist yet,
so it is a candidate rather than a plan.

---

## v3 — "Analyse what I built"

v2 is still a calculator you describe a ship to. v3 reads the ship.

### Check mode (B9)

Load a blueprint and analyse what is actually on it. The remaining work is `.vrb` **grid** decoding
— `Engine` already hosts the game's assemblies, and `../BlueprintHelperSE2` has solved the grid
format, so this is porting rather than research.

Design §7 asks that Plan mode's screen pre-fill Check mode's inputs, so it is additive rather than a
second app.

### Six-axis analysis (B11)

Falls out of Check mode: a real grid says where thrusters actually point, and
`ThrusterDefinition.ThrustDirection` exists for exactly this (Technic §10.4). Plan mode answers
"up"; handling needs the other five.

### Torque and centre of mass (B10)

The big one, and explicitly out of scope until now: it changes the *input model*, not just the
maths. Thrusters stop being a bag per axis and become things with positions. Only sensible once
Check mode supplies real placements — asking a user to type them would be absurd.

---

## Parallel track: the web build

Not tied to a version. The producer/consumer split was built for this (Technic §9): `Model` and
`Core` are platform-neutral by construction and the consumer needs nothing but a JSON file.

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
| **Geomeles (B1)** | No atmosphere anywhere in its chain; it is not a playable planet yet |

The right response to all three is `tc extract` after the patch, then check whether the warning
cleared on its own.

---

## Continuous, every version

- **Patch day is a routine, not an event.** `tc dump-schemas` diffed against the previous output,
  then `tc verify`. CI can never catch "Keen changed the data format" — no runner has the game
  (Technic §7.1.1) — so this is the human half of the safety net.
- **Keep `OccupiedCellsTable` (B13).** It is superseded by the content cache and kept deliberately as
  an independent cross-check; it is what caught the bounding-box-versus-cell-groups bug. If the two
  disagree again, trust the cache and treat the disagreement as a signal.
- **B12a** — `tc.exe` sits beside the GUI in a release but not in a dev build, so Rebuild explains
  itself instead of working locally. Deliberate; revisit only if it becomes annoying.
