using System.Windows;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.CsvService;
using InternalAlarmApp.Services.DataService;

namespace InternalAlarmApp.Views;

public partial class CsvImportPreviewWindow : Window
{
    private readonly CsvImportPreview _preview;
    private readonly AlarmRepository _repository;
    private readonly AlarmScheduler _scheduler;

    public CsvImportPreviewWindow(CsvImportPreview preview, AlarmRepository repository, AlarmScheduler scheduler)
    {
        InitializeComponent();
        _preview = preview; _repository = repository; _scheduler = scheduler;
        RowsGrid.ItemsSource = preview.Rows;
        SummaryText.Text = string.IsNullOrWhiteSpace(preview.FileErrorMessage)
            ? $"정상 {preview.ValidCount}건 · 중복으로 건너뜀 {preview.DuplicateCount}건 · 오류 {preview.ErrorCount}건"
            : preview.FileErrorMessage;
        ImportButton.IsEnabled = preview.ValidCount > 0 && string.IsNullOrWhiteSpace(preview.FileErrorMessage);
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var alarms = _preview.ValidAlarms;
        if (alarms.Count == 0) return;
        if (MessageBox.Show(this, $"{alarms.Count}건을 새 일정으로 등록할까요?", "CSV 가져오기", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        try
        {
            _repository.Import(alarms);
            _scheduler.Reschedule();
            MessageBox.Show(this, $"{alarms.Count}건을 등록했습니다.", "CSV 가져오기", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"CSV 가져오기에 실패했습니다.\n{ex.Message}", "CSV 가져오기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
