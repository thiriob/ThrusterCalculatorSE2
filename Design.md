# ThrusterCalculator SE2 — Design & UX

Companion to [Research.md](Research.md) (what the game gives us) and [Technic.md](Technic.md) (how
it's built). This document is **what the app does and how it feels to use**.

---

## 1. The problem, stated as a player

You have a ship idea and a departure planet. You want to know:

> *"How much thrust do I need, and what should I actually bolt on?"*

The app answers that in under ten seconds, with numbers **correct for the build of the game currently
installed** — not for the build a wiki was written against.

Research §3.1 is the proof this matters: the community wiki's entire ion-thrust column is a
copy-paste of the atmospheric column, overstating ion thrust by **3.5–5×**. A player sizing an
ion ship from the wiki under-builds by a factor of four and does not fly.

## 2. Design principles

**P1 — The game is the source of truth, and it moves.** Every number traces to a definition file in
the installed game. No hardcoded stat tables. When the game patches, the app shows new numbers with
no code change and no update from us.

**P2 — Be honest about what we don't know.** Three visible tiers, and each value carries its own:

| Tier | Meaning | Treatment |
|---|---|---|
| **Measured** | Read from game files, or computed by the game's own code | Normal |
| **Derived** | Computed by our math from measured values | Normal, with a "how?" affordance |
| **Assumed** | Curated guess or user-entered | Marked, editable inline |

This is load-bearing, not decorative: it's what lets the mass curve start as `Assumed` and later
become `Measured` (Research §4.2) without restructuring the UI.

**P3 — Answer first, detail on demand.** The proposed configurations are the product.

**P4 — Never require the game to be running.** Files on disk only.

**P5 — Degrade, don't block.** A missing capability greys out one panel; it never prevents launch.

---

## 3. Mode: Plan (v1)

Confirmed as the v1 scope. Flow:

```
   ① departure planet  ─┐
                        ├─▶  required thrust  ─▶  ③ proposed configurations
   ② ship mass  ────────┘                            (one per thruster type)
```

### 3.1 Step ② — two ways to give us mass

**Path A — "I know the number."** Load the ship in game, read its mass, type it in. Exact, trivial,
no unresolved research. **This is the default and always available.**

**Path B — "Work it out for me."** Describe the ship's storage — how many of each cargo container and
tank — and the app resolves total mass from game data.

Research §4.3 confirms Path B is mostly free: `CargoContainer150/250/750` carry `MaxMass`
(16 800 / 67 200 / 2 150 400 kg) directly.

Both gaps that used to sit here are now closed. **Block mass is computed** from the game's own
formula over measured inputs (Research §4.0, §4.0.0), so it is `Derived`, not `Assumed`. And **tank
contents are free because gas is massless** (Backlog B3) — tanks contribute their block mass and
nothing else, which is why the "tanks always full" rule below costs the user nothing.

So Path B shows: **cargo capacity and block masses both resolved from game data**, plus a hull mass
the user still enters. The only `Assumed` input left in the whole flow is planet surface gravity.

### 3.1.1 Which planets to show

Four are reachable in the current build — **Verdure, Kemik, Caligo and Palatine** — though ten ship
as data. Per §4.4 the rest are listed rather than hidden ("does this exist yet?" is a real question
during alpha), but the playable ones come **first, under a heading, and one of them is the default
selection**, so the common case needs no scrolling.

**Which are playable is derived, never listed.** A planet is playable when its milestone matches the
milestone of the game build the config came from (`Core.PlanetAvailabilityRules`), so the grouping
re-derives itself when Keen ships the next one. An earlier version hardcoded `{verdure, kemik}` here
and was silently wrong the moment Caligo and Palatine became reachable.

The dropdown groups into **Playable / Custom / Older milestones / Not in this build yet**, with
headings shown only for groups that have members.

### 3.2 Load presets

**Tanks are always full. Cargo varies.** Fuel is the thing you can't choose to leave behind, so
treating tanks as anything but full would flatter the numbers.

As it turns out, **the rule costs nothing to honour**: gas is massless in SE2 (Backlog B3), so a
tank's fill level does not move the ship's mass at all and the three presets differ by cargo alone.
The rule stays stated because it is the right rule, and because it would start to bite the moment
Keen gives gas a mass — at which point the presets already mean the right thing.

| Preset | Cargo | Tanks |
|---|---|---|
| Empty | 0% | 100% |
| Half | 50% | 100% |
| Full | 100% | 100% |

**All three are shown at once**, not toggled. The failure this app exists to prevent is the ship that
lifts empty and strands full — you cannot see that if you have to switch between views.

### 3.3 Step ③ — proposals per thruster type

For each thruster type available in the current game build, propose a configuration meeting the
requirement. One row per type, so the trade-off (count, added mass, power/fuel draw) is directly
comparable.

### 3.3.1 The results panel is a table, grouped — not tabs

Twelve types is a lot of rows, and the first version rendered each as a card of inline text runs.
Nothing lined up, so there were no columns for the eye to follow and the panel read as a wall.

**Fixed by alignment, not by hiding.** Real right-aligned numeric columns with tabular figures, rows
grouped under a family heading (Atmospheric / Hydrogen / Ion), families ordered so the cheapest
option overall is the first row on screen, and sizes ascending within a family so "one size up" is
the adjacent row.

**Tabs by family were considered and rejected.** They would cut the row count on screen, but the
panel's entire job is weighing an atmospheric loadout against a hydrogen one — and a tab strip puts
two thirds of the options behind a click, leaving you comparing against memory. That is the same
reason §3.2 shows all three load presets at once.

Two things are folded rather than dropped:

- **Unusable loadouts collapse into one line** — *"4 not usable here — no thrust in this
  atmosphere"* — expandable. On an atmospheric world "the ion family is dead here" is among the most
  useful things the panel says (§4.4); it just does not need a row each to say it.
- **Headroom moves to the row's tooltip.** It is derivable from the covered range, and the table
  earns its readability by carrying only what you actually choose between.

**Long term: mixed compositions** — e.g. atmospheric for lift-off plus ion for orbit. Deferred, but
the core must be designed so a mixed solver is an added function, not a rewrite (Technic §4).

---

## 4. The two things that make this non-trivial

These are the parts a naive calculator gets wrong, and they're the reason the app is worth building.

### 4.1 Integer thruster counts → every configuration is a *range*

You cannot fit half a thruster. Rounding up to a whole number means the configuration has slack, so
each proposal is honest about the band of ship mass it actually supports:

```
18 × Atmospheric 2.5 m     covers 500,000 – 518,559 kg     (at TWR 1.0, Verdure)
                           ▲ your ship: 500,000 kg — 3.6% headroom
```

Showing the upper bound tells you how much you can grow before re-planning — genuinely useful, and
it falls straight out of the math (Technic §4).

### 4.2 Thrusters weigh something, and that feeds back

**This is the subtle one.** Proposing 10 large thrusters adds their mass to the ship, which raises
required thrust, which may demand more thrusters. It's a fixed point, not a division.

It also has a **failure mode with no solution**: if a thruster cannot lift its own weight at the
target gravity and TWR, *no quantity of them ever works*. The app must detect that and say so
plainly, rather than proposing an absurd number or spinning:

> **Ion 1 m can't lift itself on Verdure.** At 1 g, each unit produces 8 950 N but weighs 58 kg
> (569 N) — it clears its own weight, but 156 of them are needed for a 500 t hull. Consider a larger
> size.

Technic §4 has the closed-form solution and the impossibility condition — it's arithmetic, not
iteration.

**Assumption to state in the UI:** proposals assume **no thrusters currently installed**. If the
entered mass came from a ship that already has thrusters, their mass is being counted twice. A
persistent one-line note, not a dismissible dialog.

### 4.3 Environment, now fully modelled

Research §3.3 found `ThrustClassesConfiguration.def` — the complete effectiveness model:

| Class | Full thrust at | Zero thrust at |
|---|---|---|
| Atmospheric | air density ≥ 0.8 | ≤ 0.2 |
| Ion | air density ≤ 0.2 | ≥ 0.8 |
| Hydrogen | everywhere (`Min = -1` sentinel) | — |
| Water | underwater only, not yet shipped |

Combined with per-planet atmosphere geometry (full density to 1.08 R, zero by 1.15 R — Research
§5.2), the app can show thrust **as a function of altitude**, not just "surface" vs "space".

**Decision: v1 computes at sea level for the chosen planet** (the departure case, which is what was
asked for), but the model supports altitude, so an altitude slider is a natural v2 addition rather
than a redesign.

### 4.4 Not-yet-implemented content is shown, not hidden

Underwater thrusters have models but no definitions, and `ThrustClassesConfiguration` already
defines a `Water` class (Research §3.3). Show them greyed: *"not implemented in this build."* During
alpha, "does this exist yet?" is a real question, and answering it is nearly free.

### 4.5 Data provenance is always on screen

```
Game data: SE2 build 2.3.0.2798 · 17,172 definitions · read 2026-08-04 14:22 · [Rebuild]
```

The app detects staleness itself (game files changed since last read) rather than silently serving
cached numbers. Silent staleness would violate P1 in the exact scenario the app exists for: patch day.

The app itself only ever reads a JSON config. A separate producer tool (`tc`) is what touches the
game (Technic §1). Three audiences get their config three ways, and only one of them ever rebuilds:

| Audience | Config source | Rebuilds? |
|---|---|---|
| Web users | Served by the host | Never — not their concern |
| Desktop binary users | Generated on first run by the bundled `tc.exe` | **Yes** — once, then on patch day |
| Power users / self-hosters | `tc extract` on their own install | Yes, by CLI |

### 4.5.1 Should the desktop GUI rebuild at all?

**Recommendation: yes, but as a thin, unglamorous "Data" section — not a headline feature.**

The argument for dropping it is decoupling, and it's mostly right. But the desktop binary user is
*precisely* the person with Space Engineers installed — they play it — and in an alpha their bundled
config goes stale within weeks. Removing rebuild entirely sends exactly the audience with the game on
their disk off to find a separate CLI tool. That's a real usability loss for the largest group.

Since the binary already ships `tc.exe` alongside it, wiring this is small: a Data panel showing the
current config's game build and staleness, plus a button that shells out. Keep it honest:

- **Desktop-only.** The web build simply doesn't have the section — it isn't a disabled control,
  it's absent. Different host, different capabilities.
- **Degrades to an explanation**, never a dead button: "Space Engineers not found" or "`tc.exe` not
  bundled with this build," with the manual CLI command shown so the user isn't stuck.
- **Visible and cancellable, with progress.** It scans 17k files and may invoke the game's own code.
  Pretending it's instant would be a lie, and the user must be able to distinguish it from a hang.
- **The app stays fully usable with no game installed.** Rebuild is the *only* feature that needs SE2
  present; its absence greys one panel and blocks nothing.

The config is **plain, hand-editable JSON**. Where the app shows an `Assumed` value, the user can fix
it inline in the UI or directly in the file — the same value, one source of truth.

### 4.5.2 Settings persist, in a file you can read

`%LocalAppData%\ThrusterCalculatorSE2\settings.ini` — plain INI with comments, the same shape as the
sibling project's. It remembers the departure planet, its gravity, and the target TWR.

Three rules, each load-bearing:

- **Self-creating.** Missing on launch, or missing a key, and it is written out complete. The file
  documents itself rather than growing silently as features are added.
- **Saved on clean exit only.** A crash leaves the previous known-good file intact rather than
  persisting whatever state caused it.
- **An implausible value is treated as absent.** A hand-edited `Gravity = 0` would make the
  requirement zero and every loadout trivially feasible — a wrong answer that looks like a working
  app. Out-of-range values fall back to the default instead.

**Saved gravity applies only when its planet came back.** Gravity is a per-planet number the user
supplies (§4.5 / Research §5.3), so carrying it onto a *different* planet would be wrong — and on a
first run the stored default would otherwise overwrite whatever the selected planet actually states.

### 4.6 Units

**Masses are kilograms, everywhere, in and out.** This reverses an earlier decision to display
tonnes, and the reason is the one that should have decided it first: **the game shows the player a
mass in kilograms**, bottom-right on the HUD. That number is the input, so asking for tonnes made the
player divide by a thousand before typing — and a slipped factor of a thousand is a silent, entirely
plausible-looking wrong answer that nothing downstream can catch.

Mixing them is worse than either: kilograms in and tonnes out means converting in your head to check
whether a result is sane, which is exactly the moment the slip goes unnoticed. So ship mass, hull
mass, cargo capacity, added thruster mass and the supported range are all kilograms with thousand
separators.

Thrust stays **kN** — raw thrust runs to 15 465 370 N, which is unreadable, and no in-game figure
competes with it. Gravity is m·s⁻². TWR is a plain multiplier ("1.4×"), because that's how players
talk. Resource draw carries its own units from the config (**kW** for electricity, **L/s** for
hydrogen) — they are not comparable across classes, so an unlabelled number would invite exactly
that comparison (Research §3).

---

## 5. Layout sketch

Single window, live recompute — not a wizard.

```
┌───────────────────────────────────────────────────────────────────────────────┐
│  ThrusterCalculator SE2                                                          │
├─────────────────────────────┬─────────────────────────────────────────────────┤
│  DEPARTURE                  │   REQUIRED THRUST (up, TWR 1.0)                  │
│  Planet  [ Verdure      ▾]  │                                                  │
│  Gravity [ 9.81 ] m/s² ⚠    │     empty    3 920 kN     half   4 610 kN        │
│  TWR     [ 1.0  ]           │     full     5 300 kN                            │
│  ─────────────────────────  │                                                  │
│  SHIP MASS                  │   CONFIGURATIONS  (full load)                    │
│  ( ) I know it: [       ]kg │   ┌──────────────────────────────────────────┐   │
│  (•) Work it out            │   │ Atmospheric 2.5 m  19 × +8,816 kg  12 MW │   │
│      Hull   [300,000] kg ⚠  │   │   covers 528,000–546,000 kg · 3.4% head  │   │
│      Cargo 250 [   4 ]      │   │ Atmospheric 5 m     4 × +6,207 kg  10 MW │   │
│      Cargo 750 [   0 ]      │   │   covers 512,000–589,000 kg · 12% head   │   │
│      H2 Tank 500 [ 2 ]      │   │ Hydrogen 2.5 m      3 × +3,015 kg 36 L/s │   │
│  ─────────────────────────  │   │   covers 501,000–581,000 kg · 14% head   │   │
│  Dry      300,000 kg        │   │ Ion 1.5 m          ✕ no thrust in atmo    │   │
│  Cargo    268,800 kg (full) │   └──────────────────────────────────────────┘   │
│  Tanks      2,000 kg (block)│                                                  │
│  Total    570,800 kg        │   ⓘ Assumes no thrusters currently installed.    │
│                             │   ⚠ = Assumed value — click to edit              │
├─────────────────────────────┴─────────────────────────────────────────────────┤
│ Game data: build 2.3.0.2798 · 17,172 defs · read 14:22 · [Rebuild]            │
└───────────────────────────────────────────────────────────────────────────────┘
```

Notes on the sketch:

- **⚠ marks `Assumed` values inline** and they're editable in place — P2 in practice. Only two carry
  it now: planet surface gravity, which is world-instance data the producer can never read
  (Research §5.3), and the hull mass the user types. Block masses are `Derived` from game data.
- **Tanks show block mass only**, with no ⚠: gas is massless (Backlog B3), so a full tank weighs what
  an empty one does. Saying so beats leaving the reader to wonder where the fuel went.
- **Ion shows a reason, not a number.** "✕ no thrust in atmosphere" is the useful answer; a `0` or a
  blank row is not.
- Each configuration shows **count, added mass, draw, and supported range** — everything needed to
  choose, without a detail view.
- Cargo/tank counts are per *block type*, matching how you actually build.

## 6. What this is deliberately *not*

- **Not a blueprint editor.** Read-only against game files — we can never corrupt a save.
  (`BlueprintHelperSE2` is the project that writes.)
- **Not an overlay or injector.** Research §1: no scripting API exists.
- **Not a wiki.** It computes against *your* install.
- **Torque and centre-of-mass are out of scope for v1.** Thrusters are a bag per axis. Placement
  effects are a substantially larger problem; noting it explicitly so it doesn't creep in.
- **Six-axis analysis deferred.** Plan mode answers the departure question, which is "up". Lateral
  axes matter for handling and belong with the Check mode that reads a real blueprint.

## 7. Later

- **Check mode** — load a blueprint, analyse what's actually there. Needs `.vrb` (Research §5.4).
  Design the Plan screen so Check pre-fills its inputs, making it additive rather than a new screen.
- **Mixed thruster compositions** (§3.3).
- **Altitude slider** (§4.3) — the model already supports it.
- **Six-axis breakdown**, with Check mode.
