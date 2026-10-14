# Skybound Sage — observer foundation 0.5

Original local callback-lifecycle helper built after the actual v0.4 metadata check identified matching typed Bind/Unbind candidates. This library is not a Wukong adapter or an installed mod. It has no loader entry point, game reference, object lookup, input polling, patch, stat effect or scoring operation.

`OwnedSubscription` tracks one exact owned delegate and caller-supplied add/remove operations. It prevents duplicate attachment, rejects cross-thread/reentrant mutations, makes successful shutdown idempotent, attempts exact compensating cleanup after a failed add, and preserves Unknown state plus callback/remover references when cleanup fails. It never calls a shared Clear operation or invokes the owned event handler.

The helper's isolated fixture checks verify its own bookkeeping and behavior with synthetic host operations. They do not prove that Wukong's Bind method preserves other listeners, that native Unbind is reliable, that a callback identifies the player, or that the constructing thread is a game thread. The real adapter must establish those claims before supplying its host operations and must capture lifecycle-generation tokens when registering callbacks.

Run `build-local.ps1` to compile the library and local test runner with the existing Windows Framework compiler. It downloads/installs nothing and touches no game files. See `LOCAL-TEST-LOG.txt` for the actual check result.

Next: locate genuine gameplay interfaces/player context, verify a real callback's outcome/ownership semantics, then connect a logging-only observer. Style scoring, HUD display, controller activation and player-only Fury effects remain separate unimplemented game milestones. The tested v0.4 module and earlier packages are preserved.
