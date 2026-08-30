using System.Threading;
using System.Windows;
using InternalAlarmApp.DateTimeParser;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services;
using InternalAlarmApp.Services.AlarmService;
using InternalAlarmApp.Services.ClipboardService;
using InternalAlarmApp.Services.DataService;
using InternalAlarmApp.Services.FileOpenService;
using InternalAlarmApp.Services.HotKeyService;
using InternalAlarmApp.Services.NotificationService;
using InternalAlarmApp.Utils;
using InternalAlarmApp.Views;

namespace InternalAlarmApp;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private AlarmRepository? _repository;
    private AlarmScheduler? _scheduler;
    private DailyRoutineService? _dailyRoutine;
    private GlobalKeySequenceService? _hotKey;
    private TrayService? _tray;
    private MainWindow? _mainWindow;
    private AppSettings _settings = new();
    private readonly KoreanDateTimeParser _parser = new();
    private readonly SelectionTextService _selection = new();
    private readonly FileOpenService _fileOpen = new();
    private readonly ActiveContextService _activeContext = new();
    private readonly Dictionary<long, NotificationPopup> _openPopups = [];
    private MorningBriefingWindow? _morningBriefingWindow;
    private DailyWrapUpWindow? _dailyWrapUpWindow;
    private bool _capturing;
    private bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _singleInstance = new Mutex(true, @"Local\InternalAlarmApp.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("내 업무 알림이 이미 실행 중입니다. 시스템 트레이를 확인해 주세요.", "내 업무 알림", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown(); return;
        }

        try
        {
            AppPaths.EnsureCreated();
            _settings = AppSettings.Load(AppPaths.Settings);
            _settings.Save(AppPaths.Settings);
            _repository = new AlarmRepository(AppPaths.Database);
            _repository.Initialize();
            _scheduler = new AlarmScheduler(_repository);
            _scheduler.AlarmDue += Scheduler_AlarmDue;
            _dailyRoutine = new DailyRoutineService(_settings);
            _dailyRoutine.MorningBriefingDue += (_, _) => Dispatcher.BeginInvoke(ShowMorningBriefing);
            _dailyRoutine.DailyWrapUpDue += (_, _) => Dispatcher.BeginInvoke(ShowDailyWrapUp);
            _tray = new TrayService();
            _mainWindow = new MainWindow(_repository, _scheduler, _parser, _fileOpen);
            MainWindow = _mainWindow;
            WireTray();
            _hotKey = new GlobalKeySequenceService(_settings.HotKeyVirtualKey, _settings.DoublePressMilliseconds, _settings.HotKeyModifiers, _settings.HotKeyPressCount);
            _hotKey.Triggered += (_, _) => Dispatcher.BeginInvoke(CaptureSelectionAndOpenEditor);
            _hotKey.Start();
            _scheduler.Start();
            _dailyRoutine.Start();
            Logger.Info("프로그램 시작");
            _mainWindow.ShowAndActivate();
        }
        catch (Exception ex)
        {
            Logger.Error("시작", ex);
            MessageBox.Show($"프로그램을 시작하지 못했습니다.\n{ex.Message}", "시작 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private async void CaptureSelectionAndOpenEditor()
    {
        if (_capturing || _mainWindow is null) return;
        _capturing = true;
        try
        {
            var source = _activeContext.CaptureSource();
            await Task.Delay(180);
            var text = await _selection.GetSelectedTextAsync();
            var activeTarget = _activeContext.ResolveTarget(source);
            _mainWindow.OpenEditor(text, activeTarget.Target, activeTarget.SuggestedTitle);
        }
        catch (Exception ex)
        {
            Logger.Error("선택 텍스트 가져오기", ex);
            MessageBox.Show("선택한 문장을 가져오지 못했습니다. 등록 창에 직접 입력해 주세요.", "텍스트 가져오기", MessageBoxButton.OK, MessageBoxImage.Information);
            _mainWindow.OpenEditor();
        }
        finally { _capturing = false; }
    }

    private void Scheduler_AlarmDue(object? sender, AlarmDueEventArgs e) => Dispatcher.BeginInvoke(() =>
    {
        if (_repository is null || _scheduler is null || _tray is null) return;
        if (_openPopups.Remove(e.Alarm.Id, out var previous)) previous.Close();
        var popup = new NotificationPopup(e.Alarm, _repository, _scheduler, _fileOpen, _settings.KeepPopupVisible);
        _openPopups[e.Alarm.Id] = popup;
        popup.Closed += (_, _) =>
        {
            if (_openPopups.TryGetValue(e.Alarm.Id, out var current) && ReferenceEquals(current, popup)) _openPopups.Remove(e.Alarm.Id);
        };
        popup.Show();
    });

    private void WireTray()
    {
        if (_tray is null || _mainWindow is null) return;
        _tray.NewAlarmRequested += (_, _) => Dispatcher.Invoke(() => _mainWindow.OpenEditor());
        _tray.ListRequested += (_, _) => Dispatcher.Invoke(() => _mainWindow.ShowAndActivate());
        _tray.TodayRequested += (_, _) => Dispatcher.Invoke(() => _mainWindow.ShowAndActivate("오늘 일정"));
        _tray.MorningBriefingRequested += (_, _) => Dispatcher.Invoke(ShowMorningBriefing);
        _tray.DailyWrapUpRequested += (_, _) => Dispatcher.Invoke(ShowDailyWrapUp);
        _tray.SettingsRequested += (_, _) => Dispatcher.Invoke(() => ShowSettings(_mainWindow));
        _tray.ExitRequested += (_, _) => Dispatcher.Invoke(ExitApplication);
    }

    public void ShowSettings(Window owner)
    {
        var dialog = new SettingsWindow(_settings) { Owner = owner };
        if (dialog.ShowDialog() != true) return;
        try
        {
            StartupService.SetEnabled(_settings.RunAtStartup);
            _settings.Save(AppPaths.Settings);
            _hotKey?.Update(_settings.HotKeyVirtualKey, _settings.DoublePressMilliseconds, _settings.HotKeyModifiers, _settings.HotKeyPressCount);
            _dailyRoutine?.Update(_settings);
        }
        catch (Exception ex)
        {
            Logger.Error("설정 저장", ex);
            MessageBox.Show(owner, $"일부 설정을 저장하지 못했습니다.\n{ex.Message}", "설정 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        Logger.Info("프로그램 종료");
        _scheduler?.Dispose(); _dailyRoutine?.Dispose(); _hotKey?.Dispose(); _tray?.Dispose();
        _mainWindow?.ForceClose();
        Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _scheduler?.Dispose(); _dailyRoutine?.Dispose(); _hotKey?.Dispose(); _tray?.Dispose(); _singleInstance?.Dispose();
        base.OnExit(e);
    }

    private void ShowMorningBriefing()
    {
        if (_repository is null) return;
        if (_morningBriefingWindow is { IsVisible: true }) { _morningBriefingWindow.Activate(); return; }
        var window = new MorningBriefingWindow(_repository.GetForDate(DateTime.Today), _fileOpen);
        _morningBriefingWindow = window;
        window.ShowListRequested += (_, _) => _mainWindow?.ShowAndActivate("오늘 일정");
        window.Closed += (_, _) => { if (ReferenceEquals(_morningBriefingWindow, window)) _morningBriefingWindow = null; };
        window.Show();
        window.Activate();
    }

    private void ShowDailyWrapUp()
    {
        if (_repository is null || _scheduler is null) return;
        if (_dailyWrapUpWindow is { IsVisible: true }) { _dailyWrapUpWindow.Activate(); return; }
        var window = new DailyWrapUpWindow(_repository, _scheduler, _fileOpen);
        _dailyWrapUpWindow = window;
        window.ShowListRequested += (_, _) => _mainWindow?.ShowAndActivate("오늘 일정");
        window.AlarmsPostponed += CloseAlarmPopups;
        window.Closed += (_, _) => { if (ReferenceEquals(_dailyWrapUpWindow, window)) _dailyWrapUpWindow = null; };
        window.Show();
        window.Activate();
    }

    private void CloseAlarmPopups(IReadOnlyList<long> alarmIds)
    {
        foreach (var id in alarmIds)
            if (_openPopups.Remove(id, out var popup)) popup.Close();
        _mainWindow?.ShowAndActivate();
    }
}
