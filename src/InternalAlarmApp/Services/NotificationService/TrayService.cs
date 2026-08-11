using Drawing = System.Drawing;
using Forms = System.Windows.Forms;
using InternalAlarmApp.Models;

namespace InternalAlarmApp.Services.NotificationService;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon _icon;
    public event EventHandler? NewAlarmRequested;
    public event EventHandler? ListRequested;
    public event EventHandler? TodayRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? ExitRequested;

    public TrayService()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("새 알림 등록", null, (_, _) => NewAlarmRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("알림 목록", null, (_, _) => ListRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("오늘 일정", null, (_, _) => TodayRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("설정", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("종료", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        _icon = new Forms.NotifyIcon
        {
            Text = "내 업무 알림",
            Icon = Drawing.SystemIcons.Information,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => ListRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ShowNativeNotification(Alarm alarm, Reminder reminder)
    {
        var timing = reminder.Kind == ReminderKind.PreAlert ? $"{reminder.ReminderMinutes}분 전 알림" : $"{alarm.AlarmDateTime:MM/dd HH:mm}";
        _icon.BalloonTipTitle = "내 업무 알림";
        _icon.BalloonTipText = $"{alarm.Title}\n{timing}";
        _icon.BalloonTipIcon = Forms.ToolTipIcon.Info;
        _icon.ShowBalloonTip(8000);
    }

    public void ShowStartupMessage() => _icon.ShowBalloonTip(3000, "내 업무 알림", "프로그램이 시스템 트레이에서 실행 중입니다.", Forms.ToolTipIcon.Info);

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
