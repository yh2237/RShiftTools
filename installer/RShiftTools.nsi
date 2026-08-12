Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "nsDialogs.nsh"

!define APP_NAME      "RShiftTools"
!define APP_EXE       "rshiftt.exe"
!define APP_VERSION   "1.1.0"
!define UNINSTALL_REG "Software\Microsoft\Windows\CurrentVersion\Uninstall\RShiftTools"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "RShiftTools-${APP_VERSION}-setup.exe"
InstallDir "$PROGRAMFILES64\RShiftTools"

Var RADIO_USER
Var RADIO_ALL
Var INST_ALLUSERS
Var EXISTING_FOUND
Var EXISTING_INSTALLDIR
Var EXISTING_ALLUSERS
Var RADIO_REINSTALL
Var IS_REINSTALL

Function .onInit
  StrCpy $EXISTING_FOUND 0

  ReadRegStr $EXISTING_INSTALLDIR HKCU "${UNINSTALL_REG}" "InstallLocation"
  ${If} $EXISTING_INSTALLDIR != ""
    StrCpy $EXISTING_FOUND 1
    StrCpy $EXISTING_ALLUSERS 0
    Return
  ${EndIf}

  ReadRegStr $EXISTING_INSTALLDIR HKLM "${UNINSTALL_REG}" "InstallLocation"
  ${If} $EXISTING_INSTALLDIR != ""
    StrCpy $EXISTING_FOUND 1
    StrCpy $EXISTING_ALLUSERS 1
    Return
  ${EndIf}

  StrCpy $EXISTING_INSTALLDIR "$LOCALAPPDATA\RShiftTools"
  IfFileExists "$EXISTING_INSTALLDIR\${APP_EXE}" 0 +4
    StrCpy $EXISTING_FOUND 1
    StrCpy $EXISTING_ALLUSERS 0
    Return

  StrCpy $EXISTING_INSTALLDIR "$PROGRAMFILES64\RShiftTools"
  IfFileExists "$EXISTING_INSTALLDIR\${APP_EXE}" 0 +3
    StrCpy $EXISTING_FOUND 1
    StrCpy $EXISTING_ALLUSERS 1
FunctionEnd

Function CreateScopePage
  ${If} $IS_REINSTALL == 1
    StrCpy $INST_ALLUSERS $EXISTING_ALLUSERS
    StrCpy $INSTDIR $EXISTING_INSTALLDIR
    Abort
  ${EndIf}

  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 12u "インストール範囲を選択してください:"
  Pop $0

  ${NSD_CreateRadioButton} 0 20 100% 12u "現在のユーザーのみ"
  Pop $RADIO_USER

  ${NSD_CreateRadioButton} 0 40 100% 12u "すべてのユーザー"
  Pop $RADIO_ALL

  ${NSD_SetState} $RADIO_USER ${BST_CHECKED}

  nsDialogs::Show
FunctionEnd

Function LeaveScopePage
  ${NSD_GetState} $RADIO_ALL $0
  StrCmp $0 ${BST_CHECKED} allusers peruser
  allusers:
    StrCpy $INST_ALLUSERS 1
    StrCpy $INSTDIR "$PROGRAMFILES64\RShiftTools"
    Goto done
  peruser:
    StrCpy $INST_ALLUSERS 0
    StrCpy $INSTDIR "$LOCALAPPDATA\RShiftTools"
  done:
FunctionEnd

Function CreateMaintenancePage
  ${If} $EXISTING_FOUND != 1
    Abort
  ${EndIf}

  nsDialogs::Create 1018
  Pop $0
  ${If} $0 == error
    Abort
  ${EndIf}

  ${NSD_CreateLabel} 0 0 100% 24u "RShiftTools は既にインストールされています。$\n$\nインストール先: $EXISTING_INSTALLDIR$\n$\n操作を選択してください:"
  Pop $0

  ${NSD_CreateRadioButton} 0 60 100% 12u "上書きインストール（設定は保持されます）"
  Pop $RADIO_REINSTALL

  ${NSD_CreateRadioButton} 0 80 100% 12u "アンインストール"
  Pop $0

  ${NSD_SetState} $RADIO_REINSTALL ${BST_CHECKED}

  nsDialogs::Show
FunctionEnd

