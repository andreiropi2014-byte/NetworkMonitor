using System.Text;

namespace NetworkMonitor.Server.Logging;

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

    public void Info(string message) => Log("INFO", message);
    public void Error(string message) => Log("ERROR", message);
    public void Warning(string message) => Log("WARNING", message);
    public void Debug(string message) => Log("DEBUG", message);

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