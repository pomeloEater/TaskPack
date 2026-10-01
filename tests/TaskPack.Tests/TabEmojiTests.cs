using System.IO;
using Xunit;

namespace TaskPack.Tests;

public class TabEmojiTests
{
    [Fact]
    public void 목록의_이모티콘과_그림_파일이_빠짐없이_짝을_이룬다()
    {
        var assets = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "assets", "emoji");
        var files = Directory.GetFiles(assets, "*.png").Select(f => Path.GetFileNameWithoutExtension(f)!).OrderBy(n => n).ToList();
        var slugs = TabEmoji.Catalog.Select(e => e.Slug).OrderBy(n => n).ToList();

        Assert.Equal(files, slugs);
        Assert.Equal(slugs.Count, slugs.Distinct().Count());
        Assert.Equal(TabEmoji.Catalog.Count, TabEmoji.Catalog.Select(e => e.Emoji).Distinct().Count());
    }

    [Fact]
    public void 글자_모양_선택자가_달라도_같은_이모티콘으로_찾는다()
    {
        Assert.Equal("gear", TabEmoji.Find("⚙")?.Slug);
        Assert.Equal("gear", TabEmoji.Find("⚙️")?.Slug);
        Assert.Equal("briefcase", TabEmoji.Find("\U0001F4BC")?.Slug);
        Assert.Null(TabEmoji.Find("\U0001F9B8"));   // 목록에 없는 이모티콘
        Assert.Null(TabEmoji.Find(null));
    }

    [Theory]
    [InlineData("\U0001F9B8", "\U0001F9B8")]                       // 목록 밖 이모티콘
    [InlineData("  \U0001F4BC  ", "\U0001F4BC")]                   // 앞뒤 공백
    [InlineData("\U0001F44D\U0001F3FD", "\U0001F44D\U0001F3FD")]   // 피부색이 붙은 이모티콘은 한 글자
    [InlineData("\U0001F4BC\U0001F4BB", "\U0001F4BC")]             // 여러 개를 넣으면 첫 번째만
    [InlineData("❤️", "❤️")]
    public void 입력에서_이모티콘_하나를_뽑는다(string input, string expected) =>
        Assert.Equal(expected, TabEmoji.Extract(input));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("A")]
    [InlineData("가")]
    [InlineData("7")]
    [InlineData("abc\U0001F4BC")]
    public void 이모티콘이_아니면_거부한다(string input) => Assert.Null(TabEmoji.Extract(input));

    [Fact]
    public void 탭_이모티콘이_저장_불러오기와_백업_왕복을_거쳐도_남는다()
    {
        var bag = BagStore.CreateBag("시험");
        bag.TabEmoji = "\U0001F680";
        bag.HideName = true;
        BagStore.SaveBag(bag);
        Assert.Equal("\U0001F680", BagStore.LoadBag(bag.Id).TabEmoji);

        var zip = Path.Combine(TestEnvironment.DataDir, "roundtrip.zip");
        Backup.Export(zip);

        // 이모티콘을 지운 뒤 백업을 불러오면 되돌아와야 한다
        bag.TabEmoji = null;
        bag.HideName = false;
        BagStore.SaveBag(bag);
        Backup.Import(zip);

        var restored = BagStore.LoadBag(bag.Id);
        Assert.Equal("\U0001F680", restored.TabEmoji);
        Assert.True(restored.HideName);
        Assert.Null(restored.TabIcon);
    }

    [Fact]
    public void 이모티콘_없는_옛_가방_파일도_읽는다()
    {
        var bag = BagStore.CreateBag("옛 가방");
        File.WriteAllText(Path.Combine(BagStore.BagDir(bag.Id), "bag.json"), """{ "name": "옛 가방", "columns": 3, "rows": 3, "tabIcon": null }""");
        Assert.Null(BagStore.LoadBag(bag.Id).TabEmoji);
    }
}
