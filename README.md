# ThrusterCalculator SE2

A thruster calculator for **Space Engineers 2**, reading live values from your installed copy of the
game rather than a hardcoded stat table.

You give it a departure planet and a ship mass; it tells you how much thrust you need and proposes a
configuration for each thruster type — accounting for the fact that the thrusters you add have mass
of their own.

> **Status: working.** `tc extract` produces a config from your installed game, and the desktop app
> computes thruster loadouts from it. The documents below are the design, grounded in the actual
> shipped game data rather than guesswork.

## Why read the game instead of a wiki

SE2 is in alpha and its numbers move every patch. During research we cross-checked the community
wiki's thruster table against the game's own definition files and found the **entire ion-thrust
column was a copy-paste of the atmospheric column**, overstating ion thrust by 3.5–5x. A player
sizing an ion ship from those figures would under-build by a factor of four.

The game files had it right. That's the whole premise.

## Documents

| | |
|---|---|
| **[Research.md](Research.md)** | What the game actually gives us. File formats, the thruster/planet/mass data, what's reachable and what isn't. Every claim traced to a file or a decompiled method. |
| **[Design.md](Design.md)** | What the app does and how it feels to use. Modes, UX decisions, and the two things that make this non-trivial. |
| **[Technic.md](Technic.md)** | Architecture. The producer/consumer split, project layout, the sizing math, testing strategy. |
| **[Schema.md](Schema.md)** | The `gamedata.json` contract — the interface between the two halves. Stands alone. |
| **[Backlog.md](Backlog.md)** | Deferred decisions and known gaps, each with enough context to pick up cold. |

## How it's put together

Two programs that never link to each other, joined by a versioned JSON file:

```
  PRODUCER (th)                      CONSUMER (GUI / web)
  scans the game's .def files  -->   reads gamedata.json
  needs: SE2 installed, Windows      needs: nothing
```

The consumer requires no game install, no game assemblies, and no Windows-specific API — which is
what keeps a web version on the table and makes CI trivial.

Built with .NET 9 and Avalonia. Windows-first, because the game is.

## Key findings from research

- The game's `.def` files are **plain JSON** — 17,172 of them, forming a GUID-keyed graph. No binary
  parsing is needed for anything the calculator uses.
- The **atmospheric effectiveness model** is fully in data (`ThrustClassesConfiguration.def`):
  atmospheric thrusters ramp to zero below 0.2 air density, ion thrusters *above* 0.8, and hydrogen
  has no falloff at all.
- **Block mass isn't stored** — it's computed. Decompiling the engine gave
  `mass = massCurveModifier * sqrt(V) * log10(V) + minBlockMass`, verified by recovering `V` as an
  exact integer for all twelve thrusters.
- Planets carry their own gravity and atmosphere geometry in data, so **custom and future planets are
  picked up automatically**.

## A note on game data

This repository contains **no data extracted from Space Engineers 2**. Definition files and generated
configs are gitignored; the only committed data file is a hand-authored synthetic fixture with
invented numbers, used for tests. Run the producer against your own installation to generate a real
config.
