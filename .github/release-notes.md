## Which download

| | Size | Needs |
|---|---|---|
| `…-win-x64.zip` | ~260 MB | **Nothing.** Take this one if unsure |
| `…-win-x64-requires-dotnet9.zip` | ~30 MB | [.NET 9 **Desktop** Runtime, x64](https://dotnet.microsoft.com/download/dotnet/9.0) |

Either way the folder holds two programs — `ThrusterCalculator.Gui.exe` to use it, and `tc.exe`
which reads your game — plus `sample-gamedata.json`. The first launch takes a moment longer than
later ones while the graphics libraries unpack.

The small one needs the *Desktop* runtime specifically, not the plain one — `tc.exe` hosts the
game's own assemblies to read block data, and those pull in WPF and WinForms. With only the base
runtime installed the app opens but Rebuild fails.

## Getting started

1. **Unblock the zip before extracting.** Right-click it → Properties → tick **Unblock** → OK.
   Windows marks anything downloaded, and unblocking *after* extraction is too late — every file
   inherits the mark and the app will refuse to start.
2. Extract anywhere. There is no installer; the folder *is* the application.
3. Run `ThrusterCalculator.Gui.exe`. Windows will warn that it is from an unknown publisher —
   the build is not code-signed. **More info → Run anyway.**

## First run: generate your data

The download deliberately contains **no game data.** On first launch you will see a banner saying
the numbers are samples, and a **Rebuild** button at the bottom of the window.

Click it. `tc.exe` reads your own installed copy of Space Engineers 2 and writes `gamedata.json`
beside the app, and the window updates in place — no restart, and nothing you have typed is lost.

**Why it works this way.** The thruster values move with every patch, so a config baked into a
release is out of date the moment Keen ships one — and shipping their numbers is not ours to do
(see `License.md`). Generating it locally means the data always matches your install, and the app
tells you when your game has changed since you last built it.

## Uninstalling

Delete the folder. Settings live in `%LocalAppData%\ThrusterCalculatorSE2\settings.ini`; delete
that too if you want no trace.

## Requirements

Windows x64 and an installed copy of Space Engineers 2. No .NET runtime is needed — the build is
self-contained, which is why the download is large.

---

This is a fan-made tool, not affiliated with or endorsed by Keen Software House.
