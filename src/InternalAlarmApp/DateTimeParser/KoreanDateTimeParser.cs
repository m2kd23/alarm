using System.Globalization;
using System.Text.RegularExpressions;

namespace InternalAlarmApp.DateTimeParser;

public sealed partial class KoreanDateTimeParser
{
    public ParseResult Parse(string? text, DateTime now)
    {
        text = (text ?? "").Trim();
        if (text.Length == 0) return new(null, false, false, "");

        var timeMatch = FindTime(text);
        var dateMatch = FindDate(text, now, timeMatch.Time);
        DateTime? result = null;
        if (dateMatch.Date.HasValue || timeMatch.Time.HasValue)
        {
            var date = dateMatch.Date ?? now.Date;
            var time = timeMatch.Time ?? TimeSpan.Zero;
            result = date.Add(time);
        }

        var title = text;
        foreach (var token in new[] { dateMatch.Token, timeMatch.Token }.Where(x => !string.IsNullOrWhiteSpace(x)).OrderByDescending(x => x!.Length))
            title = RemoveOnce(title, token!);
        title = CleanupTitle(title);
        return new(result, dateMatch.Date.HasValue, timeMatch.Time.HasValue, title);
    }

    private static (DateTime? Date, string? Token) FindDate(string text, DateTime now, TimeSpan? parsedTime)
    {
        var relative = RelativeDateRegex().Match(text);
        if (relative.Success)
        {
            var offset = relative.Value switch { "오늘" => 0, "내일" => 1, "모레" => 2, _ => 0 };
            return (now.Date.AddDays(offset), relative.Value);
        }

        var numeric = NumericDateRegex().Match(text);
        if (numeric.Success)
        {
            var year = numeric.Groups["y"].Success ? int.Parse(numeric.Groups["y"].Value) : now.Year;
            var month = int.Parse(numeric.Groups["m"].Value);
            var day = int.Parse(numeric.Groups["d"].Value);
            try
            {
                var date = new DateTime(year, month, day);
                if (!numeric.Groups["y"].Success && date < now.Date) date = date.AddYears(1);
                return (date, numeric.Value);
            }
            catch (ArgumentOutOfRangeException) { return (null, null); }
        }

        var weekday = WeekdayRegex().Match(text);
        if (weekday.Success)
        {
            var target = weekday.Groups["day"].Value switch
            {
                "월" => DayOfWeek.Monday, "화" => DayOfWeek.Tuesday, "수" => DayOfWeek.Wednesday,
                "목" => DayOfWeek.Thursday, "금" => DayOfWeek.Friday, "토" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday
            };
            var mondayOffset = ((int)now.DayOfWeek + 6) % 7;
            var thisMonday = now.Date.AddDays(-mondayOffset);
            DateTime date;
            if (weekday.Groups["prefix"].Value == "다음주") date = thisMonday.AddDays(7 + MondayIndex(target));
            else if (weekday.Groups["prefix"].Value == "이번주")
            {
                date = thisMonday.AddDays(MondayIndex(target));
                if (date < now.Date) date = date.AddDays(7);
            }
            else
            {
                var days = ((int)target - (int)now.DayOfWeek + 7) % 7;
                date = now.Date.AddDays(days);
                if (days == 0 && (!parsedTime.HasValue || parsedTime.Value <= now.TimeOfDay)) date = date.AddDays(7);
            }
            return (date, weekday.Value);
        }

        return (null, null);
    }

    private static (TimeSpan? Time, string? Token) FindTime(string text)
    {
        var korean = KoreanTimeRegex().Match(text);
        if (korean.Success)
        {
            var hour = int.Parse(korean.Groups["h"].Value);
            var minute = korean.Groups["min"].Success ? int.Parse(korean.Groups["min"].Value) : 0;
            var meridiem = korean.Groups["ampm"].Value;
            if (meridiem == "오후" && hour < 12) hour += 12;
            if (meridiem == "오전" && hour == 12) hour = 0;
            if (hour is >= 0 and <= 23 && minute is >= 0 and <= 59) return (new TimeSpan(hour, minute, 0), korean.Value);
        }

        var colon = ColonTimeRegex().Match(text);
        if (colon.Success)
        {
            var hour = int.Parse(colon.Groups["h"].Value);
            var minute = int.Parse(colon.Groups["min"].Value);
            if (hour <= 23 && minute <= 59) return (new TimeSpan(hour, minute, 0), colon.Value);
        }
        var period = PeriodDeadlineRegex().Match(text);
        if (period.Success) return (period.Groups["period"].Value == "오전" ? new TimeSpan(12, 0, 0) : new TimeSpan(18, 0, 0), period.Value);
        return (null, null);
    }

    private static int MondayIndex(DayOfWeek day) => ((int)day + 6) % 7;

    private static string RemoveOnce(string source, string value)
    {
        var index = source.IndexOf(value, StringComparison.Ordinal);
        return index < 0 ? source : source.Remove(index, value.Length);
    }

    private static string CleanupTitle(string title)
    {
        title = Regex.Replace(title, @"(^|\s)(까지|에|알림|일정)(?=\s|$)", " ");
        title = Regex.Replace(title, @"\s+", " ").Trim(' ', ',', '.', '-', ':');
        return string.IsNullOrWhiteSpace(title) ? "업무 알림" : title;
    }

    [GeneratedRegex(@"오늘|내일|모레", RegexOptions.CultureInvariant)]
    private static partial Regex RelativeDateRegex();

    [GeneratedRegex(@"(?:(?<y>20\d{2})\s*[년./-]\s*)?(?<m>1[0-2]|0?[1-9])\s*(?:월|[./-])\s*(?<d>3[01]|[12]\d|0?[1-9])\s*일?", RegexOptions.CultureInvariant)]
    private static partial Regex NumericDateRegex();

    [GeneratedRegex(@"(?<prefix>이번주|다음주)?\s*(?<day>[월화수목금토일])요일", RegexOptions.CultureInvariant)]
    private static partial Regex WeekdayRegex();

    [GeneratedRegex(@"(?<ampm>오전|오후)?\s*(?<h>[01]?\d|2[0-3])\s*시(?:\s*(?<min>[0-5]?\d)\s*분)?", RegexOptions.CultureInvariant)]
    private static partial Regex KoreanTimeRegex();

    [GeneratedRegex(@"(?<!\d)(?<h>[01]?\d|2[0-3]):(?<min>[0-5]\d)(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex ColonTimeRegex();

    [GeneratedRegex(@"(?<period>오전|오후)까지", RegexOptions.CultureInvariant)]
    private static partial Regex PeriodDeadlineRegex();
}
