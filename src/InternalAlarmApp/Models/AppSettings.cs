using System.Text.Json;

namespace InternalAlarmApp.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 4;
    public int HotKeyVirtualKey { get; set; } = 0x14; // Caps Lock
    public string HotKeyName { get; set; } = "Ctrl + Caps Lock";
    public int HotKeyModifiers { get; set; } = 1; // Ctrl
    public int HotKeyPressCount { get; set; } = 1;
    public int DoublePressMilliseconds { get; set; } = 500;
    public bool RunAtStartup { get; set; }
    public bool KeepPopupVisible { get; set; } = true;
    public DailyTriggerMode MorningBriefingMode { get; set; } = DailyTriggerMode.Disabled;
    public string MorningBriefingTime { get; set; } = "08:50";
    public bool DailyWrapUpEnabled { get; set; }
    public string DailyWrapUpTime { get; set; } = "17:50";

    public static AppSettings Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return new();
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? new();
            using var document = JsonDocument.Parse(json);
            var isLegacy = !document.RootElement.TryGetProperty(nameof(SettingsVersion), out var version)
                || version.GetInt32() < 3;
            if (isLegacy)
            {
                settings.SettingsVersion = 4;
                settings.HotKeyVirtualKey = 0x14;
                settings.HotKeyName = "Ctrl + Caps Lock";
                settings.HotKeyModifiers = 1;
                settings.HotKeyPressCount = 1;
            }
            else if (settings.SettingsVersion < 4)
            {
                settings.SettingsVersion = 4;
                settings.MorningBriefingMode = DailyTriggerMode.Disabled;
                settings.MorningBriefingTime = "08:50";
                settings.DailyWrapUpEnabled = false;
                settings.DailyWrapUpTime = "17:50";
            }
            return settings;
        }
        catch { return new(); }
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}

public enum DailyTriggerMode { Disabled, AtAppStartup, AtSpecifiedTime }
