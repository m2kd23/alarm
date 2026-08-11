using InternalAlarmApp.Models;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Utils;

namespace InternalAlarmApp.Services.AlarmService;

public sealed class AlarmScheduler : IDisposable
{
    private readonly AlarmRepository _repository;
    private readonly object _gate = new();
    private System.Threading.Timer? _timer;
    private bool _checking;

    public event EventHandler<AlarmDueEventArgs>? AlarmDue;

    public AlarmScheduler(AlarmRepository repository) => _repository = repository;

    public void Start() => Reschedule(immediate: true);

    public void Reschedule(bool immediate = false)
    {
        lock (_gate)
        {
            _timer?.Dispose();
            var next = _repository.GetNextTrigger();
            var delay = immediate ? TimeSpan.FromMilliseconds(100) : next.HasValue ? next.Value - DateTime.Now : TimeSpan.FromHours(12);
            if (delay < TimeSpan.FromMilliseconds(100)) delay = TimeSpan.FromMilliseconds(100);
            if (delay > TimeSpan.FromHours(12)) delay = TimeSpan.FromHours(12);
            _timer = new System.Threading.Timer(_ => CheckDue(), null, delay, Timeout.InfiniteTimeSpan);
        }
    }

    private void CheckDue()
    {
        lock (_gate)
        {
            if (_checking) return;
            _checking = true;
        }
        try
        {
            var now = DateTime.Now;
            var due = _repository.GetDue(now.AddSeconds(1));
            foreach (var group in due.GroupBy(x => x.AlarmId))
            {
                var alarm = _repository.Get(group.Key);
                foreach (var item in group) _repository.MarkTriggered(item, now);
                if (alarm is null || alarm.Completed || alarm.Status == AlarmStatus.Cancelled) continue;

                var reminder = group.OrderByDescending(x => x.ScheduledAt).First();
                AlarmDue?.Invoke(this, new AlarmDueEventArgs(alarm, reminder));
                if (reminder.RepeatMinutes > 0 && (reminder.RepeatCount < 0 || reminder.TriggerCount < reminder.RepeatCount))
                    _repository.AddFollowUp(alarm.Id, now.AddMinutes(reminder.RepeatMinutes), ReminderKind.Repeat,
                        reminder.RepeatMinutes, reminder.RepeatCount, reminder.TriggerCount + 1);
            }
        }
        catch (Exception ex) { Logger.Error("알림 스케줄러", ex); }
        finally
        {
            lock (_gate) _checking = false;
            Reschedule();
        }
    }

    public void Snooze(long alarmId, int minutes)
    {
        _repository.AddFollowUp(alarmId, DateTime.Now.AddMinutes(minutes), ReminderKind.Snooze);
        Reschedule();
    }

    public void Dispose() { lock (_gate) { _timer?.Dispose(); _timer = null; } }
}

public sealed class AlarmDueEventArgs(Alarm alarm, Reminder reminder) : EventArgs
{
    public Alarm Alarm { get; } = alarm;
    public Reminder Reminder { get; } = reminder;
}
