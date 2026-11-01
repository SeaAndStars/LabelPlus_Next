#!/usr/bin/env bash
# Build macOS installers (.app bundle inside a compressed .dmg, plus a .zip).
#
# The DMG is produced on Linux with libdmg-hfsplus: a real HFS+ volume is
# created, populated (permissions + /Applications symlink preserved) and then
# converted into a UDIF image. macOS mounts it like any other .dmg.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/../lib/common.sh"

need_cmd mkfs.hfsplus
need_cmd zip
need_cmd python3

"$ROOT/packaging/lib/build-dmg-tools.sh"
DMGTOOLS="$TOOLS_DIR/dmg-tools"

ICON_DIR="$STAGE_DIR/icons-macos"
make_icons "$ICON_DIR"
python3 "$ROOT/packaging/macos/make-icns.py" "$ICON_DIR" "$STAGE_DIR/icon-macos.icns" >/dev/null
[ -s "$STAGE_DIR/icon-macos.icns" ] || die "failed to build icon.icns"

RIDS="${MACOS_RIDS:-osx-x64 osx-arm64}"

for rid in $RIDS; do
  arch="${rid#osx-}"
  base="$STAGE_DIR/macos-$rid"
  payload="$base/payload"
  app="$base/LabelPlus_Next.app"
  root="$base/dmgroot"
  img="$base/LabelPlus_Next.hfs"
  out_dmg="$OUT_DIR/LabelPlus_Next-${VERSION}-macos-${arch}.dmg"
  out_zip="$OUT_DIR/LabelPlus_Next-${VERSION}-macos-${arch}.zip"

  stage_payload "$rid" "$payload"

  rm -rf "$app" "$root" "$img"
  mkdir -p "$app/Contents/MacOS" "$app/Contents/Resources" "$root"
  cp -a "$payload"/. "$app/Contents/MacOS/"
  mv "$app/Contents/MacOS/LICENSE.md" "$app/Contents/Resources/LICENSE.md"
  install -m 0644 "$STAGE_DIR/icon-macos.icns" "$app/Contents/Resources/icon.icns"
  printf 'APPL????' > "$app/Contents/PkgInfo"
  sed "s|@VERSION@|${VERSION}|g" "$ROOT/packaging/macos/Info.plist.in" \
    > "$app/Contents/Info.plist"
  chmod 0755 "$app/Contents/MacOS/LabelPlus_Next.Desktop" \
             "$app/Contents/MacOS/update/LabelPlus_Next.Update"

  cp -a "$app" "$root/LabelPlus_Next.app"

  cat > "$root/README.txt" <<EOF
LabelPlus Next ${VERSION} (macOS ${arch})
================================================

安装 / Install
  1. 将 LabelPlus_Next.app 拖入右侧的 Applications 文件夹。
     Drag "LabelPlus_Next.app" onto the Applications folder.
  2. 首次打开：在"应用程序"中右键点击 LabelPlus Next -> 打开。
     若提示"已损坏"或"无法验证开发者"，请在终端执行：
       xattr -dr com.apple.quarantine "/Applications/LabelPlus_Next.app"
     First launch: right-click the app in Applications -> Open.
     If macOS reports the app is damaged/unidentified, run the xattr
     command above once.

说明 / Notes
  * 应用未使用 Apple 开发者证书签名或公证，需按上述方式放行一次。
    The build is unsigned and not notarized.
  * 应用内自动更新要求应用所在目录可写；放在 ~/Applications 可直接更新，
    放在 /Applications 时可能需要管理员权限。
    In-app updates require a writable install location; ~/Applications works,
    /Applications may require administrator rights.
  * 用户配置保存在 ~/Library/Application Support/LabelPlus_Next。
    User configuration lives in ~/Library/Application Support/LabelPlus_Next.
EOF

  content_kb="$(du -sk "$root" | cut -f1)"
  image_mb=$(( content_kb / 1024 + content_kb / 1024 / 5 + 64 ))
  log "creating HFS+ volume for $rid (${image_mb} MiB)"
  dd if=/dev/zero of="$img" bs=1M count="$image_mb" status=none
  mkfs.hfsplus -v "LabelPlus Next" "$img" >/dev/null

  "$DMGTOOLS/hdutil" "$img" addall "$root" / > "$LOGS/hdutil-add-$rid.log" 2>&1 \
    || die "hdutil addall failed for $rid"
  "$DMGTOOLS/hdutil" "$img" symlink /Applications /Applications >> "$LOGS/hdutil-add-$rid.log" 2>&1 \
    || die "hdutil symlink failed for $rid"

  # libdmg's addall writes regular files as 0644; restore the executable bit
  # for everything that is executable in the staged bundle.
  while IFS= read -r -d '' f; do
    rel="/${f#"$root"/}"
    "$DMGTOOLS/hdutil" "$img" chmod 0755 "$rel" >/dev/null
  done < <(find "$root" -type f -perm -u+x -print0)

  log "building $(basename "$out_dmg")"
  "$DMGTOOLS/dmg" build "$img" "$out_dmg" > "$LOGS/dmg-$rid.log" 2>&1 \
    || { tail -20 "$LOGS/dmg-$rid.log" >&2; die "dmg build failed for $rid"; }
  chmod 0644 "$out_dmg"

  # Verify the image really contains the bundle, the symlink and the updater.
  "$DMGTOOLS/hdutil" "$out_dmg" ls / > "$base/dmg-listing.txt" 2>&1 || true
  grep -q "LabelPlus_Next.app" "$base/dmg-listing.txt" \
    || die "DMG for $rid is missing the app bundle"
  grep -q "120644.*Applications" "$base/dmg-listing.txt" \
    || die "DMG for $rid is missing the /Applications symlink"
  "$DMGTOOLS/hdutil" "$out_dmg" ls /LabelPlus_Next.app/Contents/MacOS/update \
    > "$base/dmg-update-listing.txt" 2>&1 || true
  grep -q "LabelPlus_Next.Update" "$base/dmg-update-listing.txt" \
    || die "DMG for $rid is missing the updater"

  ( cd "$root" && zip -q -r -y "$out_zip" "LabelPlus_Next.app" "README.txt" )
  log "built $(basename "$out_dmg") and $(basename "$out_zip")"
done

log "macOS installers written to $OUT_DIR"
