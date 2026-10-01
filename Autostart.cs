using System.IO;
using Microsoft.Win32;

namespace TaskPack;

public enum AutostartState
{
    Off,                    // 시작 때 켜지 않는다
    On,                     // 시작 때 켠다
    DisabledInTaskManager,  // 목록에는 있지만 작업 관리자의 "시작 앱"에서 사용 안 함으로 꺼 두었다
}

// "Windows를 시작할 때 TaskPack 켜 두기": 내 계정의 시작 프로그램 목록(HKCU\...\Run)에 값 하나를 쓰고 지운다. 관리자 권한은 필요 없다.
// 켜진 상태는 config.json이 아니라 레지스트리를 직접 읽어 보여 준다. 백업을 다른 PC에 불러와도 자동 실행이 몰래 켜지지 않게 하기 위해서다.
internal static class Autostart
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    // 시험 중(TASKPACK_DATA_DIR)에는 실제 TaskPack의 값을 건드리지 않도록 다른 이름을 쓴다
    private static readonly string ValueName =
        Environment.GetEnvironmentVariable("TASKPACK_DATA_DIR") is { Length: > 0 } ? "TaskPack (시험)" : "TaskPack";

    // 자동 실행으로 뜬 TaskPack은 가방을 열지 않고 상주만 한다 (--background)
    public static string CommandFor(string exePath) => $"\"{exePath}\" --background";

    // 작업 관리자가 "사용 안 함"으로 바꾸면 StartupApproved 값의 첫 바이트가 홀수(3, 7)가 된다. 짝수(2, 6)면 사용
    public static bool IsDisabledFlag(byte[]? approved) => approved is { Length: > 0 } && (approved[0] & 1) == 1;

    public static AutostartState GetState()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string)
            return AutostartState.Off;
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        return IsDisabledFlag(approved?.GetValue(ValueName) as byte[])
            ? AutostartState.DisabledInTaskManager
            : AutostartState.On;
    }

    // 켜거나 끈다. 켤 때는 작업 관리자에서 꺼 둔 표시도 지워서 실제로 켜지게 한다. 레지스트리를 쓰지 못하면 false
    public static bool TrySet(bool on)
    {
        try
        {
            if (on)
            {
                var exe = Environment.ProcessPath;
                if (exe is null)
                    return false;
                using var run = Registry.CurrentUser.CreateSubKey(RunKey);
                run.SetValue(ValueName, CommandFor(exe));
            }
            else
            {
                using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
                run?.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey, writable: true);
            approved?.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            return false;
        }
    }

    // 설치 폴더가 바뀌어 실행 파일 경로가 달라졌으면 값을 고친다 (켜져 있을 때만)
    public static void RefreshPath()
    {
        try
        {
            var exe = Environment.ProcessPath;
            if (exe is null)
                return;
            using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (run?.GetValue(ValueName) is string current && current != CommandFor(exe))
                run.SetValue(ValueName, CommandFor(exe));
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            // 고치지 못하면 그대로 둔다
        }
    }
}

// 마우스오버 설정을 켜고 끈다. 켜면 Windows 시작 때도 켜 두고, 끄면 그 항목도 지운다 (설정·알림 영역·소개 띠 공통)
internal static class HoverSetting
{
    public static void Apply(DrawerConfig config, bool on)
    {
        config.HoverOpen = on;
        Autostart.TrySet(on);
    }
}
