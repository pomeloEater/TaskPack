using System.Diagnostics;
using System.IO;
using static TaskPack.NativeMethods;

namespace TaskPack;

// 작업표시줄의 가방 아이콘 = TaskPack 창을 여는 바로가기와 그 아이콘.
// 클래스 이름과 식별자(TaskPack.Drawer)는 이미 고정된 바로가기가 쓰고 있어 바꾸지 않는다
internal static class Drawer
{
    public const string AppId = "TaskPack.Drawer";

    private static readonly string AppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    private static readonly string PinnedDir = Path.Combine(AppData, @"Microsoft\Internet Explorer\Quick Launch\User Pinned\TaskBar");
    private static readonly string StartMenuLink = Path.Combine(AppData, @"Microsoft\Windows\Start Menu\Programs\TaskPack.lnk");

    public static string ExePath => Environment.ProcessPath
        ?? throw new InvalidOperationException("TaskPack.exe 위치를 알 수 없습니다.");

    // 지금 작업표시줄 아이콘 파일 (설정이 없거나 파일이 사라졌으면 TaskPack.exe)
    public static string IconPath(DrawerConfig config)
    {
        if (config.DrawerIcon is { } stored)
        {
            var path = Path.IsPathRooted(stored) ? stored : Path.Combine(BagStore.Root, stored);
            if (File.Exists(path))
                return path;
        }
        return ExePath;
    }

    public static bool IsPinned() =>
        Directory.Exists(PinnedDir) &&
        Directory.EnumerateFiles(PinnedDir, "*.lnk").Any(lnk => ShellLink.ReadAppId(lnk) == AppId);

    // 모든 사용자용으로 설치했을 때 설치 프로그램이 만드는 시작 메뉴 바로가기
    private static readonly string CommonStartMenuLink = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "TaskPack.lnk");

    // 시작 메뉴에 TaskPack 바로가기를 만든다. 작업표시줄 고정은 사용자가 이 바로가기로 한다.
    // 설치 프로그램이 모든 사용자용 바로가기를 이미 만들었으면 중복으로 만들지 않고 그것을 돌려준다
    public static string CreateShortcut(DrawerConfig config)
    {
        if (File.Exists(CommonStartMenuLink) && ShellLink.ReadAppId(CommonStartMenuLink) == AppId)
            return CommonStartMenuLink;

        Directory.CreateDirectory(Path.GetDirectoryName(StartMenuLink)!);
        ShellLink.Create(StartMenuLink, ExePath, "", IconPath(config), 0, AppId, "TaskPack 가방");
        return StartMenuLink;
    }

    public static void RevealInExplorer(string path) =>
        Process.Start("explorer.exe", $"/select,\"{path}\"")?.Dispose();

    // source가 null이면 기본 아이콘으로 되돌린다. 고친 바로가기 수를 돌려준다
    public static int ChangeIcon(DrawerConfig config, string? source)
    {
        var old = config.DrawerIcon is { } stored && !Path.IsPathRooted(stored) ? Path.Combine(BagStore.Root, stored) : null;
        config.DrawerIcon = source is null
            ? null
            : BagStore.ToStored(BagStore.Root, IconFile.Import(source, BagStore.Root, "drawer-icon"));
        BagStore.SaveConfig(config);

        var updated = RefreshShortcuts(config);
        if (old is not null && IconPath(config) != old)
            IconFile.DeleteImported(old, BagStore.Root);
        return updated;
    }

    // 시작 메뉴 바로가기와 고정된 복사본 중 TaskPack 식별자를 가진 것의 아이콘을 지금 설정으로 맞춘다
    public static int RefreshShortcuts(DrawerConfig config)
    {
        var icon = IconPath(config);
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var targets = new[] { Path.GetDirectoryName(StartMenuLink)!, Path.GetDirectoryName(CommonStartMenuLink)!, PinnedDir, desktop }
            .Where(Directory.Exists)
            .SelectMany(dir => Directory.EnumerateFiles(dir, "*.lnk"))
            .Where(lnk => ShellLink.ReadAppId(lnk) == AppId)
            .ToList();

        var updated = 0;
        foreach (var lnk in targets)
        {
            try
            {
                ShellLink.SetIcon(lnk, icon, 0);
                SHChangeNotifyPath(SHCNE_UPDATEITEM, SHCNF_PATHW, lnk, IntPtr.Zero);
                updated++;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
            {
                // 모든 사용자용 시작 메뉴 바로가기는 관리자 권한이 없으면 고칠 수 없다. 나머지는 계속 고친다
            }
        }
        // 작업표시줄이 아이콘을 다시 읽도록 전체 알림
        SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, IntPtr.Zero, IntPtr.Zero);
        return updated;
    }

    // 같은 앱인지 비교하기 위한 값. 바로가기는 "대상 경로 + 인자", 그 밖에는 경로 자체 (대소문자 무시)
    public static string LaunchKey(string path)
    {
        if (Path.GetExtension(path).Equals(".lnk", StringComparison.OrdinalIgnoreCase) &&
            ShellLink.ReadTarget(path) is { } target)
        {
            // 대상 경로가 없는 설치 관리자형 바로가기는 파일 이름으로 비교한다
            return target.Path.Length > 0
                ? $"{NormalizePath(target.Path)}|{target.Arguments.Trim()}".ToLowerInvariant()
                : "name:" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
        }
        return $"{NormalizePath(path)}|".ToLowerInvariant();
    }

    private static string NormalizePath(string path)
    {
        if (ShellItems.IsShellPath(path))
            return path;
        try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    // 작업표시줄에 고정된 앱 바로가기 (TaskPack 자신은 빼고)
    public static List<string> PinnedApps() =>
        Directory.Exists(PinnedDir)
            ? Directory.EnumerateFiles(PinnedDir, "*.lnk")
                .Where(lnk => ShellLink.ReadAppId(lnk) != AppId)
                .OrderBy(Path.GetFileNameWithoutExtension, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : new List<string>();
}
