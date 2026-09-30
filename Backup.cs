using System.IO;
using System.IO.Compression;

namespace TaskPack;

// 모든 가방과 설정을 zip 하나로 내보내고 불러온다
internal static class Backup
{
    private const string ConfigName = "config.json";
    private const string BagsPrefix = "bags/";
    private const string DrawerIconPrefix = "drawer-icon-";

    // 백업에 담는 항목: config.json, bags\..., drawer-icon-*
    private static bool IsIncluded(string entryName) =>
        entryName == ConfigName ||
        entryName.StartsWith(BagsPrefix, StringComparison.OrdinalIgnoreCase) ||
        (entryName.StartsWith(DrawerIconPrefix, StringComparison.OrdinalIgnoreCase) && !entryName.Contains('/'));

    public static void Export(string zipPath)
    {
        var temp = zipPath + ".tmp";
        using (var zip = ZipFile.Open(temp, ZipArchiveMode.Create))
        {
            foreach (var file in Directory.EnumerateFiles(BagStore.Root, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetRelativePath(BagStore.Root, file).Replace('\\', '/');
                if (IsIncluded(name) && !name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                    zip.CreateEntryFromFile(file, name);
            }
        }
        File.Move(temp, zipPath, overwrite: true);
    }

    // 지금 상태는 restore-backup-<시각> 폴더로 옮겨 두고 백업을 푼다. 옮겨 둔 폴더 경로를 돌려준다.
    // 풀다가 실패하면 옮겨 둔 상태로 되돌린다.
    public static string Import(string zipPath)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var entries = zip.Entries.Where(e => e.Name.Length > 0 && IsIncluded(e.FullName)).ToList();
        if (!entries.Any(e => e.FullName == ConfigName || e.FullName.StartsWith(BagsPrefix, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("TaskPack 백업 파일이 아닙니다.");

        var root = Path.GetFullPath(BagStore.Root) + Path.DirectorySeparatorChar;
        var targets = entries.Select(e => (Entry: e, Path: Path.GetFullPath(Path.Combine(BagStore.Root, e.FullName)))).ToList();
        // zip 안의 경로가 TaskPack 폴더 밖을 가리키면(../ 등) 거부
        if (targets.Any(t => !t.Path.StartsWith(root, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("백업 파일에 잘못된 경로가 들어 있습니다.");

        var saved = Path.Combine(BagStore.Root, $"restore-backup-{DateTime.Now:yyyyMMddHHmmss}");
        Directory.CreateDirectory(saved);
        MoveCurrent(BagStore.Root, saved);
        try
        {
            foreach (var (entry, path) in targets)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                entry.ExtractToFile(path, overwrite: true);
            }
        }
        catch
        {
            // 반쯤 풀린 내용을 지우고 원래 상태로 되돌린다
            var partial = Path.Combine(BagStore.Root, $"restore-failed-{DateTime.Now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(partial);
            MoveCurrent(BagStore.Root, partial);
            MoveCurrent(saved, BagStore.Root);
            throw;
        }
        return saved;
    }

    // from 폴더의 config.json, bags, drawer-icon-* 를 to 폴더로 옮긴다
    private static void MoveCurrent(string from, string to)
    {
        var config = Path.Combine(from, ConfigName);
        if (File.Exists(config))
            File.Move(config, Path.Combine(to, ConfigName));

        var bags = Path.Combine(from, "bags");
        if (Directory.Exists(bags))
            BagStore.MoveDirectory(bags, Path.Combine(to, "bags"));

        foreach (var icon in Directory.EnumerateFiles(from, DrawerIconPrefix + "*"))
            File.Move(icon, Path.Combine(to, Path.GetFileName(icon)));
    }
}
