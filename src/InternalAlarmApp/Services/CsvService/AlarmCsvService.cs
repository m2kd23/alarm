using System.Globalization;
using System.Text;
using InternalAlarmApp.Models;
using TargetOpenService = InternalAlarmApp.Services.FileOpenService.FileOpenService;

namespace InternalAlarmApp.Services.CsvService;

public sealed class AlarmCsvService
{
    private static readonly string[] BaseHeaders = ["제목", "일시", "상태", "완료시각", "설명", "원문", "사전알림(분)", "반복간격(분)", "반복횟수"];

    public CsvImportPreview Preview(string path, IEnumerable<Alarm> existingAlarms)
    {
        string text;
        try { text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true)); }
        catch (DecoderFallbackException) { return CsvImportPreview.FileError("UTF-8 CSV 파일만 가져올 수 있습니다. Excel에서 'CSV UTF-8' 형식으로 저장해 주세요."); }
        catch (Exception ex) { return CsvImportPreview.FileError($"CSV 파일을 읽을 수 없습니다: {ex.Message}"); }

        List<List<string>> rows;
        try { rows = CsvCodec.Read(text); }
        catch (FormatException ex) { return CsvImportPreview.FileError($"CSV 형식 오류: {ex.Message}"); }
        if (rows.Count == 0) return CsvImportPreview.FileError("CSV 파일에 헤더가 없습니다.");

        var header = rows[0].Select(x => x.Trim().TrimStart('\uFEFF')).ToList();
        if (BaseHeaders.Any(x => !header.Contains(x, StringComparer.Ordinal)))
            return CsvImportPreview.FileError($"필수 열이 없습니다. 필수 열: {string.Join(", ", BaseHeaders)}");
        var index = header.Select((name, position) => (name, position)).ToDictionary(x => x.name, x => x.position, StringComparer.Ordinal);
        var existing = new HashSet<string>(existingAlarms.Select(IdentityKey), StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<CsvImportRow>();

        for (var rowIndex = 1; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            if (row.All(string.IsNullOrWhiteSpace)) continue;
            try
            {
                var alarm = ParseRow(row, index);
                var duplicate = !seen.Add(IdentityKey(alarm)) || existing.Contains(IdentityKey(alarm));
                results.Add(new CsvImportRow(rowIndex + 1, alarm, duplicate, duplicate ? "동일 제목·일시 일정이 이미 있습니다. 기본적으로 건너뜁니다." : "정상"));
            }
            catch (FormatException ex)
            {
                results.Add(new CsvImportRow(rowIndex + 1, null, false, ex.Message));
            }
        }
        return new CsvImportPreview(results, "");
    }

    public void Export(string path, IEnumerable<Alarm> alarms, DateTime fromDate, DateTime toDate)
    {
        var from = fromDate.Date;
        var to = toDate.Date.AddDays(1);
        var header = new List<string>(BaseHeaders);
        for (var i = 1; i <= 10; i++)
        {
            header.Add($"연결{i}유형"); header.Add($"연결{i}이름"); header.Add($"연결{i}대상");
        }
        var rows = new List<IReadOnlyList<string>> { header };
        foreach (var alarm in alarms.Where(x => x.AlarmDateTime >= from && x.AlarmDateTime < to).OrderBy(x => x.AlarmDateTime).ThenBy(x => x.Title, StringComparer.CurrentCulture))
        {
            var row = new List<string>
            {
                alarm.Title, alarm.AlarmDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), alarm.Completed ? "완료" : "예정",
                alarm.CompletedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "", alarm.Description, alarm.OriginalText,
                string.Join(';', alarm.ReminderMinutes.Distinct().Order()), alarm.RepeatMinutes.ToString(CultureInfo.InvariantCulture), alarm.RepeatCount.ToString(CultureInfo.InvariantCulture)
            };
            foreach (var attachment in alarm.Attachments.Take(10))
            {
                row.Add(KindName(attachment.Kind)); row.Add(attachment.DisplayName); row.Add(attachment.Target);
            }
            while (row.Count < header.Count) row.Add("");
            rows.Add(row);
        }
        CsvCodec.Write(path, rows);
    }

    private static Alarm ParseRow(IReadOnlyList<string> row, IReadOnlyDictionary<string, int> index)
    {
        string Value(string name) => index.TryGetValue(name, out var position) && position < row.Count ? UnescapeFormula(row[position]).Trim() : "";
        var title = Value("제목");
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200) throw new FormatException("제목은 1~200자로 입력해 주세요.");
        if (!DateTime.TryParseExact(Value("일시"), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var when))
            throw new FormatException("일시는 yyyy-MM-dd HH:mm 형식이어야 합니다.");
        var status = Value("상태");
        var completed = status == "완료";
        if (status is not "예정" and not "완료") throw new FormatException("상태는 예정 또는 완료여야 합니다.");
        if (!completed && when <= DateTime.Now) throw new FormatException("미완료 일정은 현재보다 이후 시각이어야 합니다.");

        var description = Value("설명");
        if (description.Length > 2000) throw new FormatException("설명은 2,000자 이하여야 합니다.");
        var reminderMinutes = ParsePositiveIntegers(Value("사전알림(분)"), "사전알림(분)");
        var repeatMinutes = ParseNonNegativeInteger(Value("반복간격(분)"), "반복간격(분)");
        var repeatCount = ParseInteger(Value("반복횟수"), "반복횟수");
        if (repeatMinutes == 0) repeatCount = 0;
        if (repeatMinutes > 0 && repeatCount is not (1 or 2 or 3 or 5 or -1)) throw new FormatException("반복횟수는 1, 2, 3, 5 또는 -1이어야 합니다.");

        var alarm = new Alarm
        {
            Title = title,
            AlarmDateTime = when,
            Description = description,
            OriginalText = Value("원문"),
            ReminderMinutes = reminderMinutes,
            RepeatMinutes = repeatMinutes,
            RepeatCount = repeatCount,
            Completed = completed,
            Status = completed ? AlarmStatus.Completed : AlarmStatus.Scheduled
        };
        if (completed && !string.IsNullOrWhiteSpace(Value("완료시각")))
        {
            if (!DateTime.TryParseExact(Value("완료시각"), "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var completedAt))
                throw new FormatException("완료시각은 yyyy-MM-dd HH:mm 형식이어야 합니다.");
            alarm.CompletedAt = completedAt;
        }

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i <= 10; i++)
        {
            var target = Value($"연결{i}대상");
            var kindText = Value($"연결{i}유형");
            var name = Value($"연결{i}이름");
            if (target.Length == 0 && kindText.Length == 0 && name.Length == 0) continue;
            if (target.Length == 0 || !TryParseKind(kindText, out var kind)) throw new FormatException($"연결{i} 대상·유형을 확인해 주세요.");
            var attachment = new AlarmAttachment { Kind = kind, Target = target, DisplayName = name };
            if (!TargetOpenService.IsValidForSaving(attachment, out var error)) throw new FormatException($"연결{i}: {error}");
            if (!keys.Add(TargetOpenService.NormalizedKey(attachment))) throw new FormatException($"연결{i}: 같은 연결 대상이 중복되어 있습니다.");
            if (string.IsNullOrWhiteSpace(attachment.DisplayName)) attachment.DisplayName = TargetOpenService.GetDefaultDisplayName(attachment);
            alarm.Attachments.Add(attachment);
        }
        return alarm;
    }

    private static List<int> ParsePositiveIntegers(string value, string column)
    {
        if (string.IsNullOrWhiteSpace(value)) return [];
        var result = new List<int>();
        foreach (var part in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!int.TryParse(part, out var number) || number <= 0) throw new FormatException($"{column}은 양의 정수를 ;로 구분해 입력해 주세요.");
            result.Add(number);
        }
        return result.Distinct().ToList();
    }

    private static int ParseNonNegativeInteger(string value, string column)
    {
        if (!int.TryParse(value, out var number) || number < 0) throw new FormatException($"{column}은 0 이상의 정수여야 합니다.");
        return number;
    }

    private static int ParseInteger(string value, string column)
    {
        if (!int.TryParse(value, out var number)) throw new FormatException($"{column}은 정수여야 합니다.");
        return number;
    }

    private static bool TryParseKind(string value, out AttachmentKind kind)
    {
        kind = value switch { "파일" => AttachmentKind.File, "폴더" => AttachmentKind.Folder, "웹" or "URL" => AttachmentKind.Web, _ => AttachmentKind.File };
        return value is "파일" or "폴더" or "웹" or "URL";
    }

    private static string KindName(AttachmentKind kind) => kind switch { AttachmentKind.File => "파일", AttachmentKind.Folder => "폴더", _ => "웹" };
    private static string IdentityKey(Alarm alarm) => $"{alarm.Title.Trim()}\u001F{alarm.AlarmDateTime:O}";
    private static string UnescapeFormula(string value) => value.Length >= 2 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@' ? value[1..] : value;
}

public sealed class CsvImportPreview
{
    public IReadOnlyList<CsvImportRow> Rows { get; }
    public string FileErrorMessage { get; }
    public int ValidCount => Rows.Count(x => x.Alarm != null && !x.IsDuplicate && x.Message == "정상");
    public int ErrorCount => Rows.Count(x => x.Alarm == null);
    public int DuplicateCount => Rows.Count(x => x.IsDuplicate);
    public IReadOnlyList<Alarm> ValidAlarms => Rows.Where(x => x.Alarm != null && !x.IsDuplicate && x.Message == "정상").Select(x => x.Alarm!).ToList();

    public CsvImportPreview(IReadOnlyList<CsvImportRow> rows, string fileErrorMessage) { Rows = rows; FileErrorMessage = fileErrorMessage; }
    public static CsvImportPreview FileError(string message) => new([], message);
}

public sealed class CsvImportRow
{
    public int RowNumber { get; }
    public Alarm? Alarm { get; }
    public bool IsDuplicate { get; }
    public string Message { get; }
    public string Title => Alarm?.Title ?? "";
    public string DateTimeText => Alarm?.DisplayDateTime ?? "";
    public string Result => Alarm == null ? "오류" : IsDuplicate ? "중복" : "정상";

    public CsvImportRow(int rowNumber, Alarm? alarm, bool isDuplicate, string message)
    {
        RowNumber = rowNumber; Alarm = alarm; IsDuplicate = isDuplicate; Message = message;
    }
}

internal static class CsvCodec
{
    public static List<List<string>> Read(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var value = new StringBuilder();
        var quoted = false;
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (quoted)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"') { value.Append('"'); index++; }
                    else quoted = false;
                }
                else value.Append(current);
                continue;
            }
            if (current == '"')
            {
                if (value.Length != 0) throw new FormatException("따옴표 앞에 허용되지 않는 문자가 있습니다.");
                quoted = true;
            }
            else if (current == ',') { row.Add(value.ToString()); value.Clear(); }
            else if (current == '\r' || current == '\n')
            {
                if (current == '\r' && index + 1 < text.Length && text[index + 1] == '\n') index++;
                row.Add(value.ToString()); value.Clear(); rows.Add(row); row = [];
            }
            else value.Append(current);
        }
        if (quoted) throw new FormatException("닫히지 않은 따옴표가 있습니다.");
        if (row.Count > 0 || value.Length > 0) { row.Add(value.ToString()); rows.Add(row); }
        return rows;
    }

    public static void Write(string path, IEnumerable<IReadOnlyList<string>> rows)
    {
        var content = string.Join(Environment.NewLine, rows.Select(row => string.Join(',', row.Select(Escape))));
        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }

    private static string Escape(string value)
    {
        var safe = value.Length > 0 && value[0] is '=' or '+' or '-' or '@' ? "'" + value : value;
        return '"' + safe.Replace("\"", "\"\"") + '"';
    }
}
