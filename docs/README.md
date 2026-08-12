# RShiftTools

右クリックメニューから動画・音声・画像の変換、リサイズ、カット、ファイルサイズ縮小、音声編集を行えるWindows用ツール

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

### 音声を編集

音声ファイルを選択して `音声を編集` を開くと、次の項目を変更してWAVまたはFLACへ書き出せます。

- ビット深度（維持 / 16-bit integer / 24-bit integer / 32-bit float）
- サンプルレート
- チャンネル数（維持 / Mono / Stereo）
- ディザリング

元ファイルは変更されません。複数の音声ファイルをまとめて処理できます。

### ファイルサイズ縮小

動画・音声では目標サイズを上限として書き出します。

- `MB（10進）` と `MiB（2進）` を選択可能
- 高精度モードでは出力サイズを実測し、超過時にビットレートを最大3回補正
- 入力がすでに目標以下の場合は再エンコードせずスキップ可能
- 処理後に実際の出力サイズ、目標に対する割合、試行回数を表示
- 動画の自動解像度調整、出力コーデック、FPS上限、字幕、メタデータを指定可能
- 画像は画質指定に加えて、目標KB以下へ自動調整可能

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
