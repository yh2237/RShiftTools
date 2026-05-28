@echo off
chcp 65001
echo ===== フルビルド開始 =====

cd /d "%~dp0"

echo [0/3] ffmpeg を確認中...
if not exist "redist\ffmpeg.exe" (
    echo ffmpeg が見つかりません。ダウンロードします...
    call setup-ffmpeg.bat
    if %errorlevel% neq 0 (
        echo [エラー] ffmpeg の準備に失敗しました
        pause
        exit /b 1
    )
)

echo [1/3] アプリをビルド中...
call build-app.bat
if %errorlevel% neq 0 (
    echo [エラー] アプリビルドに失敗しました
    pause
    exit /b 1
)

echo [2/3] インストーラーをビルド中...
call installer\build-installer.bat
if %errorlevel% neq 0 (
    echo [エラー] インストーラービルドに失敗しました
    pause
    exit /b 1
)

echo ===== フルビルド完了 =====
echo 出力: installer\RShiftTools-1.0.0-setup.exe
pause