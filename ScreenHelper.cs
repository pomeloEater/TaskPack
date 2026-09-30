using System.Windows;
using System.Windows.Interop;
using static TaskPack.NativeMethods;

namespace TaskPack;

internal static class ScreenHelper
{
    // 가방 위에 띄운 대화상자가 화면 밖으로 나가지 않게, 처음 뜰 때 그 모니터의 작업 영역 안으로 밀어 넣는다
    public static void KeepInsideWorkArea(Window window) =>
        window.Loaded += (_, _) => ClampToWorkArea(new WindowInteropHelper(window).Handle);

    // 창이 걸친 모니터의 작업 영역 안으로 창을 밀어 넣는다 (창이 더 크면 왼쪽·위 우선)
    public static void ClampToWorkArea(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return;
        var info = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST), ref info))
            return;

        var work = info.rcWork;
        int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
        int x = Math.Max(work.Left, Math.Min(rect.Left, work.Right - w));
        int y = Math.Max(work.Top, Math.Min(rect.Top, work.Bottom - h));
        if (x != rect.Left || y != rect.Top)
            SetWindowPos(hwnd, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
    }
}
