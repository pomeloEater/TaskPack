using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskPack;

public sealed class Slot
{
    public int Index { get; set; }
    public string Path { get; set; } = "";   // 파일 경로, 가방 폴더 기준 상대 경로, 또는 shell: 경로
    public string? Name { get; set; }        // 셸 항목(스토어 앱 등)의 표시 이름. 파일은 비워 두고 경로에서 만든다
}

public sealed class Bag
{
    public const int DefaultSide = 3;

    [JsonIgnore] public string Id { get; set; } = "";
    public string Name { get; set; } = "가방";
    public int Columns { get; set; } = DefaultSide;
    public int Rows { get; set; } = DefaultSide;
    public bool HideName { get; set; }      // 탭 아이콘이 있을 때 탭 이름을 숨긴다
    public string? TabIcon { get; set; }    // 가방 폴더 기준 상대 경로 또는 exe·dll 절대 경로
    public string? Theme { get; set; }      // null = 전체 설정 따르기, system / light / dark / custom
    public string? Color { get; set; }      // 커스텀 테마 색 (#RRGGBB)
    public List<Slot> Slots { get; set; } = new();

    [JsonIgnore] public int Capacity => Columns * Rows;

    public Slot? Find(int index) => Slots.Find(s => s.Index == index);

    public int FirstEmpty()
    {
        for (var i = 0; i < Capacity; i++)
            if (Find(i) is null)
                return i;
        return -1;
    }
}

public sealed class DrawerConfig
{
    public List<string> Tabs { get; set; } = new();  // 탭 순서 = 가방 id 목록
    public string? LastTab { get; set; }
    public string? DrawerIcon { get; set; }          // TaskPack 폴더 기준 상대 경로 또는 exe·dll 절대 경로
    public string Theme { get; set; } = TaskPack.Theme.FollowSystem;
    public bool HidePinNotice { get; set; }          // 가방 아래 "작업표시줄에 고정하면…" 알림을 다시 보지 않기
}

