# Focused Item Editor checks

Run the dependency-free console test suite from the project root:

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

The fresh process is the test executable, not RimWorld. Actual in-game save/load,
map spawning, hauling, splitting, and merging still require manual verification.
Reset is explicitly destructive: map-spawned stacks can lose excess while held
stacks may remain oversized. These tests do not normalize stacks or claim that
all oversized stacks are automatically truncated. See `docs/game-api-notes.md`
for the full-restart manual checks and native holder-loading limitations.
