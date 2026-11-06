# RimWorldUtilities — Agent Instructions

## Project
- Game: RimWorld 1.7.4998b (Waylith engine, .NET 10).
- Mod: RimWorldUtilities.
- Platform: Manjaro Linux.
- Development happens on the Linux host using VS Code.
- The game runs through Lutris inside Bubblejail instance `stellaris-sandbox`.
- Project root: `/home/slava/Projects/RimWorldUtilities`.

## Project structure
- `src/`: our mod's C# source code.
- `mod/`: RimWorld mod package, XML definitions and textures.
- `refs/`: original game assemblies (read-only references).
- `refs/decompiled/`: decompiled game sources for research.
- `deploy.sh`: builds and deploys our mod into Bubblejail.

## Decompiled sources
- Decompiled version: RimWorld 1.7.4998b.
- Main source directory: `refs/decompiled/Assembly-CSharp/`.
- Approximately 9255 C# files are available.
- Relevant files:
  - `Verse/Thing.cs`
  - `RimWorld/CompQuality.cs`
  - `RimWorld/CompRottable.cs`

## Rules for game API research
1. Search `refs/decompiled/` before investigating game internals.
2. Use `rg` to find classes, methods, properties and references.
3. Read relevant implementations, not just method signatures.
4. Verify signatures against original assemblies when needed.
5. Decompiled code is for reference only. Never modify it.
6. Do not repeatedly decompile existing assemblies.
7. Never guess RimWorld API behavior when the actual implementation is available.
8. Avoid unnecessary Harmony patches.

## Architecture
- Keep UI components separate from game logic.
- Keep the three main sections:
  - Item Editor
  - Colonist Needs
  - Crafting
- Prefer small, independently testable changes.
- Design Item Editor to support future HP, Quality and Rot Progress editing.
- Do not introduce unnecessary dependencies.

## Development workflow
1. Inspect relevant existing project files.
2. Research game APIs in local decompiled sources.
3. Implement the smallest practical change.
4. Run `dotnet build src/RimWorldUtilities.csproj -c Release`.
5. Fix compilation errors.
6. Summarize changes and provide manual testing steps.

## Restrictions
- Do not edit original game assemblies.
- Do not modify Bubblejail configuration.
- Do not deploy or launch the game unless explicitly requested.
- Do not create Git commits unless explicitly requested.
- Do not change unrelated features.
- Preserve existing working functionality.

## Efficient source code exploration

The decompiled RimWorld source tree contains over 9,000 C# files.
Treat it as a large, read-only reference database.

### Mandatory search-first workflow

1. Never load, concatenate, or read the entire decompiled source tree.
2. Never recursively dump file contents into the conversation.
3. Start by identifying the smallest relevant scope:
   - Known file: open that file directly.
   - Known class: locate its file by name.
   - Known symbol: use `rg` to find its declaration or usage.
   - Unknown location: search progressively, narrowing by directory.
4. Prefer filename searches before content searches.
5. Use `rg -l` to discover matching files without printing their contents.
6. Use `rg -n` to find line numbers and relevant matches.
7. Read only the necessary line ranges using `sed -n`.
8. Expand the inspected range only when required to understand
   surrounding logic, dependencies, or side effects.
9. Never read thousands of files just to understand one API.
10. Avoid generating large terminal outputs.

### Recommended commands

Find files by name:

    rg --files --no-ignore refs/decompiled/Assembly-CSharp |
      rg '/(Thing|CompQuality|CompRottable)\.cs$'

Find files containing a symbol:

    rg -l --no-ignore -F 'stackCount' \
      refs/decompiled/Assembly-CSharp/Verse/

Find relevant lines in a known file:

    rg -n -F 'stackCount' \
      refs/decompiled/Assembly-CSharp/Verse/Thing.cs

Read a specific source range:

    sed -n '120,200p' \
      refs/decompiled/Assembly-CSharp/Verse/Thing.cs

### Context efficiency

- Prefer targeted searches over broad repository scans.
- Do not print entire large source files unless absolutely necessary.
- Limit broad search output to a manageable number of results.
- Do not repeatedly read the same source sections.
- Expand scope gradually when initial searches are insufficient.
- Prefer inspecting implementations over collecting unrelated matches.
- Keep summaries concise and retain only findings relevant
  to the current implementation task.
- Do not create embeddings, vector indexes, or large source summaries
  unless explicitly requested.

### Source research notes

Record reusable findings in `docs/game-api-notes.md` when they are
likely to help future development tasks.

Each note should contain:
- Fully qualified class and member name.
- Relative source file path.
- Relevant API behavior or important side effects.
- Version-specific caveats when applicable.

Do not copy entire decompiled classes into documentation.
Do not treat decompiled source as editable project code.

### Important

The `refs/` directory is excluded from Git.

Use `--no-ignore` when searching decompiled sources with ripgrep,
because the directory is intentionally ignored.

Search decompiled sources only when game API research is relevant
to the task. Ordinary UI or project maintenance changes do not
require exploring game internals.