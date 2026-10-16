# Request Guard v2.6

An original, offline experiment for tracking a temporary request and deciding when a removal proposal is supported by supplied test data. This is groundwork for future optional Fury effects in Skybound Sage.

**It does not connect to Wukong, change gameplay, or make Fury playable.** Every proposal keeps native execution disabled. No loader or game files are required.

## Try the local checks

On Windows with PowerShell 7 and the existing .NET Framework C# compiler:

```powershell
pwsh -NoProfile -File .\request-guard\build-local.ps1
```

The publication copy passed **42 synthetic checks**. They cover ambiguous ownership, recorded request replacement/reuse, partial updates, context changes, uncertain cleanup, and preserving unrelated requests. The script only compiles the library and runs its local tests.

## Limits

The journal, generation, and incarnation values are supplied by the test fixtures. They are not verified Wukong observations or authenticated evidence. Equal snapshots cannot reveal a removal/recreation that was omitted from the journal. Passing these checks proves neither native ownership nor safe in-game cleanup.

There is no game adapter, controller reader, gameplay calculator, combat effect, or character swap here. Live activation, expiry, baseline behavior, and restoration still require separate investigation. This project targets single-player development; multiplayer compatibility and anti-cheat safety are unverified.

Original source is covered by the repository's MIT license. No game code, extracted assets, saves, loader binaries, or private runtime reports are included.
