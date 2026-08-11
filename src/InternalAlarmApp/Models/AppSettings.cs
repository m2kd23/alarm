using System.Text.Json;

namespace InternalAlarmApp.Models;

public sealed class AppSettings
{
    public int HotKeyVirtualKey { get; set; } = 0x91; // Scroll Lock
    public string HotKeyName { get; set; } = "Scroll Lock";
    public int DoublePressMilliseconds { get; set; } = 500;
    public bool RunAtStartup { get; set; }
    public bool KeepPopupVisible { get; set; } = true;

    public static AppSettings Load(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
