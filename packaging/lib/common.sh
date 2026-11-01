#!/usr/bin/env bash
# Shared helpers for the LabelPlus Next installer builds.
set -euo pipefail

ROOT="${ROOT:-$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)}"
VERSION="${VERSION:-$(sed -n 's/.*"version"[[:space:]]*:[[:space:]]*"\([^"]*\)".*/\1/p' \
  "$ROOT/LabelPlus_Next/Client.version.json" | head -1)}"
VERSION="${VERSION:-0.0.0}"

PRODUCT_NAME="LabelPlus Next"
SLUG="labelplus-next"
APP_ID="LabelPlus_Next"
PUBLISHER="SeaAndStars"
HOMEPAGE="https://github.com/SeaAndStars/LabelPlus_Next"
MAINTAINER="SeaAndStars <SeaAndStars@users.noreply.github.com>"

ARTIFACTS="$ROOT/artifacts"
PUBLISH_DIR="$ARTIFACTS/publish"
STAGE_DIR="$ARTIFACTS/stage"
TOOLS_DIR="$ARTIFACTS/tools"
LOGS="$ARTIFACTS/logs"
OUT_DIR="${OUT_DIR:-$ROOT/release/installers}"

ICON_ICO="$ROOT/LabelPlus_Next.Desktop/LabelPlus_Next_rounded_icon_transparent.ico"
DESKTOP_BIN="LabelPlus_Next.Desktop"
UPDATER_BIN="LabelPlus_Next.Update"

mkdir -p "$OUT_DIR" "$STAGE_DIR" "$TOOLS_DIR" "$LOGS"

log()  { printf '\033[1;34m==>\033[0m %s\n' "$*"; }
warn() { printf '\033[1;33m!!\033[0m %s\n' "$*" >&2; }
die()  { printf '\033[1;31mERROR:\033[0m %s\n' "$*" >&2; exit 1; }

need_cmd() {
  command -v "$1" >/dev/null 2>&1 || die "missing required command: $1"
}

require_publish() {
  local rid="$1"
  # Action artifacts do not reliably preserve the executable bit, so only
  # check for presence here; stage_payload sets the modes explicitly.
  [ -f "$PUBLISH_DIR/$rid/desktop/$DESKTOP_BIN" ] || [ -f "$PUBLISH_DIR/$rid/desktop/$DESKTOP_BIN.exe" ] \
    || die "missing publish output for $rid; run packaging/publish.sh first"
  [ -f "$PUBLISH_DIR/$rid/updater/$UPDATER_BIN" ] || [ -f "$PUBLISH_DIR/$rid/updater/$UPDATER_BIN.exe" ] \
    || die "missing updater publish output for $rid; run packaging/publish.sh first"
}

# Copy the client + updater payload into one directory. The updater must live in
# an "update/" subfolder: that is where the client looks for it at runtime.
stage_payload() {
  local rid="$1" dest="$2" exe_suffix="${3:-}"
  local app="$PUBLISH_DIR/$rid/desktop"
  local upd="$PUBLISH_DIR/$rid/updater"

  require_publish "$rid"
  rm -rf "$dest"
  mkdir -p "$dest/update"
  cp -a "$app"/. "$dest"/
  cp -a "$upd"/. "$dest/update"/
  find "$dest" -name '*.pdb' -delete
  cp "$ROOT/LICENSE.md" "$dest/LICENSE.md"
  chmod 0755 "$dest/$DESKTOP_BIN$exe_suffix"
  chmod 0755 "$dest/update/$UPDATER_BIN$exe_suffix"
}

# Extract PNG icons from the multi-resolution .ico in the repository.
# Writes 16/24/32/48/64/128/256/512/1024 px PNGs to $1.
make_icons() {
  local out="$1"
  mkdir -p "$out"
  local sizes=(16 24 32 48 64 128 256)
  local frames=(0 1 2 3 4 5 6)
  local i
  for i in "${!sizes[@]}"; do
    convert "${ICON_ICO}[${frames[$i]}]" -strip \
      "$out/icon-${sizes[$i]}.png"
  done
  convert "$out/icon-256.png" -filter Lanczos -resize 512x512 -strip "$out/icon-512.png"
  convert "$out/icon-256.png" -filter Lanczos -resize 1024x1024 -strip "$out/icon-1024.png"
}

icon_png() { # $1 = icon dir, $2 = size
  printf '%s/icon-%s.png\n' "$1" "$2"
}
