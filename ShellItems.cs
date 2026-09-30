using System.IO;
using System.Runtime.InteropServices;
using static TaskPack.NativeMethods;

namespace TaskPack;

// 파일이 아닌 셸 항목(시작 메뉴의 스토어 앱 등)을 다룬다
internal static class ShellItems
{
    public const string IdListFormat = "Shell IDList Array";

    // 탐색기가 "Windows 앱 목록" 항목에 붙이는 경로 앞부분
    private const string AppsFolderParsing = "::{4234D49B-0245-4DF3-B780-3893943456E1}\\";

    public static bool IsShellPath(string path) => path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase);

    // 끌어 놓은 셸 항목 목록(CIDA 구조)을 (경로, 표시 이름)으로 바꾼다
    public static List<(string Path, string? Name)> FromIdList(MemoryStream data)
    {
        var result = new List<(string, string?)>();
        var bytes = data.ToArray();
        if (bytes.Length < 8)
            return result;

        // CIDA: [항목 수][부모 위치][항목1 위치]...  위치는 이 버퍼 시작 기준
        var count = BitConverter.ToInt32(bytes, 0);
        if (count <= 0 || bytes.Length < 4 * (count + 2))
            return result;

        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);
        try
        {
            var start = handle.AddrOfPinnedObject();
            var parent = start + BitConverter.ToInt32(bytes, 4);
            for (var i = 1; i <= count; i++)
            {
                var offset = BitConverter.ToInt32(bytes, 4 + 4 * i);
                if (offset <= 0 || offset >= bytes.Length)
                    continue;

                var full = ILCombine(parent, start + offset);
                if (full == IntPtr.Zero)
                    continue;
                try
                {
                    var parsing = NameOf(full, SIGDN_DESKTOPABSOLUTEPARSING);
                    if (string.IsNullOrEmpty(parsing))
                        continue;
                    var path = ToLaunchPath(parsing);
                    // 일반 파일이면 파일 이름에서 표시 이름을 만들므로 따로 저장하지 않는다
                    var name = IsShellPath(path) ? NameOf(full, SIGDN_NORMALDISPLAY) : null;
                    result.Add((path, name));
                }
                finally
                {
                    ILFree(full);
                }
            }
        }
        finally
        {
            handle.Free();
        }
        return result;
    }

    // 파일 경로면 그대로, 앱 목록 항목이면 shell:AppsFolder\<앱 식별자>, 그 밖의 가상 항목은 shell: 경로
    private static string ToLaunchPath(string parsing)
    {
        if (parsing.StartsWith(AppsFolderParsing, StringComparison.OrdinalIgnoreCase))
            return @"shell:AppsFolder\" + parsing[AppsFolderParsing.Length..];
        if (parsing.StartsWith("::", StringComparison.Ordinal))
            return "shell:" + parsing;
        return parsing;
    }

    // shell: 경로를 셸 항목 식별자(PIDL)로 바꾼다. 실패하면 Zero. 쓴 뒤 ILFree 필요
    public static IntPtr Parse(string shellPath) =>
        SHParseDisplayName(shellPath, IntPtr.Zero, out var pidl, 0, out _) == 0 ? pidl : IntPtr.Zero;

    public static bool Exists(string shellPath)
    {
        var pidl = Parse(shellPath);
        if (pidl == IntPtr.Zero)
            return false;
        ILFree(pidl);
        return true;
    }

    private static string? NameOf(IntPtr pidl, uint sigdn)
    {
        if (SHGetNameFromIDList(pidl, sigdn, out var ptr) != 0 || ptr == IntPtr.Zero)
            return null;
        try { return Marshal.PtrToStringUni(ptr); }
        finally { Marshal.FreeCoTaskMem(ptr); }
    }
}
