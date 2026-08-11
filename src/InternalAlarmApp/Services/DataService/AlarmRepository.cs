using InternalAlarmApp.Models;

namespace InternalAlarmApp.Services.DataService;

public sealed class AlarmRepository
{
    private readonly string _databasePath;
    private readonly object _gate = new();

    public AlarmRepository(string databasePath) => _databasePath = databasePath;

    public void Initialize()
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("""
                CREATE TABLE IF NOT EXISTS Alarm (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Title TEXT NOT NULL, OriginalText TEXT NOT NULL DEFAULT '',
                    AlarmDateTime TEXT NOT NULL, Description TEXT NOT NULL DEFAULT '',
                    FilePath TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL,
                    UpdatedAt TEXT NOT NULL, Status INTEGER NOT NULL DEFAULT 0,
                    Completed INTEGER NOT NULL DEFAULT 0,
                    RepeatMinutes INTEGER NOT NULL DEFAULT 0, RepeatCount INTEGER NOT NULL DEFAULT 0
                );
                """);
            db.Execute("""
                CREATE TABLE IF NOT EXISTS Reminder (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT, AlarmId INTEGER NOT NULL,
                    ReminderMinutes INTEGER NOT NULL DEFAULT 0, RepeatMinutes INTEGER NOT NULL DEFAULT 0,
                    RepeatCount INTEGER NOT NULL DEFAULT 0, TriggerCount INTEGER NOT NULL DEFAULT 0,
                    ScheduledAt TEXT NOT NULL, Triggered INTEGER NOT NULL DEFAULT 0,
                    TriggeredAt TEXT NULL, Kind INTEGER NOT NULL DEFAULT 0,
                    FOREIGN KEY(AlarmId) REFERENCES Alarm(Id) ON DELETE CASCADE
                );
                """);
            db.Execute("CREATE INDEX IF NOT EXISTS IX_Reminder_Due ON Reminder(Triggered, ScheduledAt);");
        }
    }

    public long Save(Alarm alarm)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                var now = DateTime.Now;
                if (alarm.Id == 0)
                {
                    alarm.CreatedAt = alarm.UpdatedAt = now;
                    db.Execute("INSERT INTO Alarm(Title,OriginalText,AlarmDateTime,Description,FilePath,CreatedAt,UpdatedAt,Status,Completed,RepeatMinutes,RepeatCount) VALUES(?,?,?,?,?,?,?,?,?,?,?)",
                        alarm.Title, alarm.OriginalText, alarm.AlarmDateTime, alarm.Description, alarm.FilePath, alarm.CreatedAt, alarm.UpdatedAt, (int)alarm.Status, alarm.Completed, alarm.RepeatMinutes, alarm.RepeatCount);
                    alarm.Id = db.LastInsertRowId;
                }
                else
                {
                    alarm.UpdatedAt = now;
                    db.Execute("UPDATE Alarm SET Title=?,OriginalText=?,AlarmDateTime=?,Description=?,FilePath=?,UpdatedAt=?,Status=?,Completed=?,RepeatMinutes=?,RepeatCount=? WHERE Id=?",
                        alarm.Title, alarm.OriginalText, alarm.AlarmDateTime, alarm.Description, alarm.FilePath, alarm.UpdatedAt, (int)alarm.Status, alarm.Completed, alarm.RepeatMinutes, alarm.RepeatCount, alarm.Id);
                    db.Execute("DELETE FROM Reminder WHERE AlarmId=? AND Triggered=0", alarm.Id);
                }

                foreach (var minutes in alarm.ReminderMinutes.Distinct().Where(x => x > 0))
                    InsertReminder(db, alarm.Id, alarm.AlarmDateTime.AddMinutes(-minutes), ReminderKind.PreAlert, minutes, 0, 0);
                InsertReminder(db, alarm.Id, alarm.AlarmDateTime, ReminderKind.Primary, 0, alarm.RepeatMinutes, alarm.RepeatCount);
                db.Execute("COMMIT;");
                return alarm.Id;
            }
            catch { try { db.Execute("ROLLBACK;"); } catch { } throw; }
        }
    }

    public IReadOnlyList<Alarm> GetAll()
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var alarms = db.Query("SELECT Id,Title,OriginalText,AlarmDateTime,Description,FilePath,CreatedAt,UpdatedAt,Status,Completed,RepeatMinutes,RepeatCount FROM Alarm ORDER BY AlarmDateTime", MapAlarm);
            foreach (var alarm in alarms)
                alarm.ReminderMinutes = db.Query("SELECT ReminderMinutes FROM Reminder WHERE AlarmId=? AND Kind=? AND ReminderMinutes>0 ORDER BY ReminderMinutes DESC", r => r.Int32(0), alarm.Id, (int)ReminderKind.PreAlert);
            return alarms;
        }
    }

    public Alarm? Get(long id)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var alarm = db.Query("SELECT Id,Title,OriginalText,AlarmDateTime,Description,FilePath,CreatedAt,UpdatedAt,Status,Completed,RepeatMinutes,RepeatCount FROM Alarm WHERE Id=?", MapAlarm, id).FirstOrDefault();
            if (alarm != null)
                alarm.ReminderMinutes = db.Query("SELECT ReminderMinutes FROM Reminder WHERE AlarmId=? AND Kind=? AND ReminderMinutes>0 ORDER BY ReminderMinutes DESC", r => r.Int32(0), id, (int)ReminderKind.PreAlert);
            return alarm;
        }
    }

    public IReadOnlyList<Reminder> GetDue(DateTime through)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
            return db.Query("SELECT Id,AlarmId,ReminderMinutes,RepeatMinutes,RepeatCount,TriggerCount,ScheduledAt,Triggered,TriggeredAt,Kind FROM Reminder WHERE Triggered=0 AND ScheduledAt<=? ORDER BY ScheduledAt", MapReminder, through);
    }

    public DateTime? GetNextTrigger()
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var values = db.Query("SELECT ScheduledAt FROM Reminder WHERE Triggered=0 ORDER BY ScheduledAt LIMIT 1", r => r.DateTime(0));
            return values.Count == 0 ? null : values[0];
        }
    }

    public void MarkTriggered(Reminder reminder, DateTime at)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("UPDATE Reminder SET Triggered=1,TriggeredAt=? WHERE Id=? AND Triggered=0", at, reminder.Id);
            if (reminder.Kind != ReminderKind.PreAlert)
                db.Execute("UPDATE Alarm SET Status=?,UpdatedAt=? WHERE Id=? AND Completed=0", (int)AlarmStatus.Triggered, at, reminder.AlarmId);
        }
    }

    public void AddFollowUp(long alarmId, DateTime when, ReminderKind kind, int repeatMinutes = 0, int repeatCount = 0, int triggerCount = 0)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
            InsertReminder(db, alarmId, when, kind, 0, repeatMinutes, repeatCount, triggerCount);
    }

    public void Complete(long id)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("UPDATE Alarm SET Completed=1,Status=?,UpdatedAt=? WHERE Id=?", (int)AlarmStatus.Completed, DateTime.Now, id);
            db.Execute("UPDATE Reminder SET Triggered=1,TriggeredAt=? WHERE AlarmId=? AND Triggered=0", DateTime.Now, id);
        }
    }

    public void Delete(long id)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
            db.Execute("DELETE FROM Alarm WHERE Id=?", id);
    }

    private static void InsertReminder(WinSqliteConnection db, long alarmId, DateTime at, ReminderKind kind, int reminderMinutes, int repeatMinutes, int repeatCount, int triggerCount = 0) =>
        db.Execute("INSERT INTO Reminder(AlarmId,ReminderMinutes,RepeatMinutes,RepeatCount,TriggerCount,ScheduledAt,Triggered,Kind) VALUES(?,?,?,?,?,?,0,?)",
            alarmId, reminderMinutes, repeatMinutes, repeatCount, triggerCount, at, (int)kind);

    private static Alarm MapAlarm(SqliteRow r) => new()
    {
        Id = r.Int64(0), Title = r.Text(1), OriginalText = r.Text(2), AlarmDateTime = r.DateTime(3),
        Description = r.Text(4), FilePath = r.Text(5), CreatedAt = r.DateTime(6), UpdatedAt = r.DateTime(7),
        Status = (AlarmStatus)r.Int32(8), Completed = r.Bool(9), RepeatMinutes = r.Int32(10), RepeatCount = r.Int32(11)
    };

    private static Reminder MapReminder(SqliteRow r) => new()
    {
        Id = r.Int64(0), AlarmId = r.Int64(1), ReminderMinutes = r.Int32(2), RepeatMinutes = r.Int32(3),
        RepeatCount = r.Int32(4), TriggerCount = r.Int32(5), ScheduledAt = r.DateTime(6), Triggered = r.Bool(7),
        TriggeredAt = r.NullableDateTime(8), Kind = (ReminderKind)r.Int32(9)
    };
}
