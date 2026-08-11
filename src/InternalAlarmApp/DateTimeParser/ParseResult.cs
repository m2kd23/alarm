namespace InternalAlarmApp.DateTimeParser;

public sealed record ParseResult(DateTime? DateTime, bool HasDate, bool HasTime, string SuggestedTitle)
{
    public DateTime? Date => HasDate ? DateTime?.Date : null;
    public TimeSpan? Time => HasTime ? DateTime?.TimeOfDay : null;
}
