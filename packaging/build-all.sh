#!/usr/bin/env bash
# Build every installer for the current release.
#
#   packaging/build-all.sh                 # publish + package all platforms
#   SKIP_PUBLISH=1 packaging/build-all.sh  # package the existing artifacts
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/lib/common.sh"

if [ "${SKIP_PUBLISH:-0}" != "1" ]; then
  "$ROOT/packaging/publish.sh"
fi

"$ROOT/packaging/windows/build-windows.sh"
"$ROOT/packaging/linux/build-linux.sh"
"$ROOT/packaging/macos/build-macos.sh"

log "writing checksums"
(
  cd "$OUT_DIR"
  find . -maxdepth 1 -type f ! -name 'SHA256SUMS.txt' -printf '%f\n' | sort \
    | xargs -r sha256sum > SHA256SUMS.txt
)

printf '\n%-58s %10s\n' "ARTIFACT" "SIZE"
printf '%s\n' "--------------------------------------------------------------------------"
(
  cd "$OUT_DIR"
  find . -maxdepth 1 -type f ! -name 'SHA256SUMS.txt' -printf '%f\n' | sort \
    | while IFS= read -r f; do printf '%-58s %10s\n' "$f" "$(du -h "$f" | cut -f1)"; done
)

log "done: $OUT_DIR"
