using System.IO;
using System.Runtime.CompilerServices;

namespace TaskPack.Tests;

// BagStore.Root는 처음 쓸 때 한 번 정해지므로, 시험이 실제 가방 데이터를 건드리지 않게 그 전에 임시 폴더로 돌린다
internal static class TestEnvironment
{
    public static readonly string DataDir = Path.Combine(Path.GetTempPath(), "taskpack-tests-" + Guid.NewGuid().ToString("N")[..8]);

    [ModuleInitializer]
    internal static void Init()
    {
        Environment.SetEnvironmentVariable("TASKPACK_DATA_DIR", DataDir);
        AppDomain.CurrentDomain.ProcessExit += (_, _) => { try { Directory.Delete(DataDir, true); } catch (IOException) { } };
    }
}