// %APPDATA%\TaskPack 아래의 설정과 가방을 읽고 쓴다
public static class BagStore
{
    public const int MaxSide = 12;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping, // 한글을 그대로 읽을 수 있게 저장
    };

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaskPack");

    public static string BagsDir { get; } = Path.Combine(Root, "bags");
    private static string ConfigFile => Path.Combine(Root, "config.json");

    public static string BagDir(string id) => Path.Combine(BagsDir, id);

    // 가방 id는 폴더 이름으로 쓰이므로 영문·숫자·-·_ 만 허용
    public static bool IsValidId(string id) =>
        id.Length is > 0 and <= 40 && id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    // ───────────── 설정 ─────────────

    // 탭 목록을 실제 가방 폴더와 맞추고, 가방이 하나도 없으면 기본 가방을 만든다
    public static DrawerConfig LoadConfig()
    {
        var config = File.Exists(ConfigFile)
            ? JsonSerializer.Deserialize<DrawerConfig>(File.ReadAllText(ConfigFile), JsonOptions) ?? new DrawerConfig()
            : new DrawerConfig();

        var existing = Directory.Exists(BagsDir)
            ? Directory.EnumerateDirectories(BagsDir)
                .Select(d => new DirectoryInfo(d))
                .Where(d => IsValidId(d.Name) && File.Exists(Path.Combine(d.FullName, "bag.json")))
                .OrderBy(d => d.CreationTimeUtc)
                .Select(d => d.Name)
                .ToList()
            : new List<string>();

        // 저장된 순서를 유지하고, 목록에 없던 폴더(v1 가방 등)는 뒤에 붙인다
        var tabs = (config.Tabs ?? new()).Where(existing.Contains).Distinct().ToList();
        tabs.AddRange(existing.Where(id => !tabs.Contains(id)));
        config.Tabs = tabs;

        if (config.Tabs.Count == 0)
            config.Tabs.Add(CreateBag("가방").Id);

        if (config.LastTab is null || !config.Tabs.Contains(config.LastTab))
            config.LastTab = config.Tabs[0];

        SaveConfig(config);
        return config;
    }

    public static void SaveConfig(DrawerConfig config) => WriteJson(ConfigFile, config);

    // ───────────── 가방 ─────────────

    public static Bag LoadBag(string id)
    {
        var file = Path.Combine(BagDir(id), "bag.json");
        var bag = File.Exists(file)
            ? JsonSerializer.Deserialize<Bag>(File.ReadAllText(file), JsonOptions) ?? new Bag()
            : new Bag();

        bag.Id = id;
        // 사용자가 json을 직접 고쳤을 때를 대비해 크기와 칸 번호를 보정한다
        bag.Columns = Math.Clamp(bag.Columns, 1, MaxSide);
        bag.Rows = Math.Clamp(bag.Rows, 1, MaxSide);
        bag.Slots = (bag.Slots ?? new())
            .Where(s => s.Index >= 0 && s.Index < bag.Capacity && !string.IsNullOrWhiteSpace(s.Path))
            .GroupBy(s => s.Index)
            .Select(g => g.First())
            .ToList();
        return bag;
    }

    public static void SaveBag(Bag bag) => WriteJson(Path.Combine(BagDir(bag.Id), "bag.json"), bag);

    public static Bag CreateBag(string name)
    {
        var bag = new Bag { Id = "bag-" + Guid.NewGuid().ToString("N")[..8], Name = name };
        SaveBag(bag);
        return bag;
    }

    // 바로 지우지 않고 deleted 폴더로 옮긴다 (잘못 지웠을 때 되살릴 수 있게)
    public static void DeleteBag(string id)
    {
        var dir = BagDir(id);
        if (!Directory.Exists(dir))
            return;
        var deleted = Path.Combine(Root, "deleted");
        Directory.CreateDirectory(deleted);
        MoveDirectory(dir, Path.Combine(deleted, $"{id}-{DateTime.Now:yyyyMMddHHmmss}"));
    }

    // 보안 프로그램에 따라 AppData 아래 폴더의 이름 바꾸기·이동이 막힌다(복사·삭제는 허용).
    // 그럴 때는 복사한 뒤 원본을 지운다. 원본을 지우지 못하면 복사본을 치우고 실패로 돌려준다.
    public static void MoveDirectory(string from, string to)
    {
        try
        {
            Directory.Move(from, to);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        CopyDirectory(from, to);
        try
        {
            Directory.Delete(from, recursive: true);
        }
        catch
        {
            try { Directory.Delete(to, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from))
            File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var dir in Directory.EnumerateDirectories(from))
            CopyDirectory(dir, Path.Combine(to, Path.GetFileName(dir)));
    }

    // 아이템의 (행, 열) 자리를 유지하며 크기를 바꾼다.
    // 새 크기 밖으로 나가는 아이템은 빈칸으로 옮긴다. 아이템 수보다 칸이 적으면 바꾸지 않고 false.
    public static bool TryResize(Bag bag, int columns, int rows)
    {
        columns = Math.Clamp(columns, 1, MaxSide);
        rows = Math.Clamp(rows, 1, MaxSide);
        if (bag.Slots.Count > columns * rows)
            return false;

        var overflow = new List<Slot>();
        var used = new HashSet<int>();
        foreach (var slot in bag.Slots.OrderBy(s => s.Index))
        {
            int row = slot.Index / bag.Columns, col = slot.Index % bag.Columns;
            if (row < rows && col < columns)
            {
                slot.Index = row * columns + col;
                used.Add(slot.Index);
            }
            else
            {
                overflow.Add(slot);
            }
        }

        var next = 0;
        foreach (var slot in overflow)
        {
            while (used.Contains(next)) next++;
            slot.Index = next;
            used.Add(next);
        }

        bag.Columns = columns;
        bag.Rows = rows;
        return true;
    }

    // ───────────── 경로 ─────────────

    // 저장된 경로 → 실제 경로. 상대 경로는 가방 폴더 기준
    public static string Resolve(Bag bag, string stored) =>
        ShellItems.IsShellPath(stored) || Path.IsPathRooted(stored) ? stored : Path.Combine(BagDir(bag.Id), stored);

    // 실제 경로 → 저장할 경로. 가방 폴더 안이면 상대 경로로 바꿔 백업을 다른 PC에서 풀어도 동작하게 한다
    public static string ToStored(Bag bag, string path) => ToStored(BagDir(bag.Id), path);

    public static string ToStored(string baseDir, string path)
    {
        if (ShellItems.IsShellPath(path))
            return path;
        var full = Path.GetFullPath(path);
        var root = Path.GetFullPath(baseDir) + Path.DirectorySeparatorChar;
        return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full[root.Length..] : full;
    }

    // 가방 폴더의 items 에 파일을 복사하고 저장할 경로(상대)를 돌려준다. 같은 이름이 있으면 번호를 붙인다
    public static string CopyIntoBag(Bag bag, string source)
    {
        var target = NewItemPath(bag, source);
        File.Copy(source, target);
        return ToStored(bag, target);
    }

    // 가방 폴더 안의 파일(가져온 바로가기)을 다른 가방 폴더로 옮기고 새로 저장할 경로를 돌려준다
    public static string MoveIntoBag(Bag from, Bag to, string stored)
    {
        var source = Resolve(from, stored);
        var target = NewItemPath(to, source);
        File.Move(source, target);
        return ToStored(to, target);
    }

    private static string NewItemPath(Bag bag, string source)
    {
        var dir = Path.Combine(BagDir(bag.Id), "items");
        Directory.CreateDirectory(dir);
        var name = Path.GetFileNameWithoutExtension(source);
        var ext = Path.GetExtension(source);
        var target = Path.Combine(dir, name + ext);
        for (var n = 2; File.Exists(target); n++)
            target = Path.Combine(dir, $"{name} ({n}){ext}");
        return target;
    }

    // 쓰는 도중 끊겨도 기존 파일이 깨지지 않도록 임시 파일에 쓴 뒤 교체
    private static void WriteJson<T>(string file, T value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var temp = file + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
        File.Move(temp, file, overwrite: true);
    }
}
