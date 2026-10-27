using System.Windows;
using InternalAlarmApp.Services.CsvService;
using InternalAlarmApp.Services.DataService;
using Microsoft.Win32;

namespace InternalAlarmApp.Views;

public partial class CsvExportWindow : Window
{
    private readonly AlarmRepository _repository;
    private readonly AlarmCsvService _service;

    public CsvExportWindow(AlarmRepository repository, AlarmCsvService service)
    {
        InitializeComponent();
        FromDateBox.Loaded += (_, _) => DatePickerAlignment.Center(FromDateBox);
        ToDateBox.Loaded += (_, _) => DatePickerAlignment.Center(ToDateBox);
        _repository = repository; _service = service;
        var monday = DateTime.Today.AddDays(-((int)DateTime.Today.DayOfWeek + 6) % 7);
        FromDateBox.SelectedDate = monday;
        ToDateBox.SelectedDate = monday.AddDays(6);
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!FromDateBox.SelectedDate.HasValue || !ToDateBox.SelectedDate.HasValue || ToDateBox.SelectedDate < FromDateBox.SelectedDate)
        {
            MessageBox.Show(this, "올바른 시작·종료 날짜를 선택해 주세요.", "CSV 내보내기", MessageBoxButton.OK, MessageBoxImage.Information); return;
        }
        var dialog = new SaveFileDialog { Title = "선택한 기간 업무 CSV 저장", Filter = "CSV UTF-8 파일|*.csv", FileName = $"업무목록_{FromDateBox.SelectedDate:yyyyMMdd}_{ToDateBox.SelectedDate:yyyyMMdd}.csv", AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            _service.Export(dialog.FileName, _repository.GetAll(), FromDateBox.SelectedDate.Value, ToDateBox.SelectedDate.Value);
            MessageBox.Show(this, "CSV 파일을 저장했습니다.", "CSV 내보내기", MessageBoxButton.OK, MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"CSV 파일을 저장하지 못했습니다.\n{ex.Message}", "CSV 내보내기", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
