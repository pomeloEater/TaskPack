using System.IO;
using System.Text.Json;
using System.Windows;
using static TaskPack.NativeMethods;

namespace TaskPack;

public partial class App : Application
{
    private EventWaitHandle? _openSignal;           // 상주 중에만 세워 두는 "가방 열어" 신호
    private RegisteredWaitHandle? _openWait;
    private BagWindow? _bagWindow;                  // 지금 열려 있는 가방 (상주 중에는 닫았다 다시 연다)
    private bool _resident;
    private volatile bool _bagOpen;                 // 가방이 열려 있는지 (마우스 감시 스레드가 읽는다)
    private HoverMonitor? _hover;
    private TrayIcon? _tray;
    private volatile bool _settingsOpen;            // 알림 영역에서 연 설정 창이 떠 있는 동안은 마우스를 올려도 가방을 열지 않는다

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var background = e.Args.Contains("--background");
        // 가방을 닫은 뒤 끝낼지 남을지는 그때의 설정을 보고 정한다 (AfterBagClosed)
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // 가방이 열려 있으면 닫으라는 신호, 상주 중인 TaskPack이 있으면 열라는 신호만 보내고 끝낸다
        var bagOpen = EventWaitHandle.TryOpenExisting(Signals.Drawer, out var drawerSignal);
        EventWaitHandle? openSignal = null;
        var resident = !bagOpen && EventWaitHandle.TryOpenExisting(Signals.Open, out openSignal);
        if (bagOpen || resident)
        {
            var action = StartupPlan.Decide(background, bagOpen, resident, hoverOpen: false).Action;
            if (action == StartupAction.CloseOpenBag)
            {
                // 마우스를 올려 막 열린 가방이면 닫는 대신 앞으로 가져와야 하므로 권한도 넘긴다
                AllowSetForegroundWindow(ASFW_ANY);
                drawerSignal!.Set();
            }
            else if (action == StartupAction.AskResident)
            {
                // 이 프로세스는 작업표시줄이 띄워서 앞으로 나올 권한이 있다. 상주 쪽이 가방을 앞으로 가져올 수 있게 넘긴다
                AllowSetForegroundWindow(ASFW_ANY);
                openSignal!.Set();
            }
            drawerSignal?.Dispose();
            openSignal?.Dispose();
            Shutdown();
            return;
        }

        // 고정한 작업표시줄 바로가기와 같은 식별자를 써야 작업표시줄 아이콘 아래에 "실행 중"으로 묶인다
        SetCurrentProcessExplicitAppUserModelID(Drawer.AppId);

        DrawerConfig config;
        List<Bag> bags;
        try
        {
            (config, bags) = LoadAll();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show($"가방 정보를 읽지 못했습니다.\n{BagStore.Root}\n\n{ex.Message}",
                "TaskPack", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        var plan = StartupPlan.Decide(background, false, false, config.HoverOpen);
        switch (plan.Action)
        {
            case StartupAction.RunResident:
                if (!BecomeResident())
                {
                    // 그 사이 다른 TaskPack이 먼저 상주를 시작했다
                    if (plan.OpenBag && EventWaitHandle.TryOpenExisting(Signals.Open, out var other))
                    {
                        AllowSetForegroundWindow(ASFW_ANY);
                        other.Set();
                        other.Dispose();
                    }
                    Shutdown();
                    return;
                }
                if (plan.OpenBag)
                    ShowBag(config, bags);
                break;
            case StartupAction.RunBag:
                ShowBag(config, bags);
                break;
            default:
                Shutdown();
                break;
        }
    }

    private static (DrawerConfig, List<Bag>) LoadAll()
    {
        var config = BagStore.LoadConfig();
        return (config, config.Tabs.Select(BagStore.LoadBag).ToList());
    }

    // 상주를 시작한다. 이미 다른 TaskPack이 상주 중이면 false
    private bool BecomeResident()
    {
        _openSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Signals.Open, out var created);
        if (!created)
        {
            _openSignal.Dispose();
            _openSignal = null;
            return false;
        }
        _resident = true;
        _openWait = ThreadPool.RegisterWaitForSingleObject(_openSignal,
            (_, _) => Dispatcher.InvokeAsync(OpenBagFromSignal), null, Timeout.Infinite, executeOnlyOnce: false);
        // 작업표시줄 아이콘 위에 마우스가 머물면 가방을 연다
        _hover = new HoverMonitor(() => !_bagOpen && !_settingsOpen, button => Dispatcher.InvokeAsync(() => OpenBagFromHover(button)));
        // 켜져 있다는 사실과 끄는 길을 알림 영역(시계 옆)에 보인다
        _tray = new TrayIcon(OpenBagFromSignal, OpenSettingsFromTray, TurnOffHoverFromTray, Shutdown);
        Autostart.RefreshPath();
        return true;
    }

    // 다른 TaskPack(작업표시줄 아이콘 클릭)이 "열어 달라"고 알려 왔다. 설정과 가방은 열 때마다 새로 읽는다
    private void OpenBagFromSignal()
    {
        if (_bagWindow is not null)
            return;
        try
        {
            var (config, bags) = LoadAll();
            ShowBag(config, bags);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 읽지 못하면 이번에는 열지 않는다. 상주는 계속한다
        }
    }

    // 마우스가 TaskPack 아이콘 위에 머물렀다. 설정이 꺼졌으면 열지 않는다
    private void OpenBagFromHover(RECT button)
    {
        if (_bagWindow is not null)
            return;
        try
        {
            var (config, bags) = LoadAll();
            if (config.HoverOpen)
                ShowBag(config, bags, button);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // 읽지 못하면 이번에는 열지 않는다
        }
    }

    private void ShowBag(DrawerConfig config, List<Bag> bags, RECT? hoverButton = null)
    {
        var window = new BagWindow(config, bags, hoverButton);
        _bagWindow = window;
        _bagOpen = true;
        window.Closed += (_, _) =>
        {
            _bagWindow = null;
            _bagOpen = false;
            AfterBagClosed();
        };
        window.Show();
    }

    // 가방을 닫았다. 지금 설정에 따라 끝내거나(마우스오버 꺼짐), 상주를 시작하거나(설정에서 방금 켬), 계속 상주한다
    private void AfterBagClosed()
    {
        var hoverOpen = ReadHoverOpen();
        if (hoverOpen == false || (hoverOpen is null && !_resident))
        {
            Shutdown();
            return;
        }
        if (!_resident && !BecomeResident())
        {
            Shutdown(); // 그 사이 다른 TaskPack이 상주를 시작했다
            return;
        }
        TrimMemory();
    }

    // 설정을 읽지 못하면 null
    private static bool? ReadHoverOpen()
    {
        try
        {
            return BagStore.LoadConfig().HoverOpen;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    // 알림 영역 메뉴 "설정": 가방 없이 설정 창만 연다
    private void OpenSettingsFromTray()
    {
        if (_bagOpen || _settingsOpen)
            return;
        try
        {
            var (config, bags) = LoadAll();
            _settingsOpen = true;
            new SettingsWindow(config, bags).ShowDialog();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return;
        }
        finally
        {
            _settingsOpen = false;
        }
        // 설정에서 마우스오버를 껐으면 끝낸다
        if (ReadHoverOpen() == false)
            Shutdown();
    }

    // 알림 영역 메뉴 "마우스오버 끄기": 설정을 끄고 상주를 끝낸다 (가방이 열려 있으면 닫을 때 끝난다)
    private void TurnOffHoverFromTray()
    {
        try
        {
            var config = BagStore.LoadConfig();
            HoverSetting.Apply(config, false);
            BagStore.SaveConfig(config);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return;
        }
        if (!_bagOpen)
            Shutdown();
    }

    // 가방을 닫으면 쓰던 메모리를 돌려준다. 늘 켜져 있는 프로그램이 메모리를 붙들고 있지 않게 하기 위해서다
    private static void TrimMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        SetProcessWorkingSetSize(System.Diagnostics.Process.GetCurrentProcess().Handle, -1, -1);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hover?.Dispose();
        _tray?.Dispose();
        _openWait?.Unregister(null);
        _openSignal?.Dispose();
        base.OnExit(e);
    }
}
