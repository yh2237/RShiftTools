namespace RShiftTools.Services;

public static class AppStrings
{
    public const string AppName = "RShiftTools";
    public const string ExeName = "rshiftt.exe";
    public const string FfmpegExe = "ffmpeg.exe";
    public const string FfprobeExe = "ffprobe.exe";
    public const string MenuLabel = "RShiftTools で変換(&R)";
    public const string MenuVerb = "open";
    public const string MUIVerb = AppName;
    public const string Error_FfmpegFailed = "ffmpeg がエラーを返しました:";
    public const string Error_Cancelled = "キャンセルされました";
    public const string Error_Failed = "失敗";
    public const string Status_Waiting = "待機中";
    public const string Status_Success = "完了";
    public const string Status_Error = "エラー";
    public const string Status_CompleteFormat = "完了: {0} 件成功  /  {1} 件失敗";
    public const string Error_FfmpegMissing = "ffmpeg.exe / ffprobe.exe が見つかりません。\nexe と同じフォルダに配置してください。";
    public const string Error_FileNotSpecified = "ファイルが指定されていません。";
    public const string Error_ModeNotImplemented = "モード '{0}' は未実装です。";
    public const string Error_WindowStartupFailed = "ウィンドウの起動に失敗しました。\n{0}\n\n{1}";
}
