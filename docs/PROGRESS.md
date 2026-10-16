# Skybound Sage progress — October 16, 2026

**Research prototype; a playable combat overhaul has not been released.**

- The published v2.6 offline Request Guard passed 42 synthetic tests.
- v2.7 inspected six data-access methods and confirmed that the base calculator is abstract.
- The v2.8 reader passed 49 local tests and four simulated setup/cleanup cases. Its approved main-menu check then captured all seven selected concrete calculator implementations for static review.
- The temporary setup was removed. All three saves and the original game executable were verified unchanged.

The calculator review supports requiring a valid retained baseline; an empty request list has no default-handling path in these bodies. Which rule the player's property selects, concrete float-request behavior, request ownership/continuity, and safe in-game restoration still need verification. Combat effects and character swaps remain inactive.

Raw game code, implementation captures, saves, and private machine records remain local. This note reports research progress, not playable compatibility or multiplayer safety.
