using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using InternalAlarmApp.DateTimeParser;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Services.FileOpenService;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace InternalAlarmApp.Views;

public partial class AlarmEditorWindow : Window
{
    private readonly AlarmRepository _repository;
    private readonly AlarmScheduler _scheduler;
    private readonly Alarm _alarm;
    private readonly ObservableCollection<AlarmAttachment> _attachments = [];

    public AlarmEditorWindow(AlarmRepository repository, AlarmScheduler scheduler, KoreanDateTimeParser parser, string originalText = "", string initialTarget = "", string contextTitle = "", Alarm? existing = null)
    {
        InitializeComponent();
        _repository = repository; _scheduler = scheduler;
        _alarm = existing ?? new Alarm();
        AttachmentsList.ItemsSource = _attachments;

        if (existing != null) LoadAlarm(existing);
        else
        {
            OriginalTextBox.Text = originalText;
            var parsed = parser.Parse(originalText, DateTime.Now);
            TitleBox.Text = parsed.SuggestedTitle == "업무 알림" && !string.IsNullOrWhiteSpace(contextTitle) ? contextTitle : parsed.SuggestedTitle;
            DateBox.SelectedDate = parsed.HasDate ? parsed.Date : null;
            TimeBox.Text = parsed.HasTime ? parsed.Time!.Value.ToString(@"hh\:mm") : "";
            AddInitialTarget(initialTarget);
        }
    }

    private void LoadAlarm(Alarm alarm)
    {
        OriginalTextBox.Text = alarm.OriginalText;
        TitleBox.Text = alarm.Title;
        DateBox.SelectedDate = alarm.AlarmDateTime.Date;
        TimeBox.Text = alarm.AlarmDateTime.ToString("HH:mm");
        DescriptionBox.Text = alarm.Description;
        foreach (var attachment in alarm.Attachments)
            _attachments.Add(new AlarmAttachment { Kind = attachment.Kind, DisplayName = attachment.DisplayName, Target = attachment.Target });
        R5.IsChecked = alarm.ReminderMinutes.Contains(5); R10.IsChecked = alarm.ReminderMinutes.Contains(10);
        R30.IsChecked = alarm.ReminderMinutes.Contains(30); R60.IsChecked = alarm.ReminderMinutes.Contains(60); R1440.IsChecked = alarm.ReminderMinutes.Contains(1440);
        SelectByTag(RepeatInterval, alarm.RepeatMinutes);
        SelectByTag(RepeatCount, alarm.RepeatCount == 0 ? 1 : alarm.RepeatCount);
    }

    private void AddInitialTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        if (FileOpenService.TryInferAttachment(target, out var attachment, out _) && FileOpenService.IsValidForSaving(attachment, out _)) _attachments.Add(attachment);
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
            reminders.Add(custom);
        }
        var expiredReminder = reminders.Distinct().OrderDescending().FirstOrDefault(x => when.AddMinutes(-x) <= DateTime.Now);
        if (expiredReminder > 0) { Warn($"{ReminderLabel(expiredReminder)} 사전 알림 시각이 이미 지났습니다. 해당 항목을 해제해 주세요.", R5); return; }

        if (_attachments.Count > FileOpenService.MaximumAttachments) { Warn($"연결 대상은 최대 {FileOpenService.MaximumAttachments}개까지 등록할 수 있습니다.", AttachmentTargetBox); return; }
        var attachmentKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attachment in _attachments)
        {
            if (!FileOpenService.IsValidForSaving(attachment, out var error)) { Warn(error, AttachmentTargetBox); return; }
            if (!attachmentKeys.Add(FileOpenService.NormalizedKey(attachment))) { Warn("같은 연결 대상이 중복되어 있습니다.", AttachmentTargetBox); return; }
        }

        _alarm.Title = TitleBox.Text.Trim(); _alarm.OriginalText = OriginalTextBox.Text.Trim(); _alarm.AlarmDateTime = when;
        _alarm.Description = DescriptionBox.Text.Trim(); _alarm.ReminderMinutes = reminders.Distinct().ToList();
        _alarm.RepeatMinutes = TagValue(RepeatInterval); _alarm.RepeatCount = _alarm.RepeatMinutes == 0 ? 0 : TagValue(RepeatCount);
        _alarm.Attachments = _attachments.Select(x => new AlarmAttachment { Kind = x.Kind, DisplayName = x.DisplayName.Trim(), Target = x.Target.Trim() }).ToList();
        _alarm.Status = AlarmStatus.Scheduled; _alarm.Completed = false; _alarm.CompletedAt = null;
        try
        {
            _repository.Save(_alarm);
            _scheduler.Reschedule();
            DialogResult = true;
        }
        catch (Exception ex) { MessageBox.Show(this, $"알림을 저장하지 못했습니다.\n{ex.Message}", "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error); }
    }

    private void AddAttachment_Click(object sender, RoutedEventArgs e)
    {
        if (_attachments.Count >= FileOpenService.MaximumAttachments) { Warn($"연결 대상은 최대 {FileOpenService.MaximumAttachments}개까지 등록할 수 있습니다.", AttachmentTargetBox); return; }
        var kind = (AttachmentKind)int.Parse(((ComboBoxItem)AttachmentKindBox.SelectedItem).Tag.ToString()!);
        var attachment = new AlarmAttachment { Kind = kind, Target = AttachmentTargetBox.Text.Trim(), DisplayName = AttachmentNameBox.Text.Trim() };
        if (!FileOpenService.IsValidForSaving(attachment, out var error)) { Warn(error, AttachmentTargetBox); return; }
        if (_attachments.Any(x => string.Equals(FileOpenService.NormalizedKey(x), FileOpenService.NormalizedKey(attachment), StringComparison.OrdinalIgnoreCase))) { Warn("같은 연결 대상이 이미 추가되어 있습니다.", AttachmentTargetBox); return; }
        if (string.IsNullOrWhiteSpace(attachment.DisplayName)) attachment.DisplayName = FileOpenService.GetDefaultDisplayName(attachment);
        _attachments.Add(attachment);
        AttachmentNameBox.Clear(); AttachmentTargetBox.Clear(); AttachmentTargetBox.Focus();
    }

    private void RemoveAttachment_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is AlarmAttachment attachment) _attachments.Remove(attachment);
    }

    private void ChooseFile_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Title = "알림에 연결할 파일 선택", CheckFileExists = true, Filter = "모든 파일|*.*" };
        if (dialog.ShowDialog(this) != true) return;
        SetKind(AttachmentKind.File); AttachmentTargetBox.Text = dialog.FileName; AttachmentNameBox.Text = ""; AddAttachment_Click(sender, e);
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog { Description = "알림에 연결할 폴더 선택", UseDescriptionForTitle = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        SetKind(AttachmentKind.Folder); AttachmentTargetBox.Text = dialog.SelectedPath; AttachmentNameBox.Text = ""; AddAttachment_Click(sender, e);
    }

    private void SetKind(AttachmentKind kind)
    {
        foreach (ComboBoxItem item in AttachmentKindBox.Items)
            if (item.Tag?.ToString() == ((int)kind).ToString()) { AttachmentKindBox.SelectedItem = item; return; }
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
    private static string ReminderLabel(int minutes) => minutes >= 1440 ? $"{minutes / 1440}일 전" : minutes >= 60 ? $"{minutes / 60}시간 전" : $"{minutes}분 전";
}
