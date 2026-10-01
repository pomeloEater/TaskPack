using Xunit;

namespace TaskPack.Tests;

public class StartupPlanTests
{
    [Theory]
    // 열려 있는 가방이 있으면 (끔·켬 상관없이) 닫으라고 알린다. 자동 실행이면 조용히 끝낸다
    [InlineData(false, true, false, false, StartupAction.CloseOpenBag, false)]
    [InlineData(false, true, true, true, StartupAction.CloseOpenBag, false)]
    [InlineData(true, true, false, true, StartupAction.ExitQuietly, false)]
    // 상주 중인 TaskPack이 있고 가방이 닫혀 있으면 열라고 알린다. 자동 실행이면 조용히 끝낸다
    [InlineData(false, false, true, true, StartupAction.AskResident, false)]
    [InlineData(true, false, true, true, StartupAction.ExitQuietly, false)]
    // 아무도 없고 마우스오버가 켜져 있으면 상주한다. 자동 실행이면 가방은 열지 않는다
    [InlineData(false, false, false, true, StartupAction.RunResident, true)]
    [InlineData(true, false, false, true, StartupAction.RunResident, false)]
    // 마우스오버가 꺼져 있으면 지금과 같다 (가방을 열고, 닫으면 끝). 남은 자동 실행은 조용히 끝낸다
    [InlineData(false, false, false, false, StartupAction.RunBag, true)]
    [InlineData(true, false, false, false, StartupAction.ExitQuietly, false)]
    public void 시작_동작을_정한다(bool background, bool bagOpen, bool resident, bool hoverOpen, StartupAction action, bool openBag)
    {
        var plan = StartupPlan.Decide(background, bagOpen, resident, hoverOpen);
        Assert.Equal(action, plan.Action);
        Assert.Equal(openBag, plan.OpenBag);
    }

    [Fact]
    public void 시험_중에는_신호_이름에_꼬리표가_붙는다()
    {
        // TestEnvironment가 TASKPACK_DATA_DIR을 지정하므로 실제 TaskPack과 신호가 섞이지 않는다
        Assert.EndsWith("-test", Signals.Drawer);
        Assert.EndsWith("-test", Signals.Open);
    }
}
