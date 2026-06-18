using System;
using System.IO;
using System.Threading;

namespace Histshot.Core.Services;

public static class DebugLogger
{
    private static readonly string LogFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Histshot",
        "debug.log");

    private static readonly object Lock = new();

    static DebugLogger()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogFilePath)!);
        }
        catch { }
    }

    public static void Log(string message)
    {
        try
        {
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Thread.CurrentThread.ManagedThreadId}] {message}";
            lock (Lock)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch { }
    }
}
