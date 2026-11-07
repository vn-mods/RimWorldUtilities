#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Load local configuration unless an environment override is provided.
if [[ -z "${RIMWORLD_GAME_DIR:-}" && -f "$ROOT/.env" ]]; then
    source "$ROOT/.env"
fi

if [[ -z "${RIMWORLD_GAME_DIR:-}" ]]; then
    echo "ERROR: RIMWORLD_GAME_DIR is not configured." >&2
    echo "Copy .env.example to .env and set your game path." >&2
    exit 1
fi

GAME="${RIMWORLD_GAME_DIR%/}"
DEST="$GAME/Mods/RimWorldUtilities"

if [[ ! -f "$GAME/Assembly-CSharp.dll" ]]; then
    echo "ERROR: Game directory not found!"
    exit 1
fi

echo "==> Building mod..."
dotnet build "$ROOT/src/RimWorldUtilities.csproj" -c Release

echo "==> Preparing mod files..."
install -Dm644 \
    "$ROOT/src/bin/Release/net10.0/RimWorldUtilities.dll" \
    "$ROOT/mod/1.7/Assemblies/RimWorldUtilities.dll"

echo "==> Deploying mod..."
install -Dm644 \
    "$ROOT/mod/About/About.xml" \
    "$DEST/About/About.xml"

install -Dm644 \
    "$ROOT/mod/1.7/Assemblies/RimWorldUtilities.dll" \
    "$DEST/1.7/Assemblies/RimWorldUtilities.dll"

while IFS= read -r -d '' def_file; do
    install -Dm644 \
        "$def_file" \
        "$DEST/1.7/Defs/${def_file#"$ROOT/mod/1.7/Defs/"}"
done < <(find "$ROOT/mod/1.7/Defs" -type f -name '*.xml' -print0)

# Deploy custom textures
if [[ -d "$ROOT/mod/Textures" ]]; then
    mkdir -p "$DEST/Textures"
    cp -a "$ROOT/mod/Textures/." "$DEST/Textures/"
fi

echo "==> Deployment complete!"
echo "Installed to: $DEST"
