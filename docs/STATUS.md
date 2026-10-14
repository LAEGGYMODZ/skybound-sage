# Development status

This repository is a prototype for original single-player campaign mods. It does not contain a playable combat overhaul or automatic game installer.

## Isolated original code

The C# core passed 22 native behavioral checks. An earlier deterministic comparison with the original JavaScript model covered 4,036 synthetic events and matched twelve snapshot fields per event. Charge, ready, active, cooldown and disabled states were exercised. This repository publishes the C# sources and tests; parity is a prior local result, not an included runnable parity harness or proof of exhaustive equivalence.

The observation boundary depends on a future adapter providing genuine outcomes, stable identities, confirmed ownership and serialized calls. It rejects stale generation tokens and unconfirmed events but does not independently detect hits or dodges.

## Actual Wukong experiments already completed

One Steam installation, build `21393610`, menu version `1.0.21.23831`:

1. Version 0.2 startup probe initialized through the reviewed loader with JIT/development reload off. Main-menu startup passed. Its temporary files were removed; a subsequent clean menu launch passed. All three saves and the original executable were unchanged.
2. Version 0.3 metadata diagnostic initialized and created two local reports. Main-menu startup passed. The report contained 189 selected type descriptors and 1,600 rows, reached its row cap and reported zero metadata errors. The twelve installed files and both generated reports were removed after normal exit. All three saves and the original executable were unchanged. No campaign was loaded during either diagnostic experiment.
3. Version 0.4 focused diagnostic initialized, wrote both local reports and reached the main menu. Its report contained 58 selected types and 1,038 rows, reported 831 milliseconds and zero metadata errors, and remained partial due to selection/member/relationship/base-depth limits. It did not reach its row cap or soft time budget. All fourteen installed/generated files were removed after normal exit. The three saves and original executable were unchanged; no campaign was loaded.

These results prove those bounded startup/metadata behaviors in that installation. They do not establish loader safety in general, current-build compatibility elsewhere, callback semantics or a working gameplay mod. The loader's shutdown callback was not independently observed. No save content or raw private runtime record is published here.

## Focused discovery 0.4

Locally compiled against the actual reviewed 0.0.8 interface; 22 fixture checks passed. It also passed its bounded Wukong startup/report experiment described above. This version improves provenance, per-assembly coverage counts, selection fairness, related callback shapes and inherited binding descriptors. It performs no game invocation, instance-value read, controller polling, event subscription, patch or effect.

Observed descriptors include an attack callback accepting `Int32 ResID`, an actor-death callback accepting `AActor Attacker`, a parameterless death callback, and typed Bind/Unbind candidates that take the exact callback signature. No binding method or callback was invoked. Argument names do not establish outcome semantics or ownership, and the controller helper's out-flags descriptor does not prove physical input compatibility.

At startup, `b1.Managed` returned only two type descriptors (`ModuleRefMain` and its nested compiler-generated type). The native wrapper assemblies returned 610 and 971 slots, inspected for name hints; their selected member coverage remained partial. Other loaded modules were not expanded. This does not establish the location or later availability of business combat events.

The original callback helper described below is built and locally checked. Next comes a narrow module-reference investigation to locate gameplay interfaces and establish actual player context and native binding behavior. Any new in-game object access or callback subscription requires its own implementation/review and genuine outcome/ownership/lifecycle validation before scoring or effects are enabled.

## Original observer foundation 0.5

Added after the actual v0.4 report established matching typed binding/unbinding descriptor candidates. The helper compiled and passed 13 isolated checks: exact delegate identity, duplicate prevention, synthetic unrelated listeners, idempotent cleanup, compensating cleanup after a failed add, combined failures, retryable Unknown state, wrong-thread/reentrant calls, handler non-invocation and argument validation.

This component has no game entry point and was not installed in Wukong. It delegates add/remove behavior to a future verified adapter. A successful local fixture does not establish that native Bind preserves other listeners, that native Unbind removes a real callback correctly, or that the caller is on the game thread. Genuine outcome/ownership/context and lifecycle-generation capture remain prerequisites for integration.

## Third-party reference

The [author's loader documentation](https://github.com/czastack/B1CSharpLoader/blob/master/README.en.md) describes C# mod initialization and JIT-dependent patch support. This project references the separately obtained interface; it does not redistribute the loader or game assemblies. The current diagnostic scope keeps JIT/patch behavior off. A later patch-based feature would require a separate implementation and validation decision.
