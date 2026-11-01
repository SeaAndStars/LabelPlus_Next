#!/usr/bin/env bash
# Build Windows installers with NSIS (runs on Linux, cross-compiles the
# installer stub).
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/../lib/common.sh"

need_cmd makensis

RIDS="${WINDOWS_RIDS:-win-x64 win-arm64}"

for rid in $RIDS; do
  stage="$STAGE_DIR/windows-$rid"
  stage_payload "$rid" "$stage" ".exe"
  size_kb="$(du -sk "$stage" | cut -f1)"
  arch="${rid#win-}"
  out="$OUT_DIR/LabelPlus_Next-${VERSION}-${rid}-setup.exe"

  log "building $(basename "$out")"
  makensis -V2 \
    -DAPPVERSION="$VERSION" \
    -DAPPARCH="$arch" \
    -DSRCDIR="$stage" \
    -DOUTFILE="$out" \
    -DICONFILE="$ICON_ICO" \
    -DAPPSIZEKB="$size_kb" \
    "$ROOT/packaging/windows/installer.nsi" > "$LOGS/nsis-$rid.log" 2>&1 \
    || { tail -30 "$LOGS/nsis-$rid.log" >&2; die "makensis failed for $rid"; }

  [ -s "$out" ] || die "installer not produced for $rid"
  log "built $(basename "$out") ($(du -h "$out" | cut -f1))"
done

log "Windows installers written to $OUT_DIR"
