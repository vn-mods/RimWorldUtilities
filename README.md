# RimWorldUtilities

A small collection of quality-of-life utilities for RimWorld 1.7.

## Requirements

- Harmony

## Features

- **Item Editor** — Edit item stack counts, hit points, and quality.
- **Colonist Needs** — Keep selected colonist needs full.
- **Craft / Build / Research** — Speed up crafting, construction, and research (up to 20×).
- **Always Legendary** — Optionally make crafted items and constructed furniture legendary.
- **Abilities → Disable Cooldowns** — Bypass standard ability and Leader Speech/Conversion Ritual cooldowns for free, spawned player colonists.

## Installation

1. Install and enable **Harmony**.
2. Download a packaged release of RimWorldUtilities.
3. Extract the mod into your RimWorld `Mods/` directory.
4. Enable **RimWorldUtilities** after Harmony in the game's mod list.

## Access

Open the **Utilities** tab using the button in the bottom-right corner of the game screen.

![Utilities button](mod/Textures/RimWorldUtilities/Utilities.png)

## Installation

1. Install and enable [Harmony](https://steamcommunity.com/workshop/filedetails/?id=2009463077).
2. Download the latest ZIP from [GitHub Releases](../../releases).
3. Extract it into your RimWorld `Mods/` directory.
4. Enable RimWorldUtilities in the game's mod list.

## Development

Requires .NET 10 and local RimWorld/Harmony assemblies in `refs/`.

Build:

```bash
dotnet build src/RimWorldUtilities.csproj -c Release
```

To deploy locally, configure `RIMWORLD_GAME_DIR` in `.env` (see `.env.example`) and run `./deploy.sh`.

To create a distributable ZIP:

```bash
./package.sh 0.1.0
```

Test instructions: [tests/README.md](tests/README.md).
