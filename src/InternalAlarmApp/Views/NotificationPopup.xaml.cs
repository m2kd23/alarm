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
        OpenButton.Visibility = string.IsNullOrWhiteSpace(alarm.FilePath) ? Visibility.Collapsed : Visibility.Visible;
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

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!_fileOpen.Open(_alarm.FilePath, out var error)) MessageBox.Show(this, error, "열기 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
