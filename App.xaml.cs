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

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var background = e.Args.Contains("--background");

        // 가방이 열려 있으면 닫으라는 신호, 상주 중인 TaskPack이 있으면 열라는 신호만 보내고 끝낸다
        var bagOpen = EventWaitHandle.TryOpenExisting(Signals.Drawer, out var drawerSignal);
        EventWaitHandle? openSignal = null;
        var resident = !bagOpen && EventWaitHandle.TryOpenExisting(Signals.Open, out openSignal);
        if (bagOpen || resident)
        {
            var action = StartupPlan.Decide(background, bagOpen, resident, hoverOpen: false).Action;
            if (action == StartupAction.CloseOpenBag)
                drawerSignal!.Set();
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
                new BagWindow(config, bags).Show();
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
        // 가방을 닫아도 프로그램은 남는다
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _openWait = ThreadPool.RegisterWaitForSingleObject(_openSignal,
            (_, _) => Dispatcher.InvokeAsync(OpenBagFromSignal), null, Timeout.Infinite, executeOnlyOnce: false);
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

    private void ShowBag(DrawerConfig config, List<Bag> bags)
    {
        var window = new BagWindow(config, bags);
        _bagWindow = window;
        window.Closed += (_, _) =>
        {
            _bagWindow = null;
            if (_resident && !EndResidentIfOff())
                TrimMemory();
        };
        window.Show();
    }

    // 가방에서 마우스오버를 끄고 닫았으면 상주를 끝낸다. 끝냈으면 true
    private bool EndResidentIfOff()
    {
        try
        {
            if (BagStore.LoadConfig().HoverOpen)
                return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
        Shutdown();
        return true;
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
        _openWait?.Unregister(null);
        _openSignal?.Dispose();
        base.OnExit(e);
    }
}
