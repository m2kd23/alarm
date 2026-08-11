using InternalAlarmApp.DateTimeParser;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.FileOpenService;

var failures = new List<string>();
var parser = new KoreanDateTimeParser();
var now = new DateTime(2026, 8, 11, 9, 0, 0);

Check("8월 20일 오후 3시 기금운용 보고", new DateTime(2026, 8, 20, 15, 0, 0));
Check("내일 오전 10시 회의", new DateTime(2026, 8, 12, 10, 0, 0));
Check("8/25 14:30 자료 제출", new DateTime(2026, 8, 25, 14, 30, 0));
Check("금요일 오후 4시까지 제출", new DateTime(2026, 8, 14, 16, 0, 0));
var sameFriday = parser.Parse("금요일 오후 4시 검토", new DateTime(2026, 8, 14, 9, 0, 0));
Assert(sameFriday.DateTime == new DateTime(2026, 8, 14, 16, 0, 0), "같은 요일의 미래 시각은 오늘로 해석");
var periodDeadline = parser.Parse("내일 오전까지 회신", now);
Assert(periodDeadline.DateTime == new DateTime(2026, 8, 12, 12, 0, 0), "오전까지 해석");
var noDate = parser.Parse("기금운용 보고자료 작성", now);
Assert(!noDate.HasDate && !noDate.HasTime, "날짜 없는 문장은 날짜/시간 없음이어야 함");

var temp = Path.Combine(Path.GetTempPath(), $"InternalAlarmApp-test-{Guid.NewGuid():N}", "alarm.db");
try
{
    var repository = new AlarmRepository(temp);
    repository.Initialize();
    var alarm = new Alarm { Title = "저장 테스트", OriginalText = "내일 오전 10시 저장 테스트", AlarmDateTime = now.AddHours(1), ReminderMinutes = [10], RepeatMinutes = 5, RepeatCount = 3 };
    repository.Save(alarm);
    var loaded = repository.Get(alarm.Id);
    Assert(loaded?.Title == alarm.Title && loaded.ReminderMinutes.SequenceEqual([10]), "SQLite 저장/조회");
    Assert(repository.GetNextTrigger().HasValue, "다음 알림 계산");
    repository.Complete(alarm.Id);
    Assert(repository.Get(alarm.Id)?.Completed == true, "완료 처리");
    Assert(repository.GetNextTrigger() is null, "대기 알림이 없으면 다음 시각 없음");

    var dueAlarm = new Alarm { Title = "스케줄러 테스트", AlarmDateTime = DateTime.Now.AddMilliseconds(400), RepeatMinutes = 5, RepeatCount = 1 };
    repository.Save(dueAlarm);
    using (var signal = new ManualResetEventSlim())
    using (var scheduler = new AlarmScheduler(repository))
    {
        scheduler.AlarmDue += (_, args) => { if (args.Alarm.Id == dueAlarm.Id) signal.Set(); };
        scheduler.Start();
        Assert(signal.Wait(TimeSpan.FromSeconds(5)), "예약 시각 알림 발생");
        scheduler.Snooze(dueAlarm.Id, 10);
        Assert(repository.GetNextTrigger().HasValue, "Snooze 예약");
    }
    repository.Complete(dueAlarm.Id);

    var fileOpen = new FileOpenService();
    Assert(!fileOpen.Open(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), out _), "존재하지 않는 경로 차단");
}
finally
{
    try { Directory.Delete(Path.GetDirectoryName(temp)!, true); } catch { }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine($"FAIL ({failures.Count})");
    foreach (var failure in failures) Console.Error.WriteLine("- " + failure);
    return 1;
}
Console.WriteLine("PASS: parser 7 cases, SQLite persistence, scheduler, snooze, safe file validation");
return 0;

void Check(string text, DateTime expected)
{
    var result = parser.Parse(text, now);
    Assert(result.DateTime == expected, $"'{text}' => {result.DateTime:O}, expected {expected:O}");
}

void Assert(bool condition, string message)
{
    if (!condition) failures.Add(message);
}
