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
                File.AppendAllText(_logPath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
