using Xunit;

namespace TaskPack.Tests;

public class AutostartTests
{
    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]  // 사용
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]  // 사용
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]   // 작업 관리자에서 사용 안 함
    [InlineData(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[0], false)]
    public void 작업_관리자에서_껐는지_읽는다(byte[] approved, bool disabled) =>
        Assert.Equal(disabled, Autostart.IsDisabledFlag(approved));

    [Fact]
    public void 값이_없으면_꺼진_것으로_읽는다() => Assert.False(Autostart.IsDisabledFlag(null));

    [Fact]
    public void 자동_실행_명령은_경로를_따옴표로_감싸고_background를_붙인다() =>
        Assert.Equal("\"C:\\Program Files\\TaskPack\\TaskPack.exe\" --background", Autostart.CommandFor(@"C:\Program Files\TaskPack\TaskPack.exe"));

    // 시험 중에는 "TaskPack (시험)"이라는 이름으로만 쓰므로 실제 "TaskPack" 값은 건드리지 않는다. 끝나면 지운다
    [Fact]
    public void 켜고_끄면_상태가_따라온다()
    {
        try
        {
            Assert.True(Autostart.TrySet(true));
            Assert.Equal(AutostartState.On, Autostart.GetState());
            Assert.True(Autostart.TrySet(false));
            Assert.Equal(AutostartState.Off, Autostart.GetState());
        }
        finally
        {
            Autostart.TrySet(false);
        }
    }

    [Fact]
    public void 마우스오버를_켜면_자동_실행도_켜지고_끄면_지워진다()
    {
        var config = new DrawerConfig();
        try
        {
            HoverSetting.Apply(config, true);
            Assert.True(config.HoverOpen);
            Assert.Equal(AutostartState.On, Autostart.GetState());
            HoverSetting.Apply(config, false);
            Assert.False(config.HoverOpen);
            Assert.Equal(AutostartState.Off, Autostart.GetState());
        }
        finally
        {
            Autostart.TrySet(false);
        }
    }
}
