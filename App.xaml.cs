using System.IO;
using System.Text.Json;
using System.Windows;

namespace TaskPack;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 가방이 이미 열려 있으면(작업표시줄 아이콘을 다시 누른 경우) 닫으라는 신호만 보내고 끝낸다
        if (EventWaitHandle.TryOpenExisting(BagWindow.SignalName, out var signal))
        {
            signal.Set();
            signal.Dispose();
            Shutdown();
            return;
        }

        // 고정한 작업표시줄 바로가기와 같은 식별자를 써야 작업표시줄 아이콘 아래에 "실행 중"으로 묶인다
        NativeMethods.SetCurrentProcessExplicitAppUserModelID(Drawer.AppId);

        DrawerConfig config;
        List<Bag> bags;
        try
        {
            config = BagStore.LoadConfig();
            bags = config.Tabs.Select(BagStore.LoadBag).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            MessageBox.Show($"가방 정보를 읽지 못했습니다.\n{BagStore.Root}\n\n{ex.Message}",
                "TaskPack", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        new BagWindow(config, bags).Show();
    }
}
