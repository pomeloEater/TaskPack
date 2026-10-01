using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace TaskPack;

// GitHub 공개 API에서 최신 릴리스 정보만 읽는다. 보내는 정보는 없다 (익명 GET 한 번)
internal static class UpdateCheck
{
    private const string LatestReleaseApi = "https://api.github.com/repos/pomeloEater/TaskPack/releases/latest";
    public const string ReleasesPage = "https://github.com/pomeloEater/TaskPack/releases/latest";

    public static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(6);

    // 지금 실행 중인 버전 (TaskPack.csproj의 <Version>)
    public static Version Current { get; } = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    // 이 프로그램이 어느 판인지: "full"(.NET 포함) / "lite"(.NET 따로). 빌드 때 TaskPack.csproj가 넣는다
    public static string Edition { get; } = Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "Edition")?.Value ?? "lite";

    public sealed record Release(Version Version, string DownloadUrl);

    // "v1.1.1" / "1.1.1" → 버전. 읽을 수 없으면 null (미리 보기용 "1.2.0-beta" 같은 것은 알리지 않는다)
    public static Version? ParseVersion(string? tag)
    {
        var text = (tag ?? "").Trim().TrimStart('v', 'V');
        if (text.Length == 0 || text.Contains('-') || text.Contains('+'))
            return null;
        return Version.TryParse(text, out var version) ? version : null;
    }

    // 마지막 확인 뒤 24시간이 지났거나, 아직 확인한 적이 없거나, 시계가 과거로 돌아가 있으면 확인할 때다
    public static bool IsDue(DrawerConfig config, DateTimeOffset now) =>
        config.AutoUpdateCheck &&
        (config.LastUpdateCheck is not { } last || now < last || now - last >= Interval);

    // 알릴 새 버전이 있는지: 지금보다 높고, 사용자가 ×로 건너뛴 버전이 아니어야 한다
    public static bool HasNotice(DrawerConfig config) =>
        ParseVersion(config.LatestVersion) is { } latest &&
        latest > Current &&
        !string.Equals(config.SkippedVersion, config.LatestVersion, StringComparison.OrdinalIgnoreCase);

    // 같은 판의 설치 파일 주소. -lite 파일은 가벼운 판, 그 밖의 .exe는 .NET 포함판. 못 찾으면 null
    public static string? PickAssetUrl(IEnumerable<(string Name, string Url)> assets, string edition)
    {
        var exes = assets.Where(a => a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)).ToList();
        var wantLite = edition == "lite";
        return exes.FirstOrDefault(a => a.Name.EndsWith("-lite.exe", StringComparison.OrdinalIgnoreCase) == wantLite).Url;
    }

    // GitHub의 최신 릴리스 JSON에서 버전과 받을 주소를 뽑는다. 읽을 수 없으면 null
    public static Release? ParseRelease(string json, string edition)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (ParseVersion(root.GetProperty("tag_name").GetString()) is not { } version)
                return null;

            var assets = root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array
                ? list.EnumerateArray()
                    .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                    .ToList()
                : new List<(string, string)>();

            // 같은 판의 파일이 없으면 릴리스 페이지를 연다
            return new Release(version, PickAssetUrl(assets, edition) ?? ReleasesPage);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // 최신 릴리스를 조회한다. 오프라인·제한 초과 등 어떤 실패든 null (조용히 넘어간다)
    public static async Task<Release?> FetchLatestAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = Timeout };
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"TaskPack/{Current}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            var json = await http.GetStringAsync(LatestReleaseApi).ConfigureAwait(false);
            return ParseRelease(json, Edition);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    // 조회 결과를 설정에 반영한다. 실패(null)한 조회는 확인한 것으로 치지 않아 다음에 다시 시도한다
    public static void Apply(DrawerConfig config, Release? release, DateTimeOffset now)
    {
        if (release is null)
            return;
        config.LastUpdateCheck = now;
        config.LatestVersion = release.Version.ToString();
        config.LatestUrl = release.DownloadUrl;
    }
}
