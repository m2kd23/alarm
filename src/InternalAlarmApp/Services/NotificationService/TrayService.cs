using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

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

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
