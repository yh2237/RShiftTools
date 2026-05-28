# RShiftTools

右クリックメニューから動画・音声・画像の変換、リサイズ、カット、ファイルサイズ縮小を行えるWindows用ツール

## 必要環境

- Windows 10 / 11 (64bit)
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0)

## インストール・アンインストール

[Releases](https://github.com/yh2237/RShiftTools/releases) からインストーラーをインストールして実行してください。

既にインストールされてる場合はアンインストールの選択肢が出ます。

## 使い方

### 右クリックメニューから使う

1. エクスプローラーでファイルを右クリック（複数選択可）
2. `RShiftTools` → 目的のモードを選択

### 単体起動して使う

1. `rshiftt.exe` を起動
2. フォルダを移動してファイルを選択
3. 下部のボタンまたは右クリックメニューからモードを選択

## ビルド

### 必要環境

- [.NET 8 SDK](https://dotnet.microsoft.com/ja-jp/download/dotnet/8.0)
- [NSIS](https://nsis.sourceforge.io/Download)

### ビルド手順

```bat
# ffmpeg のダウンロード
setup-ffmpeg.bat

# アプリのみビルド
build-app.bat

# 全部ビルド
build-all.bat
```

## 依存ソフトウェア

RShiftToolsは [FFmpeg](https://ffmpeg.org/) を使用しています。

- ffmpeg / ffprobe / ffplay は [BtbN/FFmpeg-Builds](https://github.com/BtbN/FFmpeg-Builds) のLGPLビルドを使用
- ライセンス詳細は同梱の `ffmpeg-license.txt` および `gpl-3.0.txt` を参照

## ライセンス

RShiftTools本体はMIT Licenseで公開してます。

同梱されるFFmpegはLGPLv3でライセンスされています。詳細は `ffmpeg-license.txt` および `gpl-3.0.txt` を参照してください。
