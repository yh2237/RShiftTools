@echo off
chcp 65001
echo ===== アプリ ビルド =====

cd /d "%~dp0"
dotnet publish RShiftTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o bin/Release/net8.0-windows/publish
if %errorlevel% neq 0 (
    echo [エラー] ビルドに失敗しました
    exit /b 1
)

echo ffmpeg をコピー中...
copy /Y "redist\ffmpeg.exe"  "bin/Release/net8.0-windows/publish/ffmpeg.exe"
copy /Y "redist\ffprobe.exe" "bin/Release/net8.0-windows/publish/ffprobe.exe"
copy /Y "redist\ffplay.exe"  "bin/Release/net8.0-windows/publish/ffplay.exe"
for %%f in ("redist\*.dll") do copy /Y "%%f" "bin/Release/net8.0-windows/publish/"
copy /Y "redist\ffmpeg-license.txt" "bin/Release/net8.0-windows/publish/ffmpeg-license.txt"
copy /Y "redist\gpl-3.0.txt"       "bin/Release/net8.0-windows/publish/gpl-3.0.txt"

echo [完了] アプリビルド成功