#!/usr/bin/env bash
# Builds the plugin for one Jellyfin version into artifacts/McuTimeline_<version>.zip,
# ready to be unzipped into <config>/plugins/ of a Jellyfin instance.
#   ./scripts/package.sh          Jellyfin 10.11
#   ./scripts/package.sh 10.10    or 10.9
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Jellyfin.Plugin.McuTimeline/Jellyfin.Plugin.McuTimeline.csproj"

ABI="${1:-10.11}"
case "$ABI" in
    10.11) FRAMEWORK=net9.0 ;;
    10.9|10.10) FRAMEWORK=net8.0 ;;
    *) echo "Unsupported Jellyfin version $ABI, expected 10.9, 10.10 or 10.11" >&2; exit 1 ;;
esac

# build.yaml is the single source of truth for the release number, the same file the
# release pipeline reads, so a local package and a published one never disagree
read_meta() { grep -oPm1 "(?<=^$1: \")[^\"]+" "$ROOT/build.yaml"; }
VERSION="$(read_meta version).${ABI#10.}"
GUID="$(read_meta guid)"

STAGE="$ROOT/artifacts/McuTimeline_$VERSION"
rm -rf "$STAGE" "$ROOT/artifacts/publish"
mkdir -p "$STAGE"

dotnet publish "$PROJECT" -c Release -p:Version="$VERSION" -p:JellyfinAbi="$ABI" -o "$ROOT/artifacts/publish"

# only the plugin assembly ships, the host provides the Jellyfin dependencies
cp "$ROOT/artifacts/publish/Jellyfin.Plugin.McuTimeline.dll" "$ROOT/logo.png" "$STAGE/"

cat > "$STAGE/meta.json" <<META
{
    "category": "General",
    "guid": "$GUID",
    "name": "MCU Timeline",
    "description": "Marvel Cinematic Universe timeline in release or story order, with matching playlists and a collection.",
    "overview": "Marvel Cinematic Universe timeline, playlists and collection.",
    "owner": "cassien",
    "targetAbi": "$ABI.0.0",
    "framework": "$FRAMEWORK",
    "version": "$VERSION",
    "changelog": "",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
META

( cd "$ROOT/artifacts" && rm -f "McuTimeline_$VERSION.zip" \
  && zip -qr "McuTimeline_$VERSION.zip" "McuTimeline_$VERSION" )

echo "Package: $ROOT/artifacts/McuTimeline_$VERSION.zip"
