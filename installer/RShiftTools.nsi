Unicode True

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define APP_NAME      "RShiftTools"
!define APP_EXE       "rshiftt.exe"
!define APP_VERSION   "1.0.0"
!define UNINSTALL_REG "Software\Microsoft\Windows\CurrentVersion\Uninstall\RShiftTools"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "RShiftTools-${APP_VERSION}-setup.exe"
InstallDir "$PROGRAMFILES64\RShiftTools"
RequestExecutionLevel admin

!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Japanese"

Section "MainSection" SEC01

  SetOutPath "$INSTDIR"
  File /r "..\bin\Release\net8.0-windows\publish\*.*"

  WriteUninstaller "$INSTDIR\uninstall.exe"

  WriteRegStr   HKLM "${UNINSTALL_REG}" "DisplayName"      "${APP_NAME}"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "DisplayVersion"   "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "Publisher"        "RShiftTools"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "InstallLocation"  "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "UninstallString"  "$INSTDIR\uninstall.exe"
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "NoModify"         1
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "NoRepair"         1

  DetailPrint "コンテキストメニューを登録中..."
  ExecWait '"$INSTDIR\${APP_EXE}" --install'

SectionEnd

Section "Uninstall"

  ExecWait '"$INSTDIR\${APP_EXE}" --uninstall'
  DeleteRegKey HKLM "${UNINSTALL_REG}"
  RMDir /r "$INSTDIR"

SectionEnd