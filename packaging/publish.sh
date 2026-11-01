#!/usr/bin/env bash
# Publish the desktop client and updater for every supported RID.
#
# Output layout:
#   artifacts/publish/<rid>/desktop/   -> LabelPlus_Next.Desktop payload
#   artifacts/publish/<rid>/updater/   -> LabelPlus_Next.Update payload
#
# Usage:
#   packaging/publish.sh                 # all RIDs
#   RIDS="win-x64 linux-x64" packaging/publish.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT"

VERSION="${VERSION:-$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
  "$ROOT/LabelPlus_Next/Client.version.json" | head -1)}"
VERSION="${VERSION:-0.0.0}"

ALL_RIDS=(win-x64 win-arm64 linux-x64 linux-arm64 osx-x64 osx-arm64)
RIDS="${RIDS:-${ALL_RIDS[*]}}"
OUT="$ROOT/artifacts/publish"
LOGS="$ROOT/artifacts/logs"
mkdir -p "$OUT" "$LOGS"

# Packaging does not need symbols; dropping them keeps installers much smaller.
COMMON_ARGS=(
  -c Release
  -p:PublishSingleFile=true
  -p:SelfContained=true
  -p:PublishTrimmed=false
  -p:EnableCompressionInSingleFile=true
  -p:DebugType=None
  -p:DebugSymbols=false
  -p:Version="$VERSION"
  -p:InformationalVersion="$VERSION"
  --nologo
)

publish_one() {
  local project="$1" rid="$2" out="$3" log="$4"
  rm -rf "$out"
  mkdir -p "$out"
  echo "==> publish $project ($rid) -> $out"
  dotnet publish "$project" \
    -r "$rid" \
    -o "$out" \
    "${COMMON_ARGS[@]}" 2>&1 | tee "$log"
}

for rid in $RIDS; do
  publish_one "LabelPlus_Next.Desktop/LabelPlus_Next.Desktop.csproj" "$rid" \
    "$OUT/$rid/desktop" "$LOGS/publish-desktop-$rid.log"
  publish_one "LabelPlus_Next.Update/LabelPlus_Next.Update.csproj" "$rid" \
    "$OUT/$rid/updater" "$LOGS/publish-updater-$rid.log"
done

echo "==> publish finished for: $RIDS (version $VERSION)"
