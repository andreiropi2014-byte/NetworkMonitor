using System.IO;
using System.Text;

namespace NetworkMonitor.Client.Logging;

public class ConsoleFileLogger : ILogger
{
    private readonly string _filePath;
    private readonly object _lock = new();

    public ConsoleFileLogger(string filePath)
    {
        _filePath = filePath;
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }

    public void LogInfo(string message) => Log("INFO", message);
    public void LogError(string message) => Log("ERROR", message);

    private void Log(string level, string message)
    {
        var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
        Console.WriteLine(line);

        lock (_lock)
        {
            File.AppendAllText(_filePath, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}