using InternalAlarmApp.Models;
using InternalAlarmApp.Utils;

namespace InternalAlarmApp.Services.DataService;

public sealed class AlarmRepository
{
    private const int CurrentSchemaVersion = 1;
    private readonly string _databasePath;
    private readonly object _gate = new();

    public AlarmRepository(string databasePath) => _databasePath = databasePath;

    public void Initialize()
    {
        lock (_gate)
        {
            if (RequiresBackupBeforeMigration()) CreatePreMigrationBackup();

            using var db = new WinSqliteConnection(_databasePath);
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                CreateBaseTables(db);
                db.Execute("CREATE TABLE IF NOT EXISTS SchemaInfo (Name TEXT PRIMARY KEY, Value INTEGER NOT NULL);");
                var version = GetSchemaVersion(db);
                if (version < 1) MigrateToVersion1(db);
                SetSchemaVersion(db, CurrentSchemaVersion);
                db.Execute("COMMIT;");
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
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
                SaveCore(db, alarm, DateTime.Now);
                db.Execute("COMMIT;");
                return alarm.Id;
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
        }
    }

    public IReadOnlyList<long> Import(IEnumerable<Alarm> alarms)
    {
        var items = alarms.ToList();
        var now = DateTime.Now;
        if (items.Any(x => !x.Completed && x.AlarmDateTime <= now))
            throw new InvalidOperationException("미완료 과거 일정은 가져올 수 없습니다.");

        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                foreach (var alarm in items)
                {
                    alarm.Id = 0;
                    SaveCore(db, alarm, now);
                }
                db.Execute("COMMIT;");
                return items.Select(x => x.Id).ToList();
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
        }
    }

    public IReadOnlyList<Alarm> GetAll()
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var alarms = db.Query(AlarmSelect + " ORDER BY AlarmDateTime", MapAlarm);
            LoadAttachments(db, alarms);
            LoadReminderMinutes(db, alarms);
            return alarms;
        }
    }

    public Alarm? Get(long id)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var alarm = db.Query(AlarmSelect + " WHERE Id=?", MapAlarm, id).FirstOrDefault();
            if (alarm == null) return null;
            LoadAttachments(db, [alarm]);
            LoadReminderMinutes(db, [alarm]);
            return alarm;
        }
    }

    public IReadOnlyList<Alarm> GetForDate(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        return GetAll().Where(x => x.AlarmDateTime >= start && x.AlarmDateTime < end && x.Status != AlarmStatus.Cancelled).ToList();
    }

    public DailyWorkSummary GetDailySummary(DateTime date)
    {
        var start = date.Date;
        var end = start.AddDays(1);
        var all = GetAll();
        var today = all.Where(x => x.AlarmDateTime >= start && x.AlarmDateTime < end && x.Status != AlarmStatus.Cancelled).ToList();
        return new DailyWorkSummary
        {
            Date = start,
            CompletedTodayCount = all.Count(x => x.CompletedAt >= start && x.CompletedAt < end),
            TodayScheduledCount = today.Count,
            TodayScheduledCompletedCount = today.Count(x => x.Completed),
            TodayAlarms = today,
            IncompleteAlarms = today.Where(x => !x.Completed).ToList()
        };
    }

    public IReadOnlyList<ClaimedAlarmReminder> ClaimDue(DateTime through)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                var due = db.Query("SELECT Id,AlarmId,ReminderMinutes,RepeatMinutes,RepeatCount,TriggerCount,ScheduledAt,Triggered,TriggeredAt,Kind FROM Reminder WHERE Triggered=0 AND ScheduledAt<=? ORDER BY ScheduledAt", MapReminder, through);
                var claimed = new List<ClaimedAlarmReminder>();
                foreach (var group in due.GroupBy(x => x.AlarmId))
                {
                    var alarm = db.Query(AlarmSelect + " WHERE Id=?", MapAlarm, group.Key).FirstOrDefault();
                    if (alarm == null || alarm.Completed || alarm.Status == AlarmStatus.Cancelled)
                    {
                        foreach (var reminder in group)
                            db.Execute("UPDATE Reminder SET Triggered=1,TriggeredAt=? WHERE Id=? AND Triggered=0", through, reminder.Id);
                        continue;
                    }

                    var claimedReminders = new List<Reminder>();
                    foreach (var reminder in group)
                    {
                        if (db.ExecuteAffected("UPDATE Reminder SET Triggered=1,TriggeredAt=? WHERE Id=? AND Triggered=0", through, reminder.Id) == 1)
                            claimedReminders.Add(reminder);
                    }
                    if (claimedReminders.Count == 0) continue;

                    if (claimedReminders.Any(x => x.Kind != ReminderKind.PreAlert))
                        db.Execute("UPDATE Alarm SET Status=?,UpdatedAt=? WHERE Id=? AND Completed=0 AND Status<>?", (int)AlarmStatus.Triggered, through, alarm.Id, (int)AlarmStatus.Cancelled);

                    var notificationReminder = claimedReminders.OrderByDescending(x => x.ScheduledAt).First();
                    if (notificationReminder.RepeatMinutes > 0 && (notificationReminder.RepeatCount < 0 || notificationReminder.TriggerCount < notificationReminder.RepeatCount))
                        InsertReminder(db, alarm.Id, through.AddMinutes(notificationReminder.RepeatMinutes), ReminderKind.Repeat, 0,
                            notificationReminder.RepeatMinutes, notificationReminder.RepeatCount, notificationReminder.TriggerCount + 1);

                    LoadAttachments(db, [alarm]);
                    claimed.Add(new ClaimedAlarmReminder(alarm, notificationReminder));
                }
                db.Execute("COMMIT;");
                return claimed;
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
        }
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

    public bool Snooze(long alarmId, int minutes)
    {
        if (minutes <= 0) return false;
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                var canSnooze = db.Scalar<long>("SELECT COUNT(*) FROM Alarm WHERE Id=? AND Completed=0 AND Status<>?", alarmId, (int)AlarmStatus.Cancelled) > 0;
                if (!canSnooze) { db.Execute("COMMIT;"); return false; }
                InsertReminder(db, alarmId, DateTime.Now.AddMinutes(minutes), ReminderKind.Snooze, 0, 0, 0);
                db.Execute("COMMIT;");
                return true;
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
        }
    }

    public void Complete(long id)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var now = DateTime.Now;
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                db.Execute("UPDATE Alarm SET Completed=1,Status=?,CompletedAt=?,UpdatedAt=? WHERE Id=?", (int)AlarmStatus.Completed, now, now, id);
                db.Execute("UPDATE Reminder SET Triggered=1,TriggeredAt=? WHERE AlarmId=? AND Triggered=0", now, id);
                db.Execute("COMMIT;");
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
        }
    }

    public PostponeResult PostponeToDate(IEnumerable<long> alarmIds, DateTime targetDate)
    {
        var ids = alarmIds.Distinct().ToList();
        var postponed = new List<long>();
        var skipped = new List<long>();
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
        {
            var now = DateTime.Now;
            db.Execute("BEGIN IMMEDIATE;");
            try
            {
                foreach (var id in ids)
                {
                    var alarm = db.Query(AlarmSelect + " WHERE Id=?", MapAlarm, id).FirstOrDefault();
                    if (alarm == null || alarm.Completed || alarm.Status == AlarmStatus.Cancelled) { skipped.Add(id); continue; }
                    var nextAt = targetDate.Date.Add(alarm.AlarmDateTime.TimeOfDay);
                    if (nextAt <= now) { skipped.Add(id); continue; }
                    if (db.ExecuteAffected("UPDATE Alarm SET AlarmDateTime=?,Status=?,Completed=0,CompletedAt=NULL,UpdatedAt=? WHERE Id=? AND Completed=0 AND Status<>?",
                            nextAt, (int)AlarmStatus.Scheduled, now, id, (int)AlarmStatus.Cancelled) != 1)
                    {
                        skipped.Add(id); continue;
                    }
                    db.Execute("DELETE FROM Reminder WHERE AlarmId=?", id);
                    alarm.AlarmDateTime = nextAt;
                    alarm.Status = AlarmStatus.Scheduled;
                    alarm.Completed = false;
                    alarm.CompletedAt = null;
                    RebuildPendingReminders(db, alarm, now);
                    postponed.Add(id);
                }
                db.Execute("COMMIT;");
                return new PostponeResult(postponed, skipped);
            }
            catch
            {
                try { db.Execute("ROLLBACK;"); } catch { }
                throw;
            }
        }
    }

    public void Delete(long id)
    {
        lock (_gate)
        using (var db = new WinSqliteConnection(_databasePath))
            db.Execute("DELETE FROM Alarm WHERE Id=?", id);
    }

    private void SaveCore(WinSqliteConnection db, Alarm alarm, DateTime now)
    {
        var attachments = NormalizeAttachments(alarm);
        alarm.Attachments = attachments;
        alarm.FilePath = attachments.FirstOrDefault()?.Target ?? "";
        if (alarm.Completed)
        {
            alarm.Status = AlarmStatus.Completed;
            alarm.CompletedAt ??= now;
        }
        else
        {
            alarm.CompletedAt = null;
            if (alarm.Status == AlarmStatus.Completed) alarm.Status = AlarmStatus.Scheduled;
        }

        if (alarm.Id == 0)
        {
            alarm.CreatedAt = alarm.UpdatedAt = now;
            db.Execute("INSERT INTO Alarm(Title,OriginalText,AlarmDateTime,Description,FilePath,CreatedAt,UpdatedAt,CompletedAt,Status,Completed,RepeatMinutes,RepeatCount) VALUES(?,?,?,?,?,?,?,?,?,?,?,?)",
                alarm.Title, alarm.OriginalText, alarm.AlarmDateTime, alarm.Description, alarm.FilePath, alarm.CreatedAt, alarm.UpdatedAt, alarm.CompletedAt,
                (int)alarm.Status, alarm.Completed, alarm.RepeatMinutes, alarm.RepeatCount);
            alarm.Id = db.LastInsertRowId;
        }
        else
        {
            alarm.UpdatedAt = now;
            db.Execute("UPDATE Alarm SET Title=?,OriginalText=?,AlarmDateTime=?,Description=?,FilePath=?,UpdatedAt=?,CompletedAt=?,Status=?,Completed=?,RepeatMinutes=?,RepeatCount=? WHERE Id=?",
                alarm.Title, alarm.OriginalText, alarm.AlarmDateTime, alarm.Description, alarm.FilePath, alarm.UpdatedAt, alarm.CompletedAt,
                (int)alarm.Status, alarm.Completed, alarm.RepeatMinutes, alarm.RepeatCount, alarm.Id);
            db.Execute("DELETE FROM Reminder WHERE AlarmId=?", alarm.Id);
            db.Execute("DELETE FROM AlarmAttachment WHERE AlarmId=?", alarm.Id);
        }

        InsertAttachments(db, alarm.Id, attachments);
        if (!alarm.Completed) RebuildPendingReminders(db, alarm, now);
    }

    private static void RebuildPendingReminders(WinSqliteConnection db, Alarm alarm, DateTime now)
    {
        foreach (var minutes in alarm.ReminderMinutes.Distinct().Where(x => x > 0))
        {
            var reminderAt = alarm.AlarmDateTime.AddMinutes(-minutes);
            if (reminderAt > now) InsertReminder(db, alarm.Id, reminderAt, ReminderKind.PreAlert, minutes, 0, 0);
        }
        if (alarm.AlarmDateTime > now)
            InsertReminder(db, alarm.Id, alarm.AlarmDateTime, ReminderKind.Primary, 0, alarm.RepeatMinutes, alarm.RepeatCount);
    }

    private static void InsertAttachments(WinSqliteConnection db, long alarmId, IReadOnlyList<AlarmAttachment> attachments)
    {
        for (var index = 0; index < attachments.Count; index++)
        {
            var attachment = attachments[index];
            db.Execute("INSERT INTO AlarmAttachment(AlarmId,Kind,DisplayName,Target,SortOrder) VALUES(?,?,?,?,?)",
                alarmId, (int)attachment.Kind, attachment.DisplayName, attachment.Target, index);
            attachment.Id = db.LastInsertRowId;
            attachment.AlarmId = alarmId;
            attachment.SortOrder = index;
        }
    }

    private static List<AlarmAttachment> NormalizeAttachments(Alarm alarm)
    {
        var source = alarm.Attachments.Count > 0
            ? alarm.Attachments
            : string.IsNullOrWhiteSpace(alarm.FilePath) ? [] : [new AlarmAttachment { Target = alarm.FilePath, Kind = InferAttachmentKind(alarm.FilePath) }];
        var result = new List<AlarmAttachment>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in source)
        {
            var target = item.Target.Trim();
            if (target.Length == 0) continue;
            var key = $"{(int)item.Kind}:{target}";
            if (!seen.Add(key)) continue;
            result.Add(new AlarmAttachment
            {
                Kind = item.Kind,
                Target = target,
                DisplayName = string.IsNullOrWhiteSpace(item.DisplayName) ? GetDefaultAttachmentName(target, item.Kind) : item.DisplayName.Trim()
            });
            if (result.Count == 20) break;
        }
        return result;
    }

    private static AttachmentKind InferAttachmentKind(string target) =>
        Uri.TryCreate(target, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? AttachmentKind.Web :
        Directory.Exists(target) || !Path.HasExtension(target) ? AttachmentKind.Folder : AttachmentKind.File;

    private static string GetDefaultAttachmentName(string target, AttachmentKind kind)
    {
        if (kind == AttachmentKind.Web && Uri.TryCreate(target, UriKind.Absolute, out var uri)) return uri.Host;
        var name = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? target : name;
    }

    private static void LoadAttachments(WinSqliteConnection db, IEnumerable<Alarm> alarms)
    {
        var byId = alarms.ToDictionary(x => x.Id);
        if (byId.Count == 0) return;
        foreach (var alarm in byId.Values) alarm.Attachments = [];
        var attachments = db.Query("SELECT Id,AlarmId,Kind,DisplayName,Target,SortOrder FROM AlarmAttachment ORDER BY AlarmId,SortOrder,Id", MapAttachment);
        foreach (var attachment in attachments)
            if (byId.TryGetValue(attachment.AlarmId, out var alarm)) alarm.Attachments.Add(attachment);
    }

    private static void LoadReminderMinutes(WinSqliteConnection db, IEnumerable<Alarm> alarms)
    {
        foreach (var alarm in alarms)
            alarm.ReminderMinutes = db.Query("SELECT ReminderMinutes FROM Reminder WHERE AlarmId=? AND Kind=? AND ReminderMinutes>0 ORDER BY ReminderMinutes DESC", r => r.Int32(0), alarm.Id, (int)ReminderKind.PreAlert);
    }

    private bool RequiresBackupBeforeMigration()
    {
        using var db = new WinSqliteConnection(_databasePath);
        if (!TableExists(db, "Alarm")) return false;
        if (!TableExists(db, "SchemaInfo")) return true;
        return GetSchemaVersion(db) < CurrentSchemaVersion;
    }

    private void CreatePreMigrationBackup()
    {
        using (var db = new WinSqliteConnection(_databasePath)) db.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        var directory = Path.GetDirectoryName(_databasePath)!;
        var name = Path.GetFileNameWithoutExtension(_databasePath);
        var backup = Path.Combine(directory, $"{name}.pre-upgrade-{DateTime.Now:yyyyMMddHHmmss}.db");
        File.Copy(_databasePath, backup, overwrite: false);
        Logger.Info($"데이터베이스 마이그레이션 전 백업: {backup}");
    }

    private static void CreateBaseTables(WinSqliteConnection db)
    {
        db.Execute("""
            CREATE TABLE IF NOT EXISTS Alarm (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL, OriginalText TEXT NOT NULL DEFAULT '',
                AlarmDateTime TEXT NOT NULL, Description TEXT NOT NULL DEFAULT '',
                FilePath TEXT NOT NULL DEFAULT '', CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL, CompletedAt TEXT NULL,
                Status INTEGER NOT NULL DEFAULT 0, Completed INTEGER NOT NULL DEFAULT 0,
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

    private static void MigrateToVersion1(WinSqliteConnection db)
    {
        if (!HasColumn(db, "Alarm", "CompletedAt")) db.Execute("ALTER TABLE Alarm ADD COLUMN CompletedAt TEXT NULL;");
        db.Execute("""
            CREATE TABLE IF NOT EXISTS AlarmAttachment (
                Id INTEGER PRIMARY KEY AUTOINCREMENT, AlarmId INTEGER NOT NULL,
                Kind INTEGER NOT NULL, DisplayName TEXT NOT NULL DEFAULT '', Target TEXT NOT NULL,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                FOREIGN KEY(AlarmId) REFERENCES Alarm(Id) ON DELETE CASCADE
            );
            """);
        db.Execute("CREATE INDEX IF NOT EXISTS IX_AlarmAttachment_AlarmId ON AlarmAttachment(AlarmId, SortOrder);");
        db.Execute("UPDATE Alarm SET CompletedAt=UpdatedAt WHERE Completed=1 AND CompletedAt IS NULL;");
        var legacyTargets = db.Query("SELECT Id,FilePath FROM Alarm WHERE trim(FilePath)<>''", r => (Id: r.Int64(0), Target: r.Text(1)));
        foreach (var legacy in legacyTargets)
        {
            var kind = InferAttachmentKind(legacy.Target);
            db.Execute("INSERT INTO AlarmAttachment(AlarmId,Kind,DisplayName,Target,SortOrder) VALUES(?,?,?,?,0)",
                legacy.Id, (int)kind, GetDefaultAttachmentName(legacy.Target, kind), legacy.Target);
        }
    }

    private static bool TableExists(WinSqliteConnection db, string table) =>
        db.Scalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=?", table) > 0;

    private static bool HasColumn(WinSqliteConnection db, string table, string column) =>
        db.Query($"PRAGMA table_info({table})", r => r.Text(1)).Any(x => string.Equals(x, column, StringComparison.OrdinalIgnoreCase));

    private static int GetSchemaVersion(WinSqliteConnection db)
    {
        if (!TableExists(db, "SchemaInfo")) return 0;
        return (int)db.Scalar<long>("SELECT Value FROM SchemaInfo WHERE Name='SchemaVersion'");
    }

    private static void SetSchemaVersion(WinSqliteConnection db, int version)
    {
        db.Execute("DELETE FROM SchemaInfo WHERE Name='SchemaVersion'");
        db.Execute("INSERT INTO SchemaInfo(Name,Value) VALUES('SchemaVersion',?)", version);
    }

    private static void InsertReminder(WinSqliteConnection db, long alarmId, DateTime at, ReminderKind kind, int reminderMinutes, int repeatMinutes, int repeatCount, int triggerCount = 0) =>
        db.Execute("INSERT INTO Reminder(AlarmId,ReminderMinutes,RepeatMinutes,RepeatCount,TriggerCount,ScheduledAt,Triggered,Kind) VALUES(?,?,?,?,?,?,0,?)",
            alarmId, reminderMinutes, repeatMinutes, repeatCount, triggerCount, at, (int)kind);

    private const string AlarmSelect = "SELECT Id,Title,OriginalText,AlarmDateTime,Description,FilePath,CreatedAt,UpdatedAt,CompletedAt,Status,Completed,RepeatMinutes,RepeatCount FROM Alarm";

    private static Alarm MapAlarm(SqliteRow r) => new()
    {
        Id = r.Int64(0), Title = r.Text(1), OriginalText = r.Text(2), AlarmDateTime = r.DateTime(3),
        Description = r.Text(4), FilePath = r.Text(5), CreatedAt = r.DateTime(6), UpdatedAt = r.DateTime(7), CompletedAt = r.NullableDateTime(8),
        Status = (AlarmStatus)r.Int32(9), Completed = r.Bool(10), RepeatMinutes = r.Int32(11), RepeatCount = r.Int32(12)
    };

    private static AlarmAttachment MapAttachment(SqliteRow r) => new()
    {
        Id = r.Int64(0), AlarmId = r.Int64(1), Kind = (AttachmentKind)r.Int32(2), DisplayName = string.IsNullOrWhiteSpace(r.Text(3)) ? GetDefaultAttachmentName(r.Text(4), (AttachmentKind)r.Int32(2)) : r.Text(3), Target = r.Text(4), SortOrder = r.Int32(5)
    };

    private static Reminder MapReminder(SqliteRow r) => new()
    {
        Id = r.Int64(0), AlarmId = r.Int64(1), ReminderMinutes = r.Int32(2), RepeatMinutes = r.Int32(3),
        RepeatCount = r.Int32(4), TriggerCount = r.Int32(5), ScheduledAt = r.DateTime(6), Triggered = r.Bool(7),
        TriggeredAt = r.NullableDateTime(8), Kind = (ReminderKind)r.Int32(9)
    };
}
