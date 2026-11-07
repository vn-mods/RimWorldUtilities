#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
VERSION="${1:?Usage: ./package.sh VERSION (e.g. 0.1.0)}"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][A-Za-z0-9.-]+)?$ ]]; then
    echo "ERROR: Invalid version: $VERSION" >&2
    exit 1
fi

DLL="$ROOT/src/bin/Release/net10.0/RimWorldUtilities.dll"
ARCHIVE="$ROOT/dist/RimWorldUtilities-v${VERSION}.zip"

echo "==> Building..."
dotnet build "$ROOT/src/RimWorldUtilities.csproj" -c Release

if [[ ! -f "$DLL" ]]; then
    echo "ERROR: Built DLL not found: $DLL" >&2
    exit 1
fi

echo "==> Packaging..."
TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

mkdir -p "$TMP/RimWorldUtilities"
mkdir -p "$ROOT/dist"

# Copy the mod package, without source code or local references.
cp -a "$ROOT/mod/." "$TMP/RimWorldUtilities/"

# Ensure that the archive contains the freshly built DLL.
install -Dm644 \
    "$DLL" \
    "$TMP/RimWorldUtilities/1.7/Assemblies/RimWorldUtilities.dll"

rm -f "$ARCHIVE"

(
    cd "$TMP"
    zip -qr "$ARCHIVE" RimWorldUtilities
)

echo "==> Package created:"
echo "$ARCHIVE"
