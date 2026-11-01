#!/usr/bin/env bash
# Build the Linux-side macOS DMG tools (hdutil + dmg) from
# https://github.com/planetbeing/libdmg-hfsplus.
#
# hdutil is used to populate a real HFS+ volume (permissions + symlinks),
# dmg converts that volume into a compressed UDIF (.dmg) image.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/../lib/common.sh"

DEST="$TOOLS_DIR/dmg-tools"
if [ -x "$DEST/dmg" ] && [ -x "$DEST/hdutil" ]; then
  log "dmg tools already built: $DEST"
  exit 0
fi

need_cmd git
need_cmd cmake
need_cmd python3
[ -f /usr/include/openssl/hmac.h ] || warn "libssl-dev headers not found; the DMG tools need OpenSSL headers"

SRC="$ARTIFACTS/build/libdmg-hfsplus"
rm -rf "$SRC"
log "cloning libdmg-hfsplus"
git clone --quiet --depth 1 https://github.com/planetbeing/libdmg-hfsplus.git "$SRC"

log "patching FileVault code for OpenSSL 3"
python3 - "$SRC" <<'PY'
import sys, pathlib
src = pathlib.Path(sys.argv[1])

h = src / "includes/dmg/filevault.h"
s = h.read_text()
s = s.replace("\tHMAC_CTX\thmacCTX;", "\tHMAC_CTX*\thmacCTX;")
h.write_text(s)

c = src / "dmg/filevault.c"
s = c.read_text()
s = s.replace("HMAC_Init_ex(&(info->hmacCTX)", "HMAC_Init_ex(info->hmacCTX")
s = s.replace("HMAC_Update(&(info->hmacCTX)", "HMAC_Update(info->hmacCTX")
s = s.replace("HMAC_Final(&(info->hmacCTX)", "HMAC_Final(info->hmacCTX")
s = s.replace("HMAC_CTX_cleanup(&(info->hmacCTX));",
              "HMAC_CTX_free(info->hmacCTX);\n\tinfo->hmacCTX = NULL;")
s = s.replace("HMAC_CTX_init(&(info->hmacCTX));", "info->hmacCTX = HMAC_CTX_new();")
c.write_text(s)
PY

log "building dmg tools"
cmake -S "$SRC" -B "$SRC/build" \
  -DCMAKE_BUILD_TYPE=Release \
  -DCMAKE_POLICY_VERSION_MINIMUM=3.5 > "$LOGS/dmg-tools-cmake.log" 2>&1
cmake --build "$SRC/build" -j"$(nproc)" > "$LOGS/dmg-tools-build.log" 2>&1

mkdir -p "$DEST"
cp "$SRC/build/dmg/dmg" "$SRC/build/hdutil/hdutil" "$DEST/"
log "dmg tools installed: $DEST"
