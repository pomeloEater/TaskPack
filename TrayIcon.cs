using System.IO;
namespace TaskPack;

// 상주 중에만 시계 옆(알림 영역)에 보이는 아이콘. 켜져 있다는 사실을 알리고, 끄는 길을 준다
internal sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayIcon(Action openBag, Action openSettings, Action turnOffHover, Action quit)
    {
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("가방 열기", null, (_, _) => openBag());
        menu.Items.Add("설정", null, (_, _) => openSettings());
        menu.Items.Add("마우스오버 끄기", null, (_, _) => turnOffHover());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("TaskPack 끝내기", null, (_, _) => quit());

        _icon = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "TaskPack: 작업표시줄 아이콘에 마우스를 올리면 열려요",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == System.Windows.Forms.MouseButtons.Left)
                openBag();
        };
    }

    private static System.Drawing.Icon LoadIcon()
    {
        try
        {
            if (Environment.ProcessPath is { } exe && System.Drawing.Icon.ExtractAssociatedIcon(exe) is { } icon)
                return icon;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            // 프로그램 아이콘을 읽지 못하면 기본 아이콘을 쓴다
        }
        return System.Drawing.SystemIcons.Application;
    }

    // 끝낼 때 지우지 않으면 아이콘이 알림 영역에 남는다
    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
