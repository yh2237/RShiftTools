Unicode True

!include "MUI2.nsh"
!include "LogicLib.nsh"

!define APP_NAME      "RShiftTools"
!define APP_EXE       "rshiftt.exe"
!define APP_VERSION   "1.0.0"
!define FFMPEG_URL    "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip"
!define FFMPEG_ZIP    "$TEMP\ffmpeg.zip"
!define UNINSTALL_REG "Software\Microsoft\Windows\CurrentVersion\Uninstall\RShiftTools"

Name "${APP_NAME} ${APP_VERSION}"
OutFile "RShiftTools-${APP_VERSION}-setup.exe"
InstallDir "$PROGRAMFILES64\RShiftTools"
RequestExecutionLevel admin

;--------------------------------
; MUI設定
;--------------------------------
!define MUI_ABORTWARNING
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "Japanese"

;--------------------------------
; インストール
;--------------------------------
Section "MainSection" SEC01

  SetOutPath "$INSTDIR"

  ; アプリ本体をコピー
  File /r "..\bin\Release\net8.0-windows\publish\*.*"

  ; アンインストーラー生成
  WriteUninstaller "$INSTDIR\uninstall.exe"

  ; コントロールパネルに登録
  WriteRegStr   HKLM "${UNINSTALL_REG}" "DisplayName"      "${APP_NAME}"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "DisplayVersion"   "${APP_VERSION}"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "Publisher"        "RShiftTools"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "InstallLocation"  "$INSTDIR"
  WriteRegStr   HKLM "${UNINSTALL_REG}" "UninstallString"  "$INSTDIR\uninstall.exe"
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "NoModify"         1
  WriteRegDWORD HKLM "${UNINSTALL_REG}" "NoRepair"         1

  ; ffmpegをダウンロード
  DetailPrint "ffmpeg をダウンロード中..."
  NSISdl::download /TIMEOUT=60000 "${FFMPEG_URL}" "${FFMPEG_ZIP}"
  Pop $0
  ${If} $0 != "success"
  MessageBox MB_OK|MB_ICONEXCLAMATION "ffmpeg のダウンロードに失敗しました。$\nインターネット接続を確認してください。$\nエラー: $0"
  Abort
  ${EndIf}

  ; ZIPを解凍（NSISのZipDLL or PowerShellを使用）
  DetailPrint "ffmpeg を展開中..."
  nsExec::ExecToLog 'powershell -Command "Expand-Archive -Path \"${FFMPEG_ZIP}\" -DestinationPath \"$TEMP\ffmpeg_extracted\" -Force"'
  Pop $0

  ; bin フォルダの中身だけコピー
  CopyFiles "$TEMP\ffmpeg_extracted\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe"  "$INSTDIR\ffmpeg.exe"
  CopyFiles "$TEMP\ffmpeg_extracted\ffmpeg-master-latest-win64-gpl\bin\ffprobe.exe" "$INSTDIR\ffprobe.exe"
  CopyFiles "$TEMP\ffmpeg_extracted\ffmpeg-master-latest-win64-gpl\bin\*.dll"       "$INSTDIR\"

  ; 一時ファイルを削除
  Delete "${FFMPEG_ZIP}"
  RMDir /r "$TEMP\ffmpeg_extracted"

  ; 右クリックメニュー登録
  DetailPrint "右クリックメニューを登録中..."
  ExecWait '"$INSTDIR\${APP_EXE}" --install'

SectionEnd

;--------------------------------
; アンインストール
;--------------------------------
Section "Uninstall"

  ; 右クリックメニュー登録解除
  ExecWait '"$INSTDIR\${APP_EXE}" --uninstall'

  ; コントロールパネルから削除
  DeleteRegKey HKLM "${UNINSTALL_REG}"

  ; ファイル削除
  RMDir /r "$INSTDIR"

SectionEnd