using System.Diagnostics;
using System.Windows.Automation;
using static TaskPack.NativeMethods;

namespace TaskPack;

// 작업표시줄의 TaskPack 아이콘 위에 마우스가 머무는지 지켜보고, 머물면 알린다 (Windows 11 기본 작업표시줄 기준).
// 마우스를 후킹하지 않고 0.05초마다 커서 위치만 읽는다. 커서가 작업표시줄 위에서 멈췄을 때만 UI 자동화로 아이콘을 확인한다.
internal sealed class HoverMonitor : IDisposable
{
    // 작업표시줄 버튼의 자동화 식별자. 바로가기와 프로그램에 붙인 앱 식별자(Drawer.AppId)와 같다
    private static readonly string ButtonId = "Appid: " + Drawer.AppId;

    private const int PollMs = 50;
    private const int MoveTolerance = 6;    // 이 거리(픽셀) 안의 움직임은 머무는 것으로 본다
    private const int ConfirmDelayMs = 60;  // 아이콘이 움직이는 중의 오판을 막으려고 두 번 읽는 간격

    private readonly Func<bool> _canOpen;
    private readonly Func<int> _dwellMs;
    private readonly Action<RECT> _onHover;
    private readonly CancellationTokenSource _stop = new();
    private readonly Thread _thread;

    // canOpen: 지금 가방을 열 수 있는 상태인지 (이미 열려 있으면 false). dwellMs: 아이콘 위에 이만큼(밀리초) 머물러야 열린다.
    // onHover: 아이콘 영역(물리 픽셀)과 함께 부른다 (다른 스레드에서)
    public HoverMonitor(Func<bool> canOpen, Func<int> dwellMs, Action<RECT> onHover)
    {
        _canOpen = canOpen;
        _dwellMs = dwellMs;
        _onHover = onHover;
        _thread = new Thread(Run) { IsBackground = true, Name = "TaskPack.HoverMonitor" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    public void Dispose() => _stop.Cancel();

    // 이 PC의 작업표시줄에서 TaskPack 아이콘을 찾을 수 있는지 (마우스오버가 동작할 수 있는지)
    public static bool IsSupported() => Taskbars().Any(tb => FindButton(tb) is not null);

    private void Run()
    {
        POINT anchor = default;
        var anchored = false;
        var handled = false;
        var since = Stopwatch.StartNew();
        while (!_stop.Token.WaitHandle.WaitOne(PollMs))
        {
            try
            {
                if (!GetCursorPos(out var p) || TaskbarAt(p) == IntPtr.Zero)
                {
                    anchored = false;
                    continue;
                }
                if (!anchored || Math.Abs(p.X - anchor.X) > MoveTolerance || Math.Abs(p.Y - anchor.Y) > MoveTolerance)
                {
                    anchor = p;
                    anchored = true;
                    handled = false;
                    since.Restart();
                    continue;
                }
                if (handled || since.ElapsedMilliseconds < _dwellMs())
                    continue;
                handled = true; // 같은 자리에서는 한 번만 확인한다 (움직이면 다시 확인)
                if (_canOpen() && ConfirmedButtonRect(p) is { } rect)
                    _onHover(rect);
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or System.Runtime.InteropServices.COMException or InvalidOperationException)
            {
                // 탐색기가 다시 시작되는 중 등: 이번 확인은 건너뛰고 계속 지켜본다
            }
        }
    }

    // 커서가 TaskPack 아이콘 위에 있는지 두 번 읽어 같은 결과일 때만 그 영역을 돌려준다
    private static RECT? ConfirmedButtonRect(POINT p)
    {
        var first = ButtonRectAt(p);
        if (first is null)
            return null;
        Thread.Sleep(ConfirmDelayMs);
        if (!GetCursorPos(out var q))
            return null;
        var second = ButtonRectAt(q);
        return second is { } r && r.Left == first.Value.Left && r.Right == first.Value.Right ? r : null;
    }

    private static RECT? ButtonRectAt(POINT p)
    {
        var taskbar = TaskbarAt(p);
        if (taskbar == IntPtr.Zero || FindButton(taskbar) is not { } rect)
            return null;
        return p.X >= rect.Left && p.X < rect.Right && p.Y >= rect.Top && p.Y < rect.Bottom ? rect : null;
    }

    // 그 작업표시줄의 TaskPack 아이콘 영역. 아이콘은 다른 앱이 열고 닫힐 때마다 움직이므로 매번 새로 읽는다
    private static RECT? FindButton(IntPtr taskbar)
    {
        var button = AutomationElement.FromHandle(taskbar).FindFirst(TreeScope.Descendants,
            new PropertyCondition(AutomationElement.AutomationIdProperty, ButtonId));
        if (button is null)
            return null;
        var r = button.Current.BoundingRectangle;
        if (r.IsEmpty)
            return null;
        return new RECT { Left = (int)r.Left, Top = (int)r.Top, Right = (int)Math.Ceiling(r.Right), Bottom = (int)Math.Ceiling(r.Bottom) };
    }

    // 주 작업표시줄과 보조 모니터의 작업표시줄
    private static List<IntPtr> Taskbars()
    {
        var list = new List<IntPtr>();
        var main = FindWindow("Shell_TrayWnd", null);
        if (main != IntPtr.Zero)
            list.Add(main);
        var secondary = IntPtr.Zero;
        while ((secondary = FindWindowEx(IntPtr.Zero, secondary, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
            list.Add(secondary);
        return list;
    }

    private static IntPtr TaskbarAt(POINT p)
    {
        foreach (var tb in Taskbars())
            if (GetWindowRect(tb, out var r) && p.X >= r.Left && p.X < r.Right && p.Y >= r.Top && p.Y < r.Bottom)
                return tb;
        return IntPtr.Zero;
    }
}
