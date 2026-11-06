# Focused mod checks

The project uses local game references plus the installed Harmony 3 DLL. Copy
`Mods/Harmony/1.7/Assemblies/0Harmony.dll` unchanged into `refs/0Harmony.dll` before
building on a new checkout. `refs/` is ignored; the mod's Harmony reference is
compile-only and is not bundled in the mod. The test executable copies it to its
own build output.

Run the package-free console test suite from the project root:

```sh
dotnet run --project tests/RimWorldUtilities.Tests/RimWorldUtilities.Tests.csproj -c Release
```

The core checks cover integer validation, explicit opt-in, original-limit capture,
failed writes, unsupported definitions, higher limits from other mods, and limits
that must never decrease. They also cover multiple independently enabled types,
per-type errors, and preservation of unrelated definitions. They use isolated
definition fixtures, not the global game definition database.

Reset checks cover enabling, durable scheduling/cancellation, keeping active
limits and quantities unchanged, preserving pending markers during Apply,
completion only at startup, other mods' higher limits, and retained original
metadata. Failed scheduling/cancellation writes preserve the previous state;
failed startup cleanup must still suppress the reset override across restarts.

HP checks cover integer bounds, the useHitPoints flag, non-stackable selection
eligibility, separate stack eligibility, and book/minified/missing-quality
exclusions. Native property checks use the actual CompQuality and StatWorker
cache implementations with isolated items and a test HP stat worker. They verify
callback counts, no art generation, same-quality no-ops, absolute HP preservation,
clamping after downgrades, fresh Max calculations, and stale-target rejection.
Only live selection lookup is replaced by a fixture resolver; no map is started.

Crafting checks cover opt-in behavior, immediate enable/disable, unavailable
patches, failed writes, faction/prisoner exclusions, slave and non-humanlike
workers, matching active production bills, medical/autonomous exclusions,
quality support and recipe output checks. IL tests verify exact-one matching,
unchanged incompatible input, argument order, branch labels and exception blocks.

To also exercise the actual Scribe serializer, atomic settings replacement, and
loading those settings in a fresh test process, provide the installed game DLL
directory:

```sh
RIMWORLD_GAME_DIR="/path/to/RimWorld v1.7.4998b" \
  dotnet run --project tests/RimWorldUtilities.Tests/RimWorldUtilities.Tests.csproj -c Release
```

These integration checks read the game's runtime DLLs for native logging
dependency. They initialize only in-memory default preferences, write settings
under a temporary directory, and delete the fixtures afterward. They do not
launch RimWorld or read/write the player's settings or saves. Without that
environment variable, native property/serialization/restart checks are explicitly skipped.
The temporary MaxHitPoints StatDef fixture is restored after property checks and
exists only in the test process; no player/game definitions on disk are modified.

The integration suite also reads a literal settings fixture using the Steel-only
release's XML tags, verifies a raised Steel limit and original metadata survive
without a file rewrite, enables another item type, then restores both keys in a
fresh process. Fixture files never replace the player's mod settings.
Additional native checks round-trip pending reset markers, cancel one and verify
the old limit in a fresh process, then schedule it again and verify completion
across two successive fresh processes. Other enabled types stay untouched.

Crafting integration checks also inspect the real game IL and install the
production Harmony patch in the isolated test process. A patched probe exercises
the redirect and native `CompQuality` callback without starting a map. The probe
stubs only the native-generation call inside our helper, before compiling the
probe, to avoid initializing `ModsConfig` or accessing the player's profile.
The global `QualityUtility` method is never patched. Counts and argument checks
verify that the redirect calls generation exactly once and forwards the worker,
skill and inspiration flag. Native RNG, inspiration consumption, art and letters
still need in-game verification. Intentional incompatible-pattern checks exercise
the unavailable state and warning path. Crafting settings are round-tripped with
Scribe in fresh processes, including both services writing shared settings.

Speed checks cover the exact 1/2/3/5/10/20 choices, 1× returning the original
float unchanged, invalid settings, worker/bill eligibility, separate patch
availability and preservation of Legendary. Pure IL tests cover callback
resolution from the delegate-field assignment, exact-one decrement matching,
receiver reloads without fixed local indices, labels/exception blocks, and
unchanged rejected input. Native checks install the patch on the actual generated
game callback and execute a progress probe at all six speeds. The probe verifies
work/unfinished-work values, unchanged elapsed and XP tick counters, completion
branching and NPC exclusion. It models those counters; a real pawn skill tracker
and complete bill job still require in-game checks. Fresh-process settings checks
exercise 20× with Legendary on, 3× with it off, and return to 1×.

The fresh process is the test executable, not RimWorld. Actual in-game save/load,
map spawning, hauling, splitting, and merging still require manual verification.
Reset is explicitly destructive: map-spawned stacks can lose excess while held
stacks may remain oversized. These tests do not normalize stacks or claim that
all oversized stacks are automatically truncated. See `docs/game-api-notes.md`
for the full-restart manual checks and native holder-loading limitations.
