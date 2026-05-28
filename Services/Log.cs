using System.Globalization;
using System.IO;

namespace RShiftTools.Services;

public static class Log
{
    private static readonly object _lock = new();
    private static readonly string _logPath;

    static Log()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "RShiftTools"
        );
        try
        {
            Directory.CreateDirectory(dir);
        }
        catch
        {
            dir = Directory.GetCurrentDirectory();
        }
        _logPath = Path.Combine(dir, "RShiftTools.log");
    }

    public static void Info(string message) => Write("INFO", message);

    public static void Error(string message) => Write("ERROR", message);

    public static void Debug(string message) => Write("DEBUG", message);

    private static void Write(string level, string message)
    {
        try
        {
            var line =
                $"{DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture)} [{level}] {message}";
            lock (_lock)
            {
                const long maxSize = 1024 * 1024;
                try
                {
                    var info = new FileInfo(_logPath);
                    if (info.Exists && info.Length > maxSize)
                    {
                        var backup = _logPath + ".old";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(_logPath, backup);
                    }
                }
                catch { }

                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
