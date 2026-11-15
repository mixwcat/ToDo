Unicode true
RequestExecutionLevel user
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

Name "Yijian Todo"
OutFile "${OUTPUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\YijianTodo"
InstallDirRegKey HKCU "Software\YijianTodo" "InstallDir"
SetCompressor /SOLID lzma
SetCompressorDictSize 32
BrandingText "Yijian Todo"
VIProductVersion "${VERSION}.0"
VIAddVersionKey /LANG=1033 "ProductName" "Yijian Todo"
VIAddVersionKey /LANG=1033 "FileDescription" "Yijian Todo installer"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Yijian Todo"

!define MUI_ICON "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\一件Todo.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "SimpChinese"

LangString RunningMessage ${LANG_ENGLISH} "Quit Yijian Todo from the tray menu before installing or uninstalling."
LangString RunningMessage ${LANG_SIMPCHINESE} "请先从托盘菜单退出一件 Todo，再安装或卸载。"
LangString RuntimeMessage ${LANG_ENGLISH} "The Lite edition requires .NET 8 Desktop Runtime x64. Open the official download page? You can also use the full installer."
LangString RuntimeMessage ${LANG_SIMPCHINESE} "精简版需要 .NET 8 Desktop Runtime x64。是否打开官方下载页面？也可改用完整安装包。"

!macro CheckRunning suffix
  FindWindow $0 "" "一件 Todo"
  StrCmp $0 0 0 running_${suffix}
  FindWindow $0 "" "Yijian Todo"
  StrCmp $0 0 done_${suffix}
  running_${suffix}:
    MessageBox MB_OK|MB_ICONINFORMATION "$(RunningMessage)"
    Abort
  done_${suffix}:
!macroend

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_OK "This package requires Windows x64."
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
  !insertmacro CheckRunning install
!ifdef LITE
  StrCpy $0 0
  runtime_loop:
    EnumRegValue $1 HKLM "SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App" $0
    StrCmp $1 "" runtime_missing
    StrCpy $2 $1 4
    StrCmp $2 "8.0." runtime_ready
    IntOp $0 $0 + 1
    Goto runtime_loop
  runtime_missing:
    MessageBox MB_YESNO|MB_ICONINFORMATION "$(RuntimeMessage)" IDNO runtime_abort
    ExecShell "open" "https://dotnet.microsoft.com/download/dotnet/8.0/runtime"
  runtime_abort:
    Abort
  runtime_ready:
!endif
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext current
  !insertmacro CheckRunning uninstall
FunctionEnd

Section "Yijian Todo"
  SetOutPath "$INSTDIR"
  File /r "${APP_DIR}\*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateDirectory "$SMPROGRAMS\Yijian Todo"
  CreateShortcut "$SMPROGRAMS\Yijian Todo\Yijian Todo.lnk" "$INSTDIR\一件Todo.exe" "" "$INSTDIR\Assets\app.ico"
  CreateShortcut "$SMPROGRAMS\Yijian Todo\Uninstall.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\Yijian Todo.lnk" "$INSTDIR\一件Todo.exe" "" "$INSTDIR\Assets\app.ico"
  WriteRegStr HKCU "Software\YijianTodo" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "DisplayName" "Yijian Todo"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "DisplayIcon" "$INSTDIR\Assets\app.ico"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "EstimatedSize" ${APP_SIZE_KB}
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo" "NoRepair" 1
SectionEnd

Section "Uninstall"
  ; Delete only shipped files. AppData tasks, preferences and keys are preserved.
  !include "${UNINSTALL_LIST}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  Delete "$SMPROGRAMS\Yijian Todo\Yijian Todo.lnk"
  Delete "$SMPROGRAMS\Yijian Todo\Uninstall.lnk"
  RMDir "$SMPROGRAMS\Yijian Todo"
  Delete "$DESKTOP\Yijian Todo.lnk"
  DeleteRegKey HKCU "Software\YijianTodo"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\YijianTodo"
SectionEnd
