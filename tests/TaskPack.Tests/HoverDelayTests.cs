using Xunit;

namespace TaskPack.Tests;

public class HoverDelayTests
{
    [Theory]
    [InlineData(200, 200)]
    [InlineData(400, 400)]
    [InlineData(700, 700)]
    [InlineData(1000, 1000)]
    [InlineData(0, 400)]      // 선택지에 없는 값은 기본값으로 돌린다
    [InlineData(-5, 400)]
    [InlineData(450, 400)]
    [InlineData(99999, 400)]
    public void 머무는_시간은_선택지만_허용한다(int input, int expected) =>
        Assert.Equal(expected, HoverDelay.Normalize(input));

    [Fact]
    public void 기본값은_0점4초이고_선택지에_들어_있다()
    {
        Assert.Equal(400, new DrawerConfig().HoverDelayMs);
        Assert.Contains(HoverDelay.Options, o => o.Ms == HoverDelay.Default);
    }
}
