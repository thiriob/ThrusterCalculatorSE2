# ThrustersHelper SE2 — Design & UX

Companion to [Research.md](Research.md) (what the game gives us) and [Technic.md](Technic.md) (how
it's built). This document is about **what the app does and how it feels to use**.

Status: proposal. Decisions marked **[OPEN]** need your call.

---

## 1. The problem, stated as a player

You are in the ship editor. You have a hull. You want to know:

> *"Will this thing actually fly, and if not, what do I add?"*

Every follow-up is a variation: will it lift off Verdure fully loaded? Is it under-thrusted
sideways? Am I wasting mass on thrusters I don't need? Which thruster size is the efficient choice
here?

The app exists to answer that question in **under ten seconds**, with numbers that are *correct for
the build of the game currently installed* — not for the build a wiki was written against.

## 2. Design principles

These are the tie-breakers when a specific decision is contested.

**P1 — The game is the source of truth, and it moves.** Every number displayed should be traceable
to a definition file in the installed game. No hardcoded stat tables. When the game patches, the app
should show the new numbers with no code change and no update from us. This is the whole reason the
app exists (Research §3: thrust doesn't even follow a clean power law, so modelling it is hopeless).

**P2 — Be honest about what we don't know.** The game is in alpha and several things are genuinely
unknown (atmospheric falloff curve, mass curve, planet gravity — Research §8). The app must
**visibly distinguish** three tiers:

| Tier | Meaning | Visual treatment |
|---|---|---|
| **Measured** | Read from game definition files | Normal |
| **Derived** | Computed from measured values by our own math | Normal, with a "how?" affordance |
| **Assumed** | Our curated guess or a user-entered value | Marked, and editable inline |

A confidently-wrong number is worse than a flagged estimate. This principle is *why* the app is
trustworthy during alpha; it should not be compromised for visual tidiness.

**P3 — Answer first, detail on demand.** The verdict ("lifts off Verdure at 1.4× — yes") is the
product. Per-axis breakdowns, power draw, and the definition graph are progressive disclosure.

**P4 — Never require the game to be running.** Files on disk only. The app is a second-monitor
companion, not an overlay (and Research §1 rules out an overlay anyway).

**P5 — Degrade, don't block.** If planet gravity can't be extracted, use an editable table. If a
blueprint can't be decoded, fall back to manual mass entry. A missing capability greys out one
panel; it never prevents launch.

---

## 3. The two modes

**[OPEN — this is the main design decision.]** I recommend building **Plan** first and **Check**
second, but they share a calculation core so the order is cheap to change.

### Mode A — Plan (build up from requirements)

*"I want to move 500 t on Verdure with 1.5 g of headroom. What do I need?"*

Inputs: target mass, environment, desired acceleration or thrust-to-weight ratio.
Output: a ranked set of thruster loadouts that satisfy it — cheapest, lightest, fewest blocks.

This is the mode that works **today**, with zero unsolved research blockers, because the user
supplies the mass. It's also the one that helps most at the moment you actually need help: before
you've built the thing.

### Mode B — Check (analyse what exists)

*"Here's my blueprint. Where is it weak?"*

Input: a blueprint. Output: actual mass, per-axis thrust, TWR per environment, flagged weak axes.

Strictly better UX — no typing — but it depends on `.vrb` blueprint decoding (Research §5), which is
the fragile, dependency-heavy path. Building Plan first means Check becomes an *additive* feature
rather than a prerequisite.

**Recommendation: v1 = Plan. Design the UI so Check slots in as a second tab that pre-fills Plan's
inputs.** That way the blueprint reader, when it lands, makes the existing screen better instead of
needing a new one.

---

## 4. Core UX decisions

### 4.1 Six axes, not one number

SE2 ships are rarely symmetric and the classic failure is a ship that flies beautifully forward and
handles like a barge laterally. The result must be **per-direction** (up/down/fore/aft/left/right),
not a single thrust figure.

**Decision: "Up" is privileged.** It's the axis that decides whether you leave the ground, and it's
the one people get wrong. Up gets the headline verdict; the other five are a compact row beneath.

### 4.2 Environment is a first-class selector, always visible

The same ship is a different ship on Verdure vs. in orbit — and thruster *classes* differ in where
they work at all. The environment picker (planet / atmosphere / vacuum) sits **next to the result,
not in settings**, because changing it is the primary interaction, not configuration.

**Decision: show the verdict for multiple environments simultaneously** rather than making the user
toggle. A small matrix — environments × verdict — surfaces "fine in space, can't lift off Verdure"
in one glance. Toggling hides exactly the comparison the user came for.

### 4.3 Load states

Empty vs. full cargo is the other classic trap. **Decision: mass is entered as a range or as
dry + cargo**, and results show both endpoints. A ship that lifts empty and strands full is the
specific failure the app should prevent.

### 4.4 Data provenance is always on screen

Given P1 and an alpha game, the user must be able to trust *when* the numbers came from. A
persistent, quiet status strip:

```
Game data: SE2 build 2.3.0.2798 · 17,172 definitions · read 2026-08-04 14:22 · [Refresh]
```

**Decision: the app detects staleness itself** (game files changed since last read) and offers a
refresh, rather than silently serving cached numbers or forcing a re-scan every launch. Silent
staleness would violate P1 in the exact scenario the app was built for — patch day.

### 4.5 Not-yet-implemented content is shown, not hidden

Underwater thrusters have models but no definitions (Research §3). Rather than omitting them,
**show them greyed with "not implemented in this build."** During alpha, "does this exist yet?" is a
real question players have, and answering it is nearly free.

### 4.6 Units

**Decision: display in kN / t / m·s⁻² / g, compute in SI base units (N, kg).** Raw thrust values run
to 15,465,370 — unreadable. Thrust-to-weight ratio is presented as a plain multiplier ("1.4×") since
that's how players talk about it. The environment matrix shows TWR; the detail panel shows absolute
kN.

---

## 5. Layout sketch

Single window, three regions. Deliberately not a wizard — every input is live and results recompute
as you type.

```
┌──────────────────────────────────────────────────────────────────────────┐
│  ThrustersHelper SE2                                          [Plan][Check] │
├────────────────────────┬─────────────────────────────────────────────────┤
│  SHIP                  │   VERDICT                                        │
│                        │                                                  │
│  Dry mass   [ 420 ] t  │        UP        Vacuum  Verdure  Kemik  Delfos  │
│  Cargo      [  80 ] t  │      ─────────────────────────────────────────   │
│  ──────────            │      empty         —      2.1×    3.4×    1.6×   │
│  Total      500 t      │      full          —      1.4×    2.2×    1.0×  ⚠│
│                        │                                                  │
│  THRUSTERS             │   ⚠ Delfos, fully loaded: 1.0× — no margin.      │
│  Atmo 250   [  6 ]     │                                                  │
│  Atmo 100   [ 12 ]     │   Other axes (Verdure, full)                     │
│  Ion  500   [  2 ]     │   fore 1.9×  aft 0.7×⚠  left 1.1×  right 1.1× …  │
│  [+ add thruster]      │                                                  │
│                        │   Power draw   4 850 kW      [details ▾]         │
│  ENVIRONMENT           │                                                  │
│  ☑ Vacuum  ☑ Verdure   │   ── Assumed values ────────────────────────     │
│  ☑ Kemik   ☑ Delfos    │   Verdure gravity  [ 9.81 ] m/s²   (editable)    │
│                        │   Atmospheric falloff: not modelled — see docs   │
├────────────────────────┴─────────────────────────────────────────────────┤
│ Game data: build 2.3.0.2798 · 17,172 defs · read 14:22 · [Refresh]        │
└──────────────────────────────────────────────────────────────────────────┘
```

Points worth noting in the sketch:

- The **assumed-values block is part of the main result area**, not buried in settings — P2 in
  practice. Editing gravity there immediately updates the matrix.
- "Atmospheric falloff: not modelled" is stated plainly. When Research §8 Q1 is solved, that line
  becomes a real modifier and the matrix gains an altitude control.
- The matrix has an em-dash for atmospheric thrusters in vacuum rather than `0×` — "doesn't apply"
  reads differently from "produces nothing."

## 6. What this is deliberately *not*

- **Not a blueprint editor.** Read-only against game files. (`BlueprintHelperSE2` is the project that
  writes.) This keeps the risk profile low: we can never corrupt a save.
- **Not an overlay or injector.** Research §1.
- **Not a wiki//reference app.** It computes against *your* install; it doesn't document the game.
- **Not multi-platform initially.** The `.vrb` path is Windows-only and the game is Windows-only.
  Avalonia keeps the door open at ~zero cost, but cross-platform isn't a goal driving decisions.

## 7. Open questions for you

1. **Plan vs. Check first** (§3) — I recommend Plan. Agree?
2. **Optimiser scope**: should Plan actually *suggest* loadouts (search over combinations), or just
   validate a loadout you assemble? Suggesting is the more valuable feature and the more interesting
   problem; validating is a fraction of the work. I'd scope v1 to validate, design the core so the
   optimiser is a pure function added later.
3. **Planet gravity**: ship a curated editable table now (Research §5), or block on `.vrb`
   extraction? I strongly recommend the table.
4. **Is per-thruster placement relevant**, or is a bag of thrusters per axis enough? Placement
   affects torque/centre-of-mass, which is a much larger problem. I'd say bag-of-thrusters for v1
   and note torque as explicitly out of scope.
5. **Theme** — match SE2's in-game UI palette, or a neutral desktop look? Neutral is faster and ages
   better; SE2-flavoured feels more like a companion tool.
