using System.Windows;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Services.FileOpenService;

namespace InternalAlarmApp.Views;

public partial class DailyWrapUpWindow : Window
{
    private readonly AlarmRepository _repository;
    private readonly AlarmScheduler _scheduler;
    private readonly FileOpenService _fileOpen;
    public event EventHandler? ShowListRequested;
    public event Action<IReadOnlyList<long>>? AlarmsPostponed;

    public DailyWrapUpWindow(AlarmRepository repository, AlarmScheduler scheduler, FileOpenService fileOpen)
    {
        InitializeComponent();
        _repository = repository; _scheduler = scheduler; _fileOpen = fileOpen;
        PostponeDateBox.DisplayDateStart = DateTime.Today;
        PostponeDateBox.SelectedDate = DateTime.Today.AddDays(1);
        Reload();
    }

    private void Reload()
    {
        var summary = _repository.GetDailySummary(DateTime.Today);
        CompletedTodayText.Text = $"오늘 완료 처리 {summary.CompletedTodayCount}건";
        CompletionRateText.Text = $"오늘 일정 완료율 {summary.CompletionRate}%";
        IncompleteText.Text = $"미완료 {summary.IncompleteAlarms.Count}건";
        IncompleteCards.ItemsSource = summary.IncompleteAlarms;
        EmptyText.Visibility = summary.IncompleteAlarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsScroll.Visibility = summary.IncompleteAlarms.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void PostponeTomorrow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is Alarm alarm) Postpone([alarm.Id], DateTime.Today.AddDays(1));
    }

    private void PostponeSelectedDate_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Alarm alarm) return;
        if (!PostponeDateBox.SelectedDate.HasValue)
        {
            MessageBox.Show(this, "연기할 날짜를 선택해 주세요.", "날짜 선택", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Postpone([alarm.Id], PostponeDateBox.SelectedDate.Value);
    }

    private void PostponeAllTomorrow_Click(object sender, RoutedEventArgs e) =>
        Postpone(CurrentIncompleteIds(), DateTime.Today.AddDays(1));

    private void PostponeAllSelectedDate_Click(object sender, RoutedEventArgs e)
    {
        if (!PostponeDateBox.SelectedDate.HasValue)
        {
            MessageBox.Show(this, "연기할 날짜를 선택해 주세요.", "날짜 선택", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Postpone(CurrentIncompleteIds(), PostponeDateBox.SelectedDate.Value);
    }

    private void Postpone(IEnumerable<long> ids, DateTime targetDate)
    {
        var candidates = ids.Distinct().ToList();
        if (candidates.Count == 0) return;
        var result = _repository.PostponeToDate(candidates, targetDate);
        _scheduler.Reschedule();
        if (result.PostponedIds.Count > 0) AlarmsPostponed?.Invoke(result.PostponedIds);
        Reload();
        if (result.SkippedIds.Count > 0)
            MessageBox.Show(this, "현재보다 이전인 시각이거나 이미 완료된 일정은 연기하지 않았습니다.", "일정 연기", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private IReadOnlyList<long> CurrentIncompleteIds() => _repository.GetDailySummary(DateTime.Today).IncompleteAlarms.Select(x => x.Id).ToList();

    private async void OpenAll_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Alarm alarm) return;
        if (alarm.Attachments.Count == 0)
        {
            MessageBox.Show(this, "연결된 파일, 폴더 또는 웹 주소가 없습니다.", "관련 자료", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var result = await _fileOpen.OpenAllAsync(alarm.Attachments);
        if (result.Failures.Count > 0 || result.LimitReached)
        {
            var messages = result.Failures.ToList();
            if (result.LimitReached) messages.Add($"한 번에 열 수 있는 관련 자료는 {FileOpenService.MaximumAttachments}개입니다.");
            MessageBox.Show(this, string.Join(Environment.NewLine, messages), "관련 자료 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ShowList_Click(object sender, RoutedEventArgs e) => ShowListRequested?.Invoke(this, EventArgs.Empty);
}
