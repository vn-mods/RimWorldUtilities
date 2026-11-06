#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

GAME="$HOME/.local/share/bubblejail/instances/stellaris-sandbox/home/Games/riwmorld/drive_c/Games/RimWorld v1.7.4998b"

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

echo "==> Deploying into Bubblejail..."
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

echo "==> Deployment complete!"
echo "Installed to: $DEST"
