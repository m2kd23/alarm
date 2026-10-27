using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Services.FileOpenService;

namespace InternalAlarmApp.Views;

public partial class NotificationPopup : Window
{
    private readonly Alarm _alarm;
    private readonly AlarmRepository _repository;
    private readonly AlarmScheduler _scheduler;
    private readonly FileOpenService _fileOpen;

    public NotificationPopup(Alarm alarm, AlarmRepository repository, AlarmScheduler scheduler, FileOpenService fileOpen, bool keepVisible)
    {
        InitializeComponent();
        _alarm = alarm; _repository = repository; _scheduler = scheduler; _fileOpen = fileOpen;
        TitleText.Text = alarm.Title;
        TimeText.Text = $"{alarm.AlarmDateTime:yyyy-MM-dd HH:mm} 일정입니다.";
        DescriptionText.Text = alarm.Description;
        DescriptionText.Visibility = string.IsNullOrWhiteSpace(alarm.Description) ? Visibility.Collapsed : Visibility.Visible;
        OpenButton.Visibility = alarm.Attachments.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        if (alarm.Attachments.Count > 0) OpenButton.Content = $"관련 자료 {alarm.Attachments.Count}개 열기";
        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - ActualWidth - 14;
            Top = SystemParameters.WorkArea.Bottom - ActualHeight - 14;
            Activate();
        };
        if (!keepVisible)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            timer.Tick += (_, _) => { timer.Stop(); Close(); };
            timer.Start();
        }
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        _repository.Complete(_alarm.Id);
        _scheduler.Reschedule();
        Close();
    }

    private void Snooze_Click(object sender, RoutedEventArgs e)
    {
        var minutes = int.Parse(((ComboBoxItem)SnoozeMinutes.SelectedItem).Tag.ToString()!);
        _scheduler.Snooze(_alarm.Id, minutes);
        Close();
    }

    private async void Open_Click(object sender, RoutedEventArgs e)
    {
        var result = await _fileOpen.OpenAllAsync(_alarm.Attachments);
        if (result.Failures.Count > 0 || result.LimitReached)
        {
            var messages = result.Failures.ToList();
            if (result.LimitReached) messages.Add($"한 번에 열 수 있는 관련 자료는 {FileOpenService.MaximumAttachments}개입니다.");
            MessageBox.Show(this, string.Join(Environment.NewLine, messages), "열기 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
