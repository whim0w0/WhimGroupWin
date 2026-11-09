using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace WhimGroupWin;

internal sealed class TrayApplication : IDisposable
{
    private readonly Application application;
    private readonly Forms.NotifyIcon trayIcon;
    private readonly Forms.ContextMenuStrip menu;
    private readonly Icon icon;
    private FrmSetting? settingsWindow;
    private readonly GroupController? groupController;

    public TrayApplication(Application application, GroupController? groupController = null)
    {
        this.application = application;
        this.groupController = groupController;
        using var iconStream = typeof(TrayApplication).Assembly.GetManifestResourceStream("WhimGroupWin.TrayIcon.ico")
            ?? throw new InvalidOperationException("找不到托盘图标资源。");
        using var resourceIcon = new Icon(iconStream);
        icon = (Icon)resourceIcon.Clone();
        menu = new Forms.ContextMenuStrip();
        menu.Items.Add("设置", null, (_, _) => Dispatch(ShowSettings));
        menu.Items.Add("关闭", null, (_, _) => Dispatch(application.Shutdown));
        trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "WhimGroupWin",
            ContextMenuStrip = menu,
            Visible = true
        };
        trayIcon.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left)
                Dispatch(ShowSettings);
        };
    }

    private void Dispatch(Action action) => application.Dispatcher.InvokeAsync(action);

    private void ShowSettings()
    {
        if (settingsWindow is null)
        {
            settingsWindow = new FrmSetting(groupController);
            settingsWindow.Closed += (_, _) => settingsWindow = null;
            settingsWindow.Show();
        }
        if (settingsWindow.WindowState == WindowState.Minimized)
            settingsWindow.WindowState = WindowState.Normal;
        settingsWindow.Activate();
    }

    public void Dispose()
    {
        trayIcon.Visible = false;
        trayIcon.Dispose();
        menu.Dispose();
        icon.Dispose();
    }
}