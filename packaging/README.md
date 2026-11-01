# 安装包构建 / Installer packaging

把 `LabelPlus_Next.Desktop` 与 `LabelPlus_Next.Update` 打成各平台安装包。

```bash
packaging/build-all.sh                 # 发布(6 RID) + 打包全平台
SKIP_PUBLISH=1 packaging/build-all.sh  # 只重新打包已发布的产物
```

产物统一输出到 `release/installers/`（含 `SHA256SUMS.txt`）：

| 平台 | 安装包 | 说明 |
| --- | --- | --- |
| Windows x64 / arm64 | `LabelPlus_Next-<ver>-win-<rid>-setup.exe` | NSIS 安装器，按用户安装，无需管理员权限 |
| Linux x64 / arm64 | `labelplus-next_<ver>_<arch>.deb` | 安装到 `/usr/lib/labelplus-next`，带 `.desktop`、图标、`labelplus://` mime |
| Linux x64 / arm64 | `labelplus-next-<ver>-1.<arch>.rpm` | 同上，适用于 Fedora/RHEL 系 |
| Linux x64 / arm64 | `LabelPlus_Next-<ver>-linux-<rid>.AppImage` | 便携，双击运行 |
| Linux x64 / arm64 | `LabelPlus_Next-<ver>-linux-<rid>.tar.gz` | 便携目录，解压即用 |
| macOS Intel / Apple Silicon | `LabelPlus_Next-<ver>-macos-<arch>.dmg` | `.app` + `Applications` 快捷方式 |
| macOS Intel / Apple Silicon | `LabelPlus_Next-<ver>-macos-<arch>.zip` | 便携 `.app` |

## 安装布局

安装包都会把更新程序放在应用目录的 `update/` 子目录中，这是客户端
`SettingsViewModel` 寻找更新器（`update/LabelPlus_Next.Update[.exe]`）的位置：

```
<install dir>/
  LabelPlus_Next.Desktop[.exe]      Client.version.json   settings.json   原生依赖
  update/
    LabelPlus_Next.Update[.exe]     Update.version.json   原生依赖
```

- Windows：`%LOCALAPPDATA%\Programs\LabelPlus_Next`，开始菜单/桌面快捷方式，
  注册 `labelplus://` 协议与“应用和功能”卸载项；目录可写，应用内自动更新可用。
- Linux `.deb`/`.rpm`：`/usr/lib/labelplus-next`，`/usr/bin/labelplus-next`
  软链接；系统目录默认只读，**应用内自动更新不可用**，请通过包管理器升级
  （便携 tar.gz / AppImage 放在用户可写目录时可自动更新）。
- macOS：`LabelPlus_Next.app`（`Contents/MacOS` 内为应用本体，`update/` 在
  同一目录）。放入 `~/Applications` 时应用内自动更新可用；放入
  `/Applications` 通常需要管理员权限。`Info.plist` 已注册 `labelplus://`。

## 前置依赖

- .NET SDK 10（`global.json` 已固定）
- Windows 安装器：`nsis`（`makensis`）
- Linux：`dpkg-deb`、`rpmbuild`、`squashfs-tools`、`imagemagick`（`convert`）
- macOS DMG：`hfsprogs`（`mkfs.hfsplus`）、`zip`、`cmake`、`libssl-dev`、
  `python3`；`packaging/lib/build-dmg-tools.sh` 会自动克隆并编译
  [libdmg-hfsplus](https://github.com/planetbeing/libdmg-hfsplus)（已带 OpenSSL 3 补丁）
- AppImage：首次构建会自动下载 `appimagetool` 与 type2 runtime 到 `artifacts/tools/`

Ubuntu/Debian 一键安装：

```bash
sudo apt-get install -y nsis rpm dpkg-dev squashfs-tools imagemagick \
  hfsprogs zip cmake libssl-dev zlib1g-dev python3
```

## 子命令

```bash
packaging/publish.sh                        # 只发布 6 个 RID
RIDS="win-x64 linux-x64" packaging/publish.sh

packaging/windows/build-windows.sh          # 只打 Windows
packaging/linux/build-linux.sh              # 只打 Linux
packaging/macos/build-macos.sh              # 只打 macOS

WINDOWS_RIDS=win-x64 packaging/windows/build-windows.sh
LINUX_RIDS=linux-x64 packaging/linux/build-linux.sh
MACOS_RIDS=osx-arm64 packaging/macos/build-macos.sh
```

中间产物：`artifacts/publish/<rid>/{desktop,updater}` 与 `artifacts/stage/`；
日志：`artifacts/logs/`。

## macOS 说明

DMG 在 Linux 上生成：先用 `mkfs.hfsplus` 建 HFS+ 卷，`hdutil addall` 写入
`.app` 并 `hdutil symlink /Applications`，最后由 `dmg build` 转成压缩的 UDIF。
macOS 挂载后看到的是原生 HFS+ 卷（保留可执行位与符号链接）。

## 签名状态

当前没有 Apple Developer ID 与 Windows 代码签名证书，产物为**未签名**：

- macOS 首次打开需右键“打开”，或执行
  `xattr -dr com.apple.quarantine "/Applications/LabelPlus_Next.app"`；
- Windows 可能出现 SmartScreen 提示，需“更多信息 → 仍要运行”。

后续拿到证书后，可在 `packaging/macos/build-macos.sh` 中增加 `codesign` +
`xcrun notarytool`，在 `packaging/windows/build-windows.sh` 中用 `osslsigncode`
对 `setup.exe` 签名，不影响现有流程。

## CI 自动发布

`.github/workflows/desktop-release.yml`：

- `build`：矩阵构建 6 个 RID 的客户端与更新器发布产物；
- `installers`（仅 tag）：在 `ubuntu-24.04` 上复用 `build` 的产物，运行本目录脚本生成三平台安装包与 `SHA256SUMS.txt`；
- `release`：tag 发布时先上传 zip，再用 `gh release upload` 追加全部安装包；nightly 只发布 zip，避免每次推送都上传约 1.4 GB。

注意：`installers` job 在 tag 指向的提交上运行，因此 `packaging/` 脚本必须包含在该 tag 中；v1.4.1 及更早的 tag 不含这些脚本。
