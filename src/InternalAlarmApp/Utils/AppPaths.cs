namespace InternalAlarmApp.Utils;

public static class AppPaths
{
    public static readonly string Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "InternalAlarmApp");
    public static readonly string Data = Path.Combine(Root, "data");
    public static readonly string Logs = Path.Combine(Root, "logs");
    public static readonly string Settings = Path.Combine(Root, "settings", "settings.json");
    public static readonly string Database = Path.Combine(Data, "alarm.db");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Logs);
        Directory.CreateDirectory(Path.GetDirectoryName(Settings)!);
    }
}
