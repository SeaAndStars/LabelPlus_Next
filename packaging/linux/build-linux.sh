#!/usr/bin/env bash
# Build Linux installers: .deb, .rpm, .AppImage and a portable .tar.gz.
set -euo pipefail

source "$(dirname "${BASH_SOURCE[0]}")/../lib/common.sh"

need_cmd dpkg-deb
need_cmd rpmbuild
need_cmd convert
need_cmd tar
need_cmd mksquashfs

APPIMAGETOOL="$TOOLS_DIR/appimagetool-x86_64.AppImage"
if [ ! -x "$APPIMAGETOOL" ]; then
  need_cmd curl
  log "downloading appimagetool"
  curl -fL --retry 3 --silent --show-error -o "$APPIMAGETOOL" \
    https://github.com/AppImage/appimagetool/releases/download/continuous/appimagetool-x86_64.AppImage
  chmod 0755 "$APPIMAGETOOL"
fi
for run_arch in x86_64 aarch64; do
  if [ ! -s "$TOOLS_DIR/runtime-$run_arch" ]; then
    need_cmd curl
    log "downloading AppImage runtime ($run_arch)"
    curl -fL --retry 3 --silent --show-error -o "$TOOLS_DIR/runtime-$run_arch" \
      "https://github.com/AppImage/type2-runtime/releases/download/continuous/runtime-$run_arch"
  fi
done

ICON_DIR="$STAGE_DIR/icons-linux"
make_icons "$ICON_DIR"

RIDS="${LINUX_RIDS:-linux-x64 linux-arm64}"

deb_arch_for()   { case "$1" in linux-x64) echo amd64 ;; linux-arm64) echo arm64 ;; *) die "unknown rid $1" ;; esac; }
rpm_arch_for()   { case "$1" in linux-x64) echo x86_64 ;; linux-arm64) echo aarch64 ;; *) die "unknown rid $1" ;; esac; }
appimage_arch_for() { case "$1" in linux-x64) echo x86_64 ;; linux-arm64) echo aarch64 ;; *) die "unknown rid $1" ;; esac; }

write_desktop() {
  local exec_line="$1" out="$2"
  cat > "$out" <<EOF
[Desktop Entry]
Type=Application
Version=1.0
Name=LabelPlus Next
GenericName=Comic Translation Workbench
GenericName[zh_CN]=漫画翻译工作台
Comment=Annotate, translate, proofread and publish comic translations
Comment[zh_CN]=标注、翻译、校对并发布漫画译文
Exec=${exec_line}
Icon=labelplus-next
Terminal=false
StartupNotify=true
StartupWMClass=LabelPlus_Next.Desktop
Categories=Graphics;Office;
MimeType=x-scheme-handler/labelplus;
Keywords=comic;manga;translation;label;
EOF
}

install_icons_deb() { # dest-root size
  local root="$1"
  for s in 16 24 32 48 64 128 256; do
    install -D -m 0644 "$ICON_DIR/icon-$s.png" \
      "$root/usr/share/icons/hicolor/${s}x${s}/apps/labelplus-next.png"
  done
}

build_deb() {
  local rid="$1" arch payload
  arch="$(deb_arch_for "$rid")"
  payload="$STAGE_DIR/linux-$rid/payload"
  local root="$STAGE_DIR/deb-$rid"
  rm -rf "$root"
  mkdir -p "$root/DEBIAN" "$root/usr/lib/labelplus-next" "$root/usr/bin" \
    "$root/usr/share/applications" "$root/usr/share/doc/labelplus-next"

  cp -a "$payload"/. "$root/usr/lib/labelplus-next/"
  ln -sfn /usr/lib/labelplus-next/LabelPlus_Next.Desktop "$root/usr/bin/labelplus-next"
  write_desktop "/usr/lib/labelplus-next/LabelPlus_Next.Desktop %U" \
    "$root/usr/share/applications/labelplus-next.desktop"
  install_icons_deb "$root"
  install -m 0644 "$ROOT/LICENSE.md" "$root/usr/share/doc/labelplus-next/copyright"

  local installed_size
  installed_size="$(du -sk "$root/usr" | cut -f1)"

  cat > "$root/DEBIAN/control" <<EOF
Package: labelplus-next
Version: ${VERSION}
Architecture: ${arch}
Maintainer: ${MAINTAINER}
Installed-Size: ${installed_size}
Depends: libc6 (>= 2.31), libgcc-s1 | libgcc1, libstdc++6, libssl3t64 | libssl3, libicu78 | libicu76 | libicu74 | libicu72 | libicu70, libx11-6, libxext6, libxrandr2, libxi6, libxcursor1, libice6, libsm6, libfontconfig1, libfreetype6, libgl1, libegl1, zlib1g
Section: graphics
Priority: optional
Homepage: ${HOMEPAGE}
Description: LabelPlus Next - comic translation workbench
 Annotate images, translate and proofread comic pages, collaborate with
 a team and publish the result. Ships as a self-contained .NET desktop
 application (no system-wide .NET runtime required).
EOF

  install -m 0755 "$ROOT/packaging/linux/postinst" "$root/DEBIAN/postinst"
  install -m 0755 "$ROOT/packaging/linux/postrm" "$root/DEBIAN/postrm"

  local out="$OUT_DIR/labelplus-next_${VERSION}_${arch}.deb"
  dpkg-deb --root-owner-group -Zxz --build "$root" "$out" >/dev/null
  log "built $(basename "$out")"
}

