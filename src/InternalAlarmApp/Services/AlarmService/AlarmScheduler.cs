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
            var due = _repository.ClaimDue(now.AddSeconds(1));
            foreach (var item in due)
            {
                AlarmDue?.Invoke(this, new AlarmDueEventArgs(item.Alarm, item.Reminder));
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
        if (_repository.Snooze(alarmId, minutes)) Reschedule();
    }

    public void Dispose() { lock (_gate) { _timer?.Dispose(); _timer = null; } }
}

public sealed class AlarmDueEventArgs(Alarm alarm, Reminder reminder) : EventArgs
{
    public Alarm Alarm { get; } = alarm;
    public Reminder Reminder { get; } = reminder;
}
