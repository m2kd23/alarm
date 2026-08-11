using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using InternalAlarmApp.DateTimeParser;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.DataService;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace InternalAlarmApp.Views;

public partial class AlarmEditorWindow : Window
{
    private readonly AlarmRepository _repository;
    private readonly AlarmScheduler _scheduler;
    private readonly Alarm _alarm;

    public AlarmEditorWindow(AlarmRepository repository, AlarmScheduler scheduler, KoreanDateTimeParser parser, string originalText = "", Alarm? existing = null)
    {
        InitializeComponent();
        _repository = repository; _scheduler = scheduler;
        _alarm = existing ?? new Alarm();

        if (existing != null) LoadAlarm(existing);
        else
        {
            OriginalTextBox.Text = originalText;
            var parsed = parser.Parse(originalText, DateTime.Now);
            TitleBox.Text = parsed.SuggestedTitle;
            DateBox.SelectedDate = parsed.HasDate ? parsed.Date : null;
            TimeBox.Text = parsed.HasTime ? parsed.Time!.Value.ToString(@"hh\:mm") : "";
        }
    }

    private void LoadAlarm(Alarm alarm)
    {
        OriginalTextBox.Text = alarm.OriginalText;
        TitleBox.Text = alarm.Title;
        DateBox.SelectedDate = alarm.AlarmDateTime.Date;
        TimeBox.Text = alarm.AlarmDateTime.ToString("HH:mm");
        DescriptionBox.Text = alarm.Description;
        PathBox.Text = alarm.FilePath;
        R5.IsChecked = alarm.ReminderMinutes.Contains(5); R10.IsChecked = alarm.ReminderMinutes.Contains(10);
        R30.IsChecked = alarm.ReminderMinutes.Contains(30); R60.IsChecked = alarm.ReminderMinutes.Contains(60); R1440.IsChecked = alarm.ReminderMinutes.Contains(1440);
        SelectByTag(RepeatInterval, alarm.RepeatMinutes);
        SelectByTag(RepeatCount, alarm.RepeatCount == 0 ? 1 : alarm.RepeatCount);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TitleBox.Text)) { Warn("제목을 입력해 주세요.", TitleBox); return; }
        if (!DateBox.SelectedDate.HasValue) { Warn("날짜를 선택해 주세요.", DateBox); return; }
        if (!TimeSpan.TryParseExact(TimeBox.Text.Trim(), [@"h\:mm", @"hh\:mm"], CultureInfo.InvariantCulture, out var time)) { Warn("시간을 HH:mm 형식으로 입력해 주세요.", TimeBox); return; }
        var when = DateBox.SelectedDate.Value.Date.Add(time);
        if (when <= DateTime.Now) { Warn("현재보다 이후 시간을 입력해 주세요.", DateBox); return; }

        var reminders = new List<int>();
        foreach (var check in new[] { R5, R10, R30, R60, R1440 }) if (check.IsChecked == true) reminders.Add(int.Parse(check.Tag.ToString()!));
        if (!string.IsNullOrWhiteSpace(CustomReminderBox.Text))
        {
            if (!int.TryParse(CustomReminderBox.Text, out var custom) || custom <= 0) { Warn("직접 지정 사전 알림은 1 이상의 숫자로 입력해 주세요.", CustomReminderBox); return; }
            custom *= int.Parse(((ComboBoxItem)CustomReminderUnit.SelectedItem).Tag.ToString()!);
            if (when.AddMinutes(-custom) <= DateTime.Now) { Warn("사전 알림 시간이 이미 지났습니다.", CustomReminderBox); return; }
            reminders.Add(custom);
        }

        var path = PathBox.Text.Trim();
        if (path.Length > 0 && !File.Exists(path) && !Directory.Exists(path))
        {
            var answer = MessageBox.Show(this, "입력한 파일 또는 폴더가 현재 존재하지 않습니다. 경로를 그대로 저장할까요?", "경로 확인", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (answer != MessageBoxResult.Yes) return;
        }

        _alarm.Title = TitleBox.Text.Trim(); _alarm.OriginalText = OriginalTextBox.Text.Trim(); _alarm.AlarmDateTime = when;
        _alarm.Description = DescriptionBox.Text.Trim(); _alarm.FilePath = path; _alarm.ReminderMinutes = reminders.Distinct().ToList();
        _alarm.RepeatMinutes = TagValue(RepeatInterval); _alarm.RepeatCount = _alarm.RepeatMinutes == 0 ? 0 : TagValue(RepeatCount);
        _alarm.Status = AlarmStatus.Scheduled; _alarm.Completed = false;
        try
        {
            _repository.Save(_alarm);
            _scheduler.Reschedule();
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, $"알림을 저장하지 못했습니다.\n{ex.Message}", "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "알림에 연결할 파일 선택", CheckFileExists = true, Filter = "모든 파일|*.*" };
        if (dialog.ShowDialog(this) == true) PathBox.Text = dialog.FileName;
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "알림에 연결할 폴더 선택", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) PathBox.Text = dialog.SelectedPath;
    }

    private void Warn(string message, Control control)
    {
        MessageBox.Show(this, message, "입력 확인", MessageBoxButton.OK, MessageBoxImage.Information);
        control.Focus();
    }
    private static int TagValue(ComboBox combo) => int.Parse(((ComboBoxItem)combo.SelectedItem).Tag.ToString()!);
    private static void SelectByTag(ComboBox combo, int value)
    {
        foreach (ComboBoxItem item in combo.Items) if (item.Tag?.ToString() == value.ToString()) { combo.SelectedItem = item; return; }
    }
}
