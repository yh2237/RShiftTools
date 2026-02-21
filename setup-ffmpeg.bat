@echo off
chcp 65001
echo ===== ffmpeg セットアップ =====

set FFMPEG_URL=https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip
set FFMPEG_ZIP=%TEMP%\ffmpeg.zip
set FFMPEG_EXTRACT=%TEMP%\ffmpeg_extracted
set DEST=%~dp0.\bin\Debug\net8.0-windows

echo [1/3] ffmpeg をダウンロード中...
powershell -Command "Invoke-WebRequest -Uri '%FFMPEG_URL%' -OutFile '%FFMPEG_ZIP%' -UseBasicParsing"
if %errorlevel% neq 0 (
    echo [エラー] ダウンロードに失敗しました
    pause
    exit /b 1
)

echo [2/3] 展開中...
powershell -Command "Expand-Archive -Path '%FFMPEG_ZIP%' -DestinationPath '%FFMPEG_EXTRACT%' -Force"
if %errorlevel% neq 0 (
    echo [エラー] 展開に失敗しました
    pause
    exit /b 1
)

echo [3/3] コピー中...
copy /Y "%FFMPEG_EXTRACT%\ffmpeg-master-latest-win64-gpl\bin\ffmpeg.exe"  "%DEST%\ffmpeg.exe"
copy /Y "%FFMPEG_EXTRACT%\ffmpeg-master-latest-win64-gpl\bin\ffprobe.exe" "%DEST%\ffprobe.exe"
copy /Y "%FFMPEG_EXTRACT%\ffmpeg-master-latest-win64-gpl\bin\ffplay.exe"  "%DEST%\ffplay.exe"
for %%f in ("%FFMPEG_EXTRACT%\ffmpeg-master-latest-win64-gpl\bin\*.dll") do (
    copy /Y "%%f" "%DEST%\"
)

echo 一時ファイルを削除中...
del /f /q "%FFMPEG_ZIP%"
rmdir /s /q "%FFMPEG_EXTRACT%"

echo ===== 完了 =====
pause