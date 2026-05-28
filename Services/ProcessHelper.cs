using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace RShiftTools.Services;

public static class ProcessHelper
{
    public static Process StartProcess(
        string exePath,
        string arguments,
        bool redirectStdOut = false,
        bool redirectStdErr = false
    ) => StartProcess(exePath, SplitCommandLine(arguments), redirectStdOut, redirectStdErr);

    public static Process StartProcess(
        string exePath,
        IEnumerable<string>? argumentList = null,
        bool redirectStdOut = false,
        bool redirectStdErr = false
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            RedirectStandardOutput = redirectStdOut,
            RedirectStandardError = redirectStdErr,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? string.Empty,
        };

        if (redirectStdOut)
            startInfo.StandardOutputEncoding = Encoding.UTF8;
        if (redirectStdErr)
            startInfo.StandardErrorEncoding = Encoding.UTF8;

        var exeDir = Path.GetDirectoryName(exePath) ?? string.Empty;
        var curPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        startInfo.Environment["PATH"] = exeDir + ";" + curPath;

        if (argumentList != null)
        {
            try
            {
                foreach (var a in argumentList)
                {
                    if (a is null)
                        continue;
                    startInfo.ArgumentList.Add(a);
                }
            }
            catch (PlatformNotSupportedException)
            {
                startInfo.Arguments = string.Join(' ', argumentList);
            }
        }

        var proc = new Process { StartInfo = startInfo };
        try
        {
            proc.Start();
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to start process {exePath}: {ex.Message}");
            throw;
        }
        return proc;
    }

    private static List<string> SplitCommandLine(string? commandLine)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(commandLine))
            return result;

        var ptr = CommandLineToArgvW(commandLine, out var count);
        if (ptr == IntPtr.Zero)
            return result;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var p = Marshal.ReadIntPtr(ptr, i * IntPtr.Size);
                var s = Marshal.PtrToStringUni(p);
                if (s != null)
                    result.Add(s);
            }
        }
        finally
        {
            LocalFree(ptr);
        }

        return result;
    }

    public static List<string> SplitCommandLinePublic(string? commandLine) =>
        SplitCommandLine(commandLine);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CommandLineToArgvW(string lpCmdLine, out int pNumArgs);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr hMem);
}
