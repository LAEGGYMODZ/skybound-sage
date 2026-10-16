# Skybound Sage

Original combat-mod development for the single-player PC campaign of **Black Myth: Wukong**. The design mixes deliberate counters and heavier impact with varied attack flow, using original style and Fury rules.

**Prototype: this is not a playable combat overhaul yet.** The portable rules work in isolation. An original startup probe and two bounded metadata scans ran in one Steam installation and were removed afterward. The focused discovery module passed its startup/report experiment, but genuine hit/perfect-dodge outcomes, player ownership, physical controller input, HUD display and game effects are not established.

## Current components

Latest addition: [Request Guard v2.6](request-guard/README.md), a small offline experiment for attributing temporary requests and checking removal proposals. Its publication copy passed 42 synthetic checks. It has no game adapter or enabled native execution; in-game ownership, cleanup and Fury effects remain unverified. Earlier components below retain their original validation scope.

| Component | Present behavior | Evidence and limits |
|---|---|---|
| `core/` | Original style/Fury rules, event deduplication, session/lifecycle boundary | 22 isolated behavioral checks passed. Earlier C#/JavaScript parity covered 4,036 synthetic events; it does not prove game compatibility. |
| `discovery/` | Bounded descriptor scanner with assembly coverage, combat-family balancing, related callback shapes and inherited binding descriptors | 22 local checks and the v0.4 startup/report experiment passed. Actual event usability remains unverified. It invokes no game actions, values, controller reads or event handlers. |
| `observer/` | Original ownership/lifecycle helper for one callback, caller-supplied host operations and retryable cleanup | 13 local checks passed. No game entry point, Wukong binding, physical input or scoring; native host behavior remains unverified. |
| Gameplay adapter | Planned genuine player-outcome observer | Not implemented. No score is awarded from an unverified game callback or a controller press. |
| Style display / Fury effect | Planned later integration | Not implemented. The core's modifier request applies no real damage or stat change. |

## Build the isolated core

On a Windows machine with the existing .NET Framework C# compiler:

```powershell
& .\core\build-local.ps1
```

This compiles original code and runs isolated tests. It does not install or launch a game, download tools, contact GitHub or change controller settings.

## Build focused discovery locally

The entry point implements the author's real `CSharpModBase.ICSharpMod` interface. Obtain the reviewed 0.0.8 interface independently from the [author's loader release](https://github.com/czastack/B1CSharpLoader/releases/tag/v0.0.8). This repository contains none of that loader's code or binaries. The interface identity expected by the build is recorded in `discovery/build-local.ps1`.

```powershell
& .\discovery\build-local.ps1 -LoaderInterfacePath 'C:\path\to\reviewed\CSharpModBase.dll'
```

This compiles/tests only the local project. It does not install the loader/module or run Wukong. Follow the [development status](docs/STATUS.md) for measured scope and remaining work.

## Planned first playable milestone

The observer foundation can be compiled and tested independently with `observer/build-local.ps1`. It tracks the exact owned callback, rejects duplicate/cross-thread/reentrant operations, attempts compensating removal after a failed add, and preserves Unknown state after failed cleanup. Synthetic host checks do not prove that real Wukong binding methods preserve other listeners or identify player outcomes.

Establish a genuine player-hit and perfect-dodge observer, prove player/target ownership, reject duplicates and clone/echo/NPC hits, and reset correctly across player lifecycle changes. Add observational style feedback next. Only then investigate an optional player-only Fury effect and controller activation.

Returning-staff attacks, aerial follow-ups and environment changes are later ideas, with compatibility unverified. Controller buttons alone are never evidence of landed damage or a successful perfect dodge. This project has no multiplayer integration or anti-cheat bypass.

## Source and license

MIT applies to the original code included here. No proprietary game assembly, extracted asset, save, credential, private work file, third-party loader source or loader binary is included. Third-party tools and the game retain their own terms. Names of other games describe broad design inspiration; no moves, models, audio or code from those games are copied.
