using System.Text.RegularExpressions;

public static class Logger
{
    private static string? _logFilePath;
    private static Regex? _filterRegex;
    private static readonly object LockObj = new();

    public static void Initialize(string logFilePath, string? filterPattern = null)
    {
        _logFilePath = logFilePath;
        if (!string.IsNullOrEmpty(filterPattern))
            _filterRegex = new Regex(filterPattern, RegexOptions.Compiled | RegexOptions.IgnoreCase);

        File.WriteAllText(_logFilePath, $"[Log started at {DateTime.Now}]\n");
    }

    public static void Log(string message)
    {
        if (string.IsNullOrEmpty(_logFilePath))
            throw new InvalidOperationException("Logger not initialized. Call Logger.Initialize first.");

        if (_filterRegex != null && !_filterRegex.IsMatch(message))
            return;

        var logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}";
        lock (LockObj)
        {
            File.AppendAllText(_logFilePath, logLine + Environment.NewLine);
        }
    }
}