Function LeaveMaintenancePage
  ${NSD_GetState} $RADIO_REINSTALL $0
  ${If} $0 != ${BST_CHECKED}
    MessageBox MB_ICONINFORMATION|MB_OK "アンインストールを実行します。"
    ExecWait '"$EXISTING_INSTALLDIR\uninstall.exe"'
    Quit
  ${Else}
    StrCpy $IS_REINSTALL 1
  ${EndIf}
FunctionEnd

Function SkipDirIfReinstall
  ${If} $IS_REINSTALL == 1
    Abort
  ${EndIf}
FunctionEnd

!define MUI_ICON "..\Assets\RShiftTools.ico"
!define MUI_ABORTWARNING
Page custom CreateMaintenancePage LeaveMaintenancePage
Page custom CreateScopePage LeaveScopePage
!define MUI_PAGE_CUSTOMFUNCTION_PRE SkipDirIfReinstall
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Japanese"

Section "MainSection" SEC01
  ${If} $EXISTING_FOUND == 1
    ${If} $EXISTING_ALLUSERS == 1
      ExecWait '"$EXISTING_INSTALLDIR\${APP_EXE}" --uninstall --allusers'
    ${Else}
      ExecWait '"$EXISTING_INSTALLDIR\${APP_EXE}" --uninstall'
    ${EndIf}
  ${EndIf}

  StrCmp $INST_ALLUSERS 1 skip_scope
  StrCmp $INST_ALLUSERS 0 skip_scope
  StrCpy $INST_ALLUSERS 0
  skip_scope:

  StrCmp $INST_ALLUSERS 1 +3
  StrCpy $INSTDIR "$LOCALAPPDATA\RShiftTools"
  Goto instdir_done
  StrCpy $INSTDIR "$PROGRAMFILES64\RShiftTools"
  instdir_done:

  SetOutPath "$INSTDIR"
  CreateDirectory "$INSTDIR"

  File /r "..\bin\Release\net8.0-windows\publish\*.*"
  WriteUninstaller "$INSTDIR\uninstall.exe"

  StrCmp $INST_ALLUSERS 1 0 +9
  WriteRegStr   HKLM "${UNINSTALL_REG}" "DisplayName"      "${APP_NAME}"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "DisplayVersion"   "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "Publisher"        "RShiftTools"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "InstallLocation"  "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "UninstallString"  "$INSTDIR\uninstall.exe"
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "InstallScopeAllUsers" 1
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "NoModify"         1
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "NoRepair"         1
  Goto reg_done

  WriteRegStr   HKCU "${UNINSTALL_REG}" "DisplayName"      "${APP_NAME}"
  WriteRegStr   HKCU "${UNINSTALL_REG}" "DisplayVersion"   "${APP_VERSION}"
  WriteRegStr   HKCU "${UNINSTALL_REG}" "Publisher"        "RShiftTools"
  WriteRegStr   HKCU "${UNINSTALL_REG}" "InstallLocation"  "$INSTDIR"
  WriteRegStr   HKCU "${UNINSTALL_REG}" "UninstallString"  "$INSTDIR\uninstall.exe"
  WriteRegDWORD HKCU "${UNINSTALL_REG}" "InstallScopeAllUsers" 0
  WriteRegDWORD HKCU "${UNINSTALL_REG}" "NoModify"         1
  WriteRegDWORD HKCU "${UNINSTALL_REG}" "NoRepair"         1
  reg_done:

  StrCmp $INST_ALLUSERS 1 0 +3
  ExecWait '"$INSTDIR\${APP_EXE}" --install --allusers'
  Goto install_done
  ExecWait '"$INSTDIR\${APP_EXE}" --install'
  install_done:
SectionEnd

Section "Uninstall"
  ReadRegDWORD $0 HKLM "${UNINSTALL_REG}" "InstallScopeAllUsers"
  ReadRegDWORD $1 HKCU "${UNINSTALL_REG}" "InstallScopeAllUsers"
  ${If} $0 = 1
  ${OrIf} $1 = 1
    ExecWait '"$INSTDIR\${APP_EXE}" --uninstall --allusers'
  ${Else}
    ExecWait '"$INSTDIR\${APP_EXE}" --uninstall'
  ${EndIf}

  DeleteRegKey HKLM "${UNINSTALL_REG}"
  DeleteRegKey HKCU "${UNINSTALL_REG}"
  RMDir /r "$INSTDIR"
SectionEnd
