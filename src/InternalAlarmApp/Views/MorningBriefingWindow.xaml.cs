using System.Windows;
using System.Windows.Controls;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.FileOpenService;

namespace InternalAlarmApp.Views;

public partial class MorningBriefingWindow : Window
{
    private readonly FileOpenService _fileOpen;
    public event EventHandler? ShowListRequested;

    public MorningBriefingWindow(IReadOnlyList<Alarm> alarms, FileOpenService fileOpen)
    {
        InitializeComponent();
        _fileOpen = fileOpen;
        AlarmCards.ItemsSource = alarms;
        SummaryText.Text = $"{DateTime.Today:yyyy년 M월 d일} · 오늘 일정 {alarms.Count}건";
        EmptyText.Visibility = alarms.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CardsScroll.Visibility = alarms.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

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
            MessageBox.Show(this, BuildOpenResultMessage(result), "관련 자료 열기", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void ShowList_Click(object sender, RoutedEventArgs e) => ShowListRequested?.Invoke(this, EventArgs.Empty);

    private static string BuildOpenResultMessage(OpenTargetsResult result)
    {
        var lines = new List<string>();
        if (result.Opened.Count > 0) lines.Add($"관련 자료 {result.Opened.Count}개를 열었습니다.");
        if (result.Failures.Count > 0) lines.AddRange(result.Failures);
        if (result.LimitReached) lines.Add($"한 번에 열 수 있는 관련 자료는 {FileOpenService.MaximumAttachments}개입니다.");
        return string.Join(Environment.NewLine, lines);
    }
}
