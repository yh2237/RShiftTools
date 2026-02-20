@echo off
chcp 65001
echo ===== RShiftTools ビルド =====

echo [1/2] dotnet publish 中...
cd /d "%~dp0.."
dotnet publish RShiftTools.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o bin/Release/net8.0-windows/publish
if %errorlevel% neq 0 (
    echo [エラー] dotnet publish に失敗しました
    pause
    exit /b 1
)
echo [完了] dotnet publish 成功

echo ffmpeg をコピー中...
copy /Y "bin\Debug\net8.0-windows\ffmpeg.exe"  "bin\Release\net8.0-windows\publish\ffmpeg.exe"
copy /Y "bin\Debug\net8.0-windows\ffprobe.exe" "bin\Release\net8.0-windows\publish\ffprobe.exe"
copy /Y "bin\Debug\net8.0-windows\ffplay.exe"  "bin\Release\net8.0-windows\publish\ffplay.exe"
for %%f in ("bin\Debug\net8.0-windows\*.dll") do (
    copy /Y "%%f" "bin\Release\net8.0-windows\publish\"
)

echo [2/2] NSIS ビルド中...
cd installer
"C:\Program Files (x86)\NSIS\makensis.exe" RShiftTools.nsi
if %errorlevel% neq 0 (
    echo [エラー] NSIS ビルドに失敗しました
    pause
    exit /b 1
)

echo ===== ビルド完了 =====
echo インストーラー: installer\RShiftTools-1.0.0-setup.exe
pause