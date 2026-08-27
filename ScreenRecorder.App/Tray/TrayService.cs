namespace ScreenRecorder.App.Tray;

/// <summary>
/// 系统托盘外壳（§41）：双击/菜单恢复主窗口、退出。
/// 骨架阶段用 WinForms NotifyIcon；自定义托盘菜单视觉随 #13 主题基座替换。
/// </summary>
public sealed class TrayService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _notifyIcon;

    public TrayService(Action showMainWindow, Action exitApp)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("打开 Inkframe", null, (_, _) => showMainWindow());
        menu.Items.Add("退出", null, (_, _) => exitApp());

        _notifyIcon = new System.Windows.Forms.NotifyIcon
        {
            Text = "Inkframe",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _notifyIcon.DoubleClick += (_, _) => showMainWindow();
    }

    public void Dispose() => _notifyIcon.Dispose();
}
