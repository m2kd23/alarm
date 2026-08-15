namespace InternalAlarmApp.Models;

public sealed class Alarm
{
    public long Id { get; set; }
    public string Title { get; set; } = "";
    public string OriginalText { get; set; } = "";
    public DateTime AlarmDateTime { get; set; }
    public string Description { get; set; } = "";
    public string FilePath { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public AlarmStatus Status { get; set; } = AlarmStatus.Scheduled;
    public bool Completed { get; set; }
    public List<int> ReminderMinutes { get; set; } = [];
    public int RepeatMinutes { get; set; }
    public int RepeatCount { get; set; }

    public string DisplayDateTime => AlarmDateTime.ToString("yyyy-MM-dd HH:mm");
    public string DisplayStatus => Status switch
    {
        AlarmStatus.Scheduled => "예정",
        AlarmStatus.Completed => "완료",
        AlarmStatus.Cancelled => "취소",
        _ => "알림됨"
    };
    public string ReminderSummary => ReminderMinutes.Count == 0
        ? "정시"
        : string.Join(", ", ReminderMinutes.OrderDescending().Select(x => x >= 1440 ? $"{x / 1440}일 전" : x >= 60 ? $"{x / 60}시간 전" : $"{x}분 전"));
    public string FileName
    {
        get
        {
            if (string.IsNullOrWhiteSpace(FilePath)) return "";
            if (Uri.TryCreate(FilePath, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") return uri.Host;
            return Path.GetFileName(FilePath.TrimEnd(Path.DirectorySeparatorChar));
        }
    }
}

public enum AlarmStatus { Scheduled, Triggered, Completed, Cancelled }

public sealed class Reminder
{
    public long Id { get; set; }
    public long AlarmId { get; set; }
    public int ReminderMinutes { get; set; }
    public int RepeatMinutes { get; set; }
    public int RepeatCount { get; set; }
    public int TriggerCount { get; set; }
    public DateTime ScheduledAt { get; set; }
    public bool Triggered { get; set; }
    public DateTime? TriggeredAt { get; set; }
    public ReminderKind Kind { get; set; }
}

public enum ReminderKind { PreAlert, Primary, Repeat, Snooze }
