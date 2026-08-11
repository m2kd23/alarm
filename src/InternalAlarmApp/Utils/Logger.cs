namespace InternalAlarmApp.Utils;

public static class Logger
{
    private static readonly object Gate = new();

    public static void Info(string message) => Write("INFO", message);
    public static void Error(string area, Exception ex) => Write("ERROR", $"{area}: {ex.GetType().Name}: {ex.Message}");

    private static void Write(string level, string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            lock (Gate)
                File.AppendAllText(Path.Combine(AppPaths.Logs, $"app-{DateTime.Today:yyyyMMdd}.log"), $"{DateTime.Now:O} [{level}] {message}{Environment.NewLine}");
        }
        catch { }
    }
}
