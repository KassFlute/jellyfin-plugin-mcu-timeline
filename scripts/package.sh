#!/usr/bin/env bash
# Builds the plugin into artifacts/McuTimeline_<version>.zip, ready to be
# unzipped into <config>/plugins/ of a Jellyfin instance.
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
PROJECT="$ROOT/src/Jellyfin.Plugin.McuTimeline/Jellyfin.Plugin.McuTimeline.csproj"

# build.yaml is the single source of truth for the version, the same file the release
# pipeline reads, so a local package and a published one never disagree
read_meta() { grep -oPm1 "(?<=^$1: \")[^\"]+" "$ROOT/build.yaml"; }
VERSION="$(read_meta version)"
TARGET_ABI="$(read_meta targetAbi)"
GUID="$(read_meta guid)"

STAGE="$ROOT/artifacts/McuTimeline_$VERSION"
rm -rf "$STAGE"
mkdir -p "$STAGE"

dotnet publish "$PROJECT" -c Release -p:Version="$VERSION" -o "$ROOT/artifacts/publish"

# only the plugin assembly ships, the host provides the Jellyfin dependencies
cp "$ROOT/artifacts/publish/Jellyfin.Plugin.McuTimeline.dll" "$STAGE/"

cat > "$STAGE/meta.json" <<META
{
    "category": "General",
    "guid": "$GUID",
    "name": "MCU Timeline",
    "description": "Marvel Cinematic Universe timeline in release or story order, with two matching playlists.",
    "overview": "Marvel Cinematic Universe timeline and playlists.",
    "owner": "cassien",
    "targetAbi": "$TARGET_ABI",
    "framework": "net10.0",
    "version": "$VERSION",
    "changelog": "",
    "timestamp": "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
}
META

( cd "$ROOT/artifacts" && rm -f "McuTimeline_$VERSION.zip" \
  && zip -qr "McuTimeline_$VERSION.zip" "McuTimeline_$VERSION" )

echo "Package: $ROOT/artifacts/McuTimeline_$VERSION.zip"
