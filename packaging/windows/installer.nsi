; LabelPlus Next - Windows installer (NSIS 3.x)
;
; Required defines (passed by packaging/windows/build-windows.sh):
;   APPVERSION, APPARCH, SRCDIR, OUTFILE, ICONFILE, APPSIZEKB
;
; Per-user install: %LOCALAPPDATA%\Programs\LabelPlus_Next, no UAC prompt,
; which also keeps the bundled in-app updater working (install dir writable).

Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef APPVERSION
  !define APPVERSION "0.0.0"
!endif
!ifndef APPARCH
  !define APPARCH "x64"
!endif
!ifndef APPSIZEKB
  !define APPSIZEKB 0
!endif
!ifndef SRCDIR
  !error "SRCDIR define is required"
!endif
!ifndef OUTFILE
  !error "OUTFILE define is required"
!endif
!ifndef ICONFILE
  !error "ICONFILE define is required"
!endif

!define APPNAME "LabelPlus Next"
!define COMPANY "SeaAndStars"
!define APPID "LabelPlus_Next"
!define HOMEPAGE "https://github.com/SeaAndStars/LabelPlus_Next"
!define UNINSTKEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APPID}"
!define EXEFILE "LabelPlus_Next.Desktop.exe"

!if "${APPARCH}" == "arm64"
  !define DISPLAYNAME "LabelPlus Next (ARM64)"
!else
  !define DISPLAYNAME "LabelPlus Next"
!endif

Name "${DISPLAYNAME} ${APPVERSION}"
OutFile "${OUTFILE}"
InstallDir "$LOCALAPPDATA\Programs\LabelPlus_Next"
InstallDirRegKey HKCU "Software\${APPID}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
SetCompressorDictSize 64
BrandingText "${APPNAME} ${APPVERSION} (${APPARCH})"

VIProductVersion "${APPVERSION}.0"
VIAddVersionKey "ProductName" "${APPNAME}"
VIAddVersionKey "CompanyName" "${COMPANY}"
VIAddVersionKey "FileDescription" "${DISPLAYNAME} Installer"
VIAddVersionKey "FileVersion" "${APPVERSION}"
VIAddVersionKey "ProductVersion" "${APPVERSION}"
VIAddVersionKey "LegalCopyright" "MIT License"

!define MUI_ICON "${ICONFILE}"
!define MUI_UNICON "${ICONFILE}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${EXEFILE}"
!define MUI_FINISHPAGE_RUN_TEXT "$(LaunchApp)"
!define MUI_FINISHPAGE_LINK "${HOMEPAGE}"
!define MUI_FINISHPAGE_LINK_LOCATION "${HOMEPAGE}"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"

LangString LaunchApp ${LANG_ENGLISH} "Launch LabelPlus Next"
LangString LaunchApp ${LANG_SIMPCHINESE} "运行 LabelPlus Next"
LangString UninstallApp ${LANG_ENGLISH} "Uninstall LabelPlus Next"
LangString UninstallApp ${LANG_SIMPCHINESE} "卸载 LabelPlus Next"
LangString CloseAppFirst ${LANG_ENGLISH} "LabelPlus Next is running. Please close it and run the installer again."
LangString CloseAppFirst ${LANG_SIMPCHINESE} "LabelPlus Next 正在运行，请先关闭应用后重新运行安装程序。"


Section "Install"
  SetShellVarContext current
  ; Registry writes must land in the 64-bit view on x64/ARM64 Windows so the
  ; shell (and the installer's own uninstall entry) can see them.
  ${If} ${IsNativeAMD64}
  ${OrIf} ${IsNativeARM64}
    SetRegView 64
  ${EndIf}

  ; Refuse to upgrade while the client is running (locked files).
  ${If} ${FileExists} "$INSTDIR\${EXEFILE}"
    ClearErrors
    Delete "$INSTDIR\${EXEFILE}"
    ${If} ${Errors}
      MessageBox MB_ICONSTOP|MB_OK "$(CloseAppFirst)"
      Quit
    ${EndIf}
  ${EndIf}

  ; Remove the previous updater payload first: stale native libraries from an
  ; older release would otherwise stay next to the fresh updater binary.
  RMDir /r "$INSTDIR\update"

  SetOutPath "$INSTDIR"
  File /r "${SRCDIR}/*"

  WriteUninstaller "$INSTDIR\Uninstall.exe"

  CreateDirectory "$SMPROGRAMS\LabelPlus Next"
  CreateShortcut "$SMPROGRAMS\LabelPlus Next\LabelPlus Next.lnk" "$INSTDIR\${EXEFILE}"
  CreateShortcut "$SMPROGRAMS\LabelPlus Next\$(UninstallApp).lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\LabelPlus Next.lnk" "$INSTDIR\${EXEFILE}"

  WriteRegStr HKCU "${UNINSTKEY}" "DisplayName" "${DISPLAYNAME}"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayVersion" "${APPVERSION}"
  WriteRegStr HKCU "${UNINSTKEY}" "Publisher" "${COMPANY}"
  WriteRegStr HKCU "${UNINSTKEY}" "URLInfoAbout" "${HOMEPAGE}"
  WriteRegStr HKCU "${UNINSTKEY}" "HelpLink" "${HOMEPAGE}/issues"
  WriteRegStr HKCU "${UNINSTKEY}" "DisplayIcon" "$INSTDIR\${EXEFILE},0"
  WriteRegStr HKCU "${UNINSTKEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTKEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTKEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "NoRepair" 1
  WriteRegDWORD HKCU "${UNINSTKEY}" "EstimatedSize" ${APPSIZEKB}

  WriteRegStr HKCU "Software\${APPID}" "InstallDir" "$INSTDIR"

  ; labelplus:// protocol (the client also re-registers this per user at startup)
  WriteRegStr HKCU "Software\Classes\labelplus" "" "URL:LabelPlus Protocol"
  WriteRegStr HKCU "Software\Classes\labelplus" "URL Protocol" ""
  WriteRegStr HKCU "Software\Classes\labelplus\DefaultIcon" "" "$INSTDIR\${EXEFILE},0"
  WriteRegStr HKCU "Software\Classes\labelplus\shell\open\command" "" '"$INSTDIR\${EXEFILE}" "%1"'
SectionEnd

Section "Uninstall"
  SetShellVarContext current
  ${If} ${IsNativeAMD64}
  ${OrIf} ${IsNativeARM64}
    SetRegView 64
  ${EndIf}

  Delete "$DESKTOP\LabelPlus Next.lnk"
  Delete "$SMPROGRAMS\LabelPlus Next\LabelPlus Next.lnk"
  Delete "$SMPROGRAMS\LabelPlus Next\$(UninstallApp).lnk"
  RMDir "$SMPROGRAMS\LabelPlus Next"

  ${If} ${FileExists} "$INSTDIR\${EXEFILE}"
    ClearErrors
    Delete "$INSTDIR\${EXEFILE}"
    ${If} ${Errors}
      MessageBox MB_ICONSTOP|MB_OK "$(CloseAppFirst)"
      Quit
    ${EndIf}
  ${EndIf}

  DeleteRegKey HKCU "${UNINSTKEY}"
  DeleteRegKey HKCU "Software\Classes\labelplus"
  DeleteRegKey HKCU "Software\${APPID}"

  ; The install directory is fully managed by this installer.
  RMDir /r "$INSTDIR"
SectionEnd