build_rpm() {
  local rid="$1" arch payload top
  arch="$(rpm_arch_for "$rid")"
  payload="$STAGE_DIR/linux-$rid/payload"
  top="$STAGE_DIR/rpm-$rid"
  rm -rf "$top"
  mkdir -p "$top"/{BUILD,RPMS,SOURCES,SPECS,SRPMS}
  cp -a "$payload" "$top/SOURCES/payload"
  write_desktop "/usr/lib/labelplus-next/LabelPlus_Next.Desktop %U" \
    "$top/SOURCES/labelplus-next.desktop"
  mkdir -p "$top/SOURCES/icons"
  for s in 16 24 32 48 64 128 256; do
    install -m 0644 "$ICON_DIR/icon-$s.png" "$top/SOURCES/icons/icon-$s.png"
  done
  cp "$ROOT/LICENSE.md" "$top/SOURCES/LICENSE.md"

  sed -e "s|@VERSION@|${VERSION}|g" \
      -e "s|@RPMARCH@|${arch}|g" \
      -e "s|@HOMEPAGE@|${HOMEPAGE}|g" \
      "$ROOT/packaging/linux/labelplus-next.spec.in" > "$top/SPECS/labelplus-next.spec"

  rpmbuild -bb \
    --define "_topdir $top" \
    --define "_binary_payload w9.xzdio" \
    --define "debug_package %{nil}" \
    --define "__os_install_post %{nil}" \
    --target "$arch" \
    "$top/SPECS/labelplus-next.spec" > "$LOGS/rpm-$rid.log" 2>&1 \
    || { tail -30 "$LOGS/rpm-$rid.log" >&2; die "rpmbuild failed for $rid"; }

  local built
  built="$(find "$top/RPMS" -name '*.rpm' -type f | head -1)"
  [ -n "$built" ] || die "rpm not produced for $rid"
  cp "$built" "$OUT_DIR/labelplus-next-${VERSION}-1.${arch}.rpm"
  log "built labelplus-next-${VERSION}-1.${arch}.rpm"
}

build_appimage() {
  local rid="$1" arch payload app
  arch="$(appimage_arch_for "$rid")"
  payload="$STAGE_DIR/linux-$rid/payload"
  app="$STAGE_DIR/AppImage-$rid/LabelPlus_Next.AppDir"
  rm -rf "$STAGE_DIR/AppImage-$rid"
  mkdir -p "$app/usr/lib/labelplus-next"

  cp -a "$payload"/. "$app/usr/lib/labelplus-next/"
  write_desktop "LabelPlus_Next.Desktop %U" "$app/labelplus-next.desktop"
  install -m 0644 "$ICON_DIR/icon-256.png" "$app/labelplus-next.png"

  cat > "$app/AppRun" <<'EOF'
#!/bin/sh
set -e
HERE="$(dirname "$(readlink -f "$0")")"
exec "$HERE/usr/lib/labelplus-next/LabelPlus_Next.Desktop" "$@"
EOF
  chmod 0755 "$app/AppRun"
  chmod -R a+rX "$app"

  local out="$OUT_DIR/LabelPlus_Next-${VERSION}-linux-${rid#linux-}.AppImage"
  rm -f "$out"
  ARCH="$arch" APPIMAGE_EXTRACT_AND_RUN=1 "$APPIMAGETOOL" -n \
    --runtime-file "$TOOLS_DIR/runtime-$arch" \
    "$app" "$out" > "$LOGS/appimage-$rid.log" 2>&1 \
    || { tail -30 "$LOGS/appimage-$rid.log" >&2; die "appimagetool failed for $rid"; }
  chmod 0755 "$out"
  log "built $(basename "$out")"
}

build_tarball() {
  local rid="$1" payload dir
  payload="$STAGE_DIR/linux-$rid/payload"
  dir="$STAGE_DIR/tar-$rid/LabelPlus_Next-${VERSION}-linux-${rid#linux-}"
  rm -rf "$STAGE_DIR/tar-$rid"
  mkdir -p "$dir"
  cp -a "$payload"/. "$dir/"
  install -m 0644 "$ICON_DIR/icon-256.png" "$dir/labelplus-next.png"
  write_desktop "LabelPlus_Next.Desktop %U" "$dir/labelplus-next.desktop"
  cat > "$dir/README.txt" <<'EOF'
LabelPlus Next (portable Linux build)
=====================================

运行 / Run:
    ./LabelPlus_Next.Desktop

说明:
  * 首次运行如提示无权限: chmod +x LabelPlus_Next.Desktop update/LabelPlus_Next.Update
  * 用户配置保存在 ~/.local/share/LabelPlus_Next
  * update/ 内含自动更新程序；放在可写目录时可直接使用应用内自动更新
  * labelplus-next.desktop / labelplus-next.png 可用于手动创建桌面快捷方式

Notes:
  * If startup fails with a permission error:
        chmod +x LabelPlus_Next.Desktop update/LabelPlus_Next.Update
  * User configuration lives in ~/.local/share/LabelPlus_Next
  * The bundled updater in update/ enables in-app updates when the
    install directory is writable by the current user.
  * Use labelplus-next.desktop / labelplus-next.png for desktop integration.
EOF
  tar -C "$STAGE_DIR/tar-$rid" -czf \
    "$OUT_DIR/LabelPlus_Next-${VERSION}-linux-${rid#linux-}.tar.gz" \
    "$(basename "$dir")"
  log "built LabelPlus_Next-${VERSION}-linux-${rid#linux-}.tar.gz"
}

for rid in $RIDS; do
  require_publish "$rid"
  stage_payload "$rid" "$STAGE_DIR/linux-$rid/payload"
  build_deb "$rid"
  build_rpm "$rid"
  build_appimage "$rid"
  build_tarball "$rid"
done

log "Linux installers written to $OUT_DIR"
