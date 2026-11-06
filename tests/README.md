# Focused stack-limit checks

Run the dependency-free console test suite from the project root:

```sh
dotnet run --project tests/RimWorldUtilities.Tests/RimWorldUtilities.Tests.csproj -c Release
```

The core checks cover integer validation, explicit opt-in, original-limit capture,
failed writes, unsupported definitions, higher limits from other mods, and limits
that must never decrease. They use isolated definition fixtures, not the global
game definition database.

To also exercise the actual Scribe serializer, atomic settings replacement, and
loading those settings in a fresh test process, provide the installed game DLL
directory:

```sh
RIMWORLD_GAME_DIR="/path/to/RimWorld v1.7.4998b" \
  dotnet run --project tests/RimWorldUtilities.Tests/RimWorldUtilities.Tests.csproj -c Release
```

These integration checks read the game's runtime DLLs for Scribe's logging
dependency. They initialize only in-memory default preferences, write settings
under a temporary directory, and delete the fixtures afterward. They do not
launch RimWorld or read/write the player's settings or saves. Without that
environment variable, native serialization/restart checks are explicitly skipped.

The fresh process is the test executable, not RimWorld. Actual in-game save/load,
map spawning, hauling, splitting, and merging still require manual verification.
