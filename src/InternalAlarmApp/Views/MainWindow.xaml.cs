using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using InternalAlarmApp.DateTimeParser;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Services.FileOpenService;
using InternalAlarmApp.ViewModels;

namespace InternalAlarmApp.Views;

public partial class MainWindow : Window
{
    private readonly AlarmRepository _repository;
    private readonly AlarmScheduler _scheduler;
    private readonly KoreanDateTimeParser _parser;
    private readonly FileOpenService _fileOpen;
    private readonly MainViewModel _viewModel;
    private bool _forceClose;

    public MainWindow(AlarmRepository repository, AlarmScheduler scheduler, KoreanDateTimeParser parser, FileOpenService fileOpen)
    {
        InitializeComponent();
        _repository = repository; _scheduler = scheduler; _parser = parser; _fileOpen = fileOpen;
        _viewModel = new MainViewModel(repository);
        DataContext = _viewModel;
    }

    public void ShowAndActivate(string? filter = null)
    {
        if (filter != null) _viewModel.SelectFilter(filter);
        _viewModel.Reload();
        Show(); WindowState = WindowState.Normal; Activate();
    }

    public void OpenEditor(string originalText = "", Alarm? alarm = null)
    {
        var editor = new AlarmEditorWindow(_repository, _scheduler, _parser, originalText, alarm) { Owner = IsVisible ? this : null };
        if (editor.ShowDialog() == true) _viewModel.Reload();
    }

    public void ForceClose() { _forceClose = true; Close(); }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_forceClose) { e.Cancel = true; Hide(); }
        base.OnClosing(e);
    }

    private Alarm? SelectedOrWarn()
    {
        if (_viewModel.SelectedAlarm != null) return _viewModel.SelectedAlarm;
        MessageBox.Show(this, "먼저 알림을 선택해 주세요.", "알림 선택", MessageBoxButton.OK, MessageBoxImage.Information);
        return null;
    }
    private void New_Click(object sender, RoutedEventArgs e) => OpenEditor();
    private void Edit_Click(object sender, RoutedEventArgs e) { var alarm = SelectedOrWarn(); if (alarm != null) OpenEditor(alarm: alarm); }
    private void Grid_DoubleClick(object sender, MouseButtonEventArgs e) { if (_viewModel.SelectedAlarm != null) OpenEditor(alarm: _viewModel.SelectedAlarm); }
    private void Refresh_Click(object sender, RoutedEventArgs e) => _viewModel.Reload();
    private void Settings_Click(object sender, RoutedEventArgs e) => ((App)Application.Current).ShowSettings(this);
    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var alarm = SelectedOrWarn(); if (alarm == null) return;
        if (!_fileOpen.Open(alarm.FilePath, out var error)) MessageBox.Show(this, error, "열기", MessageBoxButton.OK, MessageBoxImage.Information);
    }
    private void Complete_Click(object sender, RoutedEventArgs e)
    {
        var alarm = SelectedOrWarn(); if (alarm == null) return;
        _repository.Complete(alarm.Id); _scheduler.Reschedule(); _viewModel.Reload();
    }
    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        var alarm = SelectedOrWarn(); if (alarm == null) return;
        if (MessageBox.Show(this, $"'{alarm.Title}' 알림을 삭제할까요?", "알림 삭제", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        _repository.Delete(alarm.Id); _scheduler.Reschedule(); _viewModel.Reload();
    }
}
