# RimWorldUtilities — Agent Instructions

## Project

RimWorldUtilities is a C# mod for RimWorld 1.7 (Waylith engine, .NET 10).

The mod provides three main features:
- Item Editor
- Colonist Needs
- Crafting

## Repository structure

- `src/` — C# source code.
- `mod/` — distributable mod files, XML definitions, and assets.
- `refs/` — local game assemblies and decompiled sources (read-only, excluded from Git).
- `docs/` — project documentation and API research notes.
- `deploy.sh` — local build and deployment script.

## Development guidelines

- Inspect existing code before making changes.
- Keep UI components separate from game logic.
- Prefer small, focused changes.
- Preserve existing functionality and avoid unrelated modifications.
- Follow existing project conventions.
- Avoid unnecessary dependencies and Harmony patches.
- Do not modify original game assemblies or decompiled sources.

## Game API research

When investigating RimWorld internals:

- Consult local sources in `refs/decompiled/Assembly-CSharp/` before making assumptions.
- Use targeted `rg` searches to locate relevant classes, members, and implementations.
- Use `--no-ignore` when searching within `refs/`.
- Read only relevant files and source ranges. Never dump the entire decompiled tree.
- Verify API behavior and signatures against actual implementations when necessary.
- Do not repeat decompilation of existing assemblies.
- Record reusable findings in `docs/game-api-notes.md`.

Do not explore game internals for tasks that do not require API research.

## Build and validation

Build the project with:

```bash
dotnet build src/RimWorldUtilities.csproj -c Release
```

After making changes:

1. Run the build and resolve compilation errors.
2. Review the diff for unintended changes.
3. Summarize the implementation and any remaining limitations.
4. Provide manual in-game testing steps when relevant.

A successful build does not guarantee correct in-game behavior.

## Safety and scope

- Do not modify `refs/` or commit game assemblies or decompiled sources.
- Do not modify local sandbox or game configurations.
- Do not deploy or launch the game unless explicitly requested.
- Do not create Git commits or push changes unless explicitly requested.
- Do not change unrelated features or remove working functionality.

## Documentation

Keep user-facing documentation accurate and consistent with implemented functionality. Do not describe planned features as already available.