# Tests

RimWorldUtilities uses a package-free C# console test project at
`tests/RimWorldUtilities.Tests/`. It combines isolated regression checks with
optional native-assembly integration checks. **Passing the suite does not replace
in-game testing.**

## Prerequisites

- The .NET SDK required by the project (currently .NET 10).
- Local RimWorld reference assemblies in `refs/` (not included in Git).
- Harmony 3 from the installed Harmony mod. On a fresh checkout, copy
  `Mods/Harmony/1.7/Assemblies/0Harmony.dll` to `refs/0Harmony.dll` without
  modifying it. This is a compile-time reference; the mod does not bundle Harmony.

## Run

From the repository root:

```sh
dotnet run --project tests/RimWorldUtilities.Tests/RimWorldUtilities.Tests.csproj -c Release
```

To enable the additional checks that require installed RimWorld runtime DLLs,
set `RIMWORLD_GAME_DIR` to the game installation directory:

```sh
RIMWORLD_GAME_DIR="/path/to/RimWorld" \
  dotnet run --project tests/RimWorldUtilities.Tests/RimWorldUtilities.Tests.csproj -c Release
```

Without `RIMWORLD_GAME_DIR`, the tests requiring those runtime DLLs (including
some native property, serialization, and fresh-process checks) are skipped.
Review the test output to see which checks actually ran.

## What is covered

- **Item Editor:** stack and HP bounds, target eligibility, quality callbacks,
  cache invalidation, and stale-selection protection.
- **Large stacks and settings:** per-item-definition overrides, resets,
  backward-compatible settings, failed writes, and persistence across fresh
  test processes.
- **Crafting and construction:** Always Legendary eligibility and Harmony
  call-site matching; crafting/building speed factors and native IL handling.
- **Research:** speed factors, ordinary research eligibility, and unaffected
  alternative research paths.
- **Colonist Needs:** eligibility, Keep Full intervals, basic needs, and
  Biotech Learning/Play behavior.
- **Abilities:** strict colonist eligibility, excluded charge/external/custom
  categories, actual assembly IL patterns and rejection of ambiguous patterns.
  Optional native checks cover installed cooldown patches, retained timers after
  faction changes, shared-ideology ritual contexts, dialog confirmation guards,
  and Scribe/fresh-process setting persistence. These do not execute a full ritual.
- **Compatibility checks:** preservation of unrelated settings, rejection of
  incompatible patch patterns, and independent feature availability.

Some integration checks use original game assemblies, real Scribe serialization,
and isolated Harmony patches. They may start **new test executable processes**,
not RimWorld itself. Fixtures and test settings are temporary; these checks do
not deliberately access the player's settings or saves.

## What is not covered

The test suite does **not** run a complete RimWorld game, map, crafting job,
construction, caravan, or save/load session. Full gameplay behavior, interactions
with other mods, and the risks of removing large-stack overrides still require
manual testing in a disposable save. In particular, reducing stack limits can
cause loss of oversized quantities; the suite does not normalize them.

See `docs/game-api-notes.md` for the related API findings and high-risk manual
verification scenarios.
