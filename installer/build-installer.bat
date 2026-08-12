@echo off
chcp 65001
echo ===== インストーラー ビルド =====

cd /d "%~dp0.."

set PUBLISH_DIR=bin\Release\net8.0-windows\publish

if not exist "%PUBLISH_DIR%\rshiftt.exe" (
    echo アプリがビルドされていません。先に build-app.bat を実行してください。
    pause
    exit /b 1
)

echo ffmpeg をコピー中...
copy /Y "redist\ffmpeg.exe"  "%PUBLISH_DIR%\ffmpeg.exe"
copy /Y "redist\ffprobe.exe" "%PUBLISH_DIR%\ffprobe.exe"
copy /Y "redist\ffplay.exe"  "%PUBLISH_DIR%\ffplay.exe"
for %%f in ("redist\*.dll") do (
    copy /Y "%%f" "%PUBLISH_DIR%\"
)
copy /Y "redist\ffmpeg-license.txt" "%PUBLISH_DIR%\ffmpeg-license.txt"
copy /Y "redist\gpl-3.0.txt"       "%PUBLISH_DIR%\gpl-3.0.txt"

echo NSIS ビルド中...
cd /d "%~dp0"

set NSIS_EXE=
if exist "C:\Program Files (x86)\NSIS\makensis.exe" (
    set "NSIS_EXE=C:\Program Files (x86)\NSIS\makensis.exe"
) else if exist "C:\Program Files\NSIS\makensis.exe" (
    set "NSIS_EXE=C:\Program Files\NSIS\makensis.exe"
) else (
    for /f "tokens=*" %%i in ('where makensis 2^>nul') do (
        set "NSIS_EXE=%%i"
        goto :nsis_found
    )
)

:nsis_found
if "%NSIS_EXE%"=="" (
    echo [エラー] makensis.exe が見つかりません。NSIS をインストールしてください。
    echo          https://nsis.sourceforge.io/Download
    pause
    exit /b 1
)

"%NSIS_EXE%" RShiftTools.nsi
if %errorlevel% neq 0 (
    echo [エラー] NSIS ビルドに失敗しました
    pause
    exit /b 1
)

echo ===== ビルド完了 =====
echo インストーラー: installer\RShiftTools-1.1.0-setup.exe
pause
