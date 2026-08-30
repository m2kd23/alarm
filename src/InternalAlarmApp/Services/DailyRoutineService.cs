using System.Globalization;
using InternalAlarmApp.Models;

namespace InternalAlarmApp.Services;

public sealed class DailyRoutineService : IDisposable
{
    private readonly object _gate = new();
    private AppSettings _settings;
    private System.Threading.Timer? _timer;
    private bool _disposed;

    public event EventHandler? MorningBriefingDue;
    public event EventHandler? DailyWrapUpDue;

    public DailyRoutineService(AppSettings settings) => _settings = settings;

    public void Start()
    {
        if (_settings.MorningBriefingMode == DailyTriggerMode.AtAppStartup)
            MorningBriefingDue?.Invoke(this, EventArgs.Empty);
        Reschedule();
    }

    public void Update(AppSettings settings)
    {
        _settings = settings;
        Reschedule();
    }

    public void ShowMorningBriefingNow() => MorningBriefingDue?.Invoke(this, EventArgs.Empty);
    public void ShowDailyWrapUpNow() => DailyWrapUpDue?.Invoke(this, EventArgs.Empty);

    public void Reschedule()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _timer?.Dispose();
            var now = DateTime.Now;
            var next = GetNextDue(now);
            if (!next.HasValue) return;
            var delay = next.Value - now;
            if (delay < TimeSpan.FromMilliseconds(100)) delay = TimeSpan.FromMilliseconds(100);
            _timer = new System.Threading.Timer(_ => RaiseDue(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private DateTime? GetNextDue(DateTime now)
    {
        var values = new List<DateTime>();
        if (_settings.MorningBriefingMode == DailyTriggerMode.AtSpecifiedTime && TryGetTime(_settings.MorningBriefingTime, out var morning))
            values.Add(GetNextOccurrence(now, morning));
        if (_settings.DailyWrapUpEnabled && TryGetTime(_settings.DailyWrapUpTime, out var wrapUp))
            values.Add(GetNextOccurrence(now, wrapUp));
        return values.Count == 0 ? null : values.Min();
    }

    private void RaiseDue()
    {
        try
        {
            var now = DateTime.Now;
            if (_settings.MorningBriefingMode == DailyTriggerMode.AtSpecifiedTime
                && TryGetTime(_settings.MorningBriefingTime, out var morning)
                && IsDueNow(now, morning))
                MorningBriefingDue?.Invoke(this, EventArgs.Empty);

            if (_settings.DailyWrapUpEnabled
                && TryGetTime(_settings.DailyWrapUpTime, out var wrapUp)
                && IsDueNow(now, wrapUp))
                DailyWrapUpDue?.Invoke(this, EventArgs.Empty);
        }
        finally { Reschedule(); }
    }

    private static bool TryGetTime(string value, out TimeSpan time) =>
        TimeSpan.TryParseExact(value, [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out time);

    private static DateTime GetNextOccurrence(DateTime now, TimeSpan time)
    {
        var result = now.Date.Add(time);
        return result <= now ? result.AddDays(1) : result;
    }

    private static bool IsDueNow(DateTime now, TimeSpan time)
    {
        var scheduled = now.Date.Add(time);
        return now >= scheduled && now - scheduled < TimeSpan.FromMinutes(1);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _timer?.Dispose();
            _timer = null;
        }
    }
}
