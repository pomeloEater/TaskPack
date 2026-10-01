using TaskPack;
using Xunit;

namespace TaskPack.Tests;

public class UpdateCheckTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.FromHours(9));

    // 지금 실행 중인 버전보다 높은 버전 / 같은 버전
    private static string Newer => new Version(UpdateCheck.Current.Major + 1, 0, 0).ToString();
    private static string Same => UpdateCheck.Current.ToString(3);

    // ───────────── 버전 읽기·비교 ─────────────

    [Theory]
    [InlineData("v1.1.1", "1.1.1")]
    [InlineData("1.2.0", "1.2.0")]
    [InlineData(" V2.0.10 ", "2.0.10")]
    public void 태그에서_버전을_읽는다(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateCheck.ParseVersion(tag));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v1.2.0-beta.1")]   // 미리 보기 버전은 알리지 않는다
    [InlineData("v1.2.0+build5")]
    public void 읽을_수_없는_태그는_null(string? tag) => Assert.Null(UpdateCheck.ParseVersion(tag));

    [Fact]
    public void 자리_수가_달라도_같은_버전은_새_버전이_아니다()
    {
        // 실행 중인 버전은 1.1.0.0, 태그는 v1.1.0
        var config = new DrawerConfig { LatestVersion = "1.1.0" };
        Assert.False(new Version(1, 1, 0) > new Version(1, 1, 0, 0));
        Assert.True(UpdateCheck.ParseVersion(config.LatestVersion)! <= new Version(1, 1, 0, 0));
    }

    // ───────────── 24시간 간격 ─────────────

    [Fact]
    public void 한_번도_확인하지_않았으면_확인할_때다() =>
        Assert.True(UpdateCheck.IsDue(new DrawerConfig(), Now));

    [Theory]
    [InlineData(23, false)]
    [InlineData(24, true)]
    [InlineData(48, true)]
    public void 마지막_확인_뒤_24시간이_지나야_다시_확인한다(int hoursAgo, bool due) =>
        Assert.Equal(due, UpdateCheck.IsDue(new DrawerConfig { LastUpdateCheck = Now.AddHours(-hoursAgo) }, Now));

    [Fact]
    public void 마지막_확인_시각이_미래면_시계가_틀어진_것이니_확인한다() =>
        Assert.True(UpdateCheck.IsDue(new DrawerConfig { LastUpdateCheck = Now.AddHours(5) }, Now));

    [Fact]
    public void 자동_확인을_끄면_확인하지_않는다() =>
        Assert.False(UpdateCheck.IsDue(new DrawerConfig { AutoUpdateCheck = false }, Now));

    [Fact]
    public void 조회가_실패하면_확인한_것으로_치지_않는다()
    {
        var config = new DrawerConfig();
        UpdateCheck.Apply(config, null, Now);
        Assert.Null(config.LastUpdateCheck);
        Assert.True(UpdateCheck.IsDue(config, Now));
    }

    // ───────────── 알림·건너뛰기 ─────────────

    [Fact]
    public void 새_버전이_있으면_알린다() =>
        Assert.True(UpdateCheck.HasNotice(new DrawerConfig { LatestVersion = Newer }));

    [Fact]
    public void 같은_버전이거나_확인한_적이_없으면_알리지_않는다()
    {
        Assert.False(UpdateCheck.HasNotice(new DrawerConfig { LatestVersion = Same }));
        Assert.False(UpdateCheck.HasNotice(new DrawerConfig()));
    }

    [Fact]
    public void 건너뛴_버전은_알리지_않고_다음_버전은_다시_알린다()
    {
        var config = new DrawerConfig { LatestVersion = Newer, SkippedVersion = Newer };
        Assert.False(UpdateCheck.HasNotice(config));

        config.LatestVersion = new Version(UpdateCheck.Current.Major + 2, 0, 0).ToString();
        Assert.True(UpdateCheck.HasNotice(config));
    }

    [Fact]
    public void 조회_결과를_설정에_반영한다()
    {
        var config = new DrawerConfig();
        UpdateCheck.Apply(config, new UpdateCheck.Release(new Version(9, 1, 2), "https://example.test/a.exe"), Now);
        Assert.Equal(Now, config.LastUpdateCheck);
        Assert.Equal("9.1.2", config.LatestVersion);
        Assert.Equal("https://example.test/a.exe", config.LatestUrl);
    }

    // ───────────── 같은 판의 설치 파일 고르기 ─────────────

    private static readonly (string, string)[] Assets =
    {
        ("TaskPack-Setup-1.1.1.exe", "https://example.test/full.exe"),
        ("TaskPack-Setup-1.1.1-lite.exe", "https://example.test/lite.exe"),
        ("notes.txt", "https://example.test/notes.txt"),
    };

    [Theory]
    [InlineData("full", "https://example.test/full.exe")]
    [InlineData("lite", "https://example.test/lite.exe")]
    public void 판에_맞는_설치_파일을_고른다(string edition, string expected) =>
        Assert.Equal(expected, UpdateCheck.PickAssetUrl(Assets, edition));

    [Fact]
    public void 맞는_파일이_없으면_null()
    {
        Assert.Null(UpdateCheck.PickAssetUrl(new[] { ("TaskPack-Setup-1.1.1.exe", "u") }, "lite"));
        Assert.Null(UpdateCheck.PickAssetUrl(Array.Empty<(string, string)>(), "full"));
    }

    // ───────────── GitHub 응답 읽기 ─────────────

    private const string FakeRelease = """
        {
          "tag_name": "v1.1.1",
          "assets": [
            { "name": "TaskPack-Setup-1.1.1.exe", "browser_download_url": "https://example.test/full.exe" },
            { "name": "TaskPack-Setup-1.1.1-lite.exe", "browser_download_url": "https://example.test/lite.exe" }
          ]
        }
        """;

    [Fact]
    public void 릴리스_응답에서_버전과_같은_판의_주소를_뽑는다()
    {
        var lite = UpdateCheck.ParseRelease(FakeRelease, "lite")!;
        Assert.Equal(new Version(1, 1, 1), lite.Version);
        Assert.Equal("https://example.test/lite.exe", lite.DownloadUrl);
        Assert.Equal("https://example.test/full.exe", UpdateCheck.ParseRelease(FakeRelease, "full")!.DownloadUrl);
    }

    [Fact]
    public void 설치_파일이_없으면_릴리스_페이지를_연다() =>
        Assert.Equal(UpdateCheck.ReleasesPage, UpdateCheck.ParseRelease("""{ "tag_name": "v1.1.1", "assets": [] }""", "full")!.DownloadUrl);

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "tag_name": "v1.2.0-beta" }""")]
    public void 읽을_수_없는_응답은_null(string json) => Assert.Null(UpdateCheck.ParseRelease(json, "full"));
}
