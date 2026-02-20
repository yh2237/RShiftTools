@echo off
chcp 65001
echo ===== アプリ ビルド =====

cd /d "%~dp0"
dotnet publish RShiftTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o bin/Release/net8.0-windows/publish
if %errorlevel% neq 0 (
    echo [エラー] ビルドに失敗しました
    exit /b 1
)

echo [完了] アプリビルド成功