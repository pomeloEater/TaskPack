using System.Windows;
using Xunit;

namespace TaskPack.Tests;

public class SettingsWindowTests
{
    // 설정 창의 "버전" 카드가 XAML 오류 없이 만들어지고 상태 문구를 채우는지 (창은 화면에 띄우지 않는다)
    [Fact]
    public void 버전_카드가_상태를_보여_준다()
    {
        Exception? error = null;
        string? latestText = null;
        var thread = new Thread(() =>
        {
            try
            {
                // 앱 전체 스타일(App.xaml)을 읽어 둬야 설정 창의 StaticResource가 풀린다
                new App().InitializeComponent();
                var newer = new Version(UpdateCheck.Current.Major + 1, 0, 0).ToString();
                var window = new SettingsWindow(new DrawerConfig { LatestVersion = newer }, new List<Bag> { new() });
                latestText = (window.FindName("LatestVersionText") as System.Windows.Controls.TextBlock)?.Text;
                window.Close();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Contains("새 버전이 나왔어요", latestText);
    }
}
