namespace TaskPack;

// 프로세스 사이에 주고받는 신호 이름. TASKPACK_DATA_DIR로 시험 중이면 꼬리표를 붙여, 실제 TaskPack과 신호가 섞이지 않게 한다
internal static class Signals
{
    private static readonly string Suffix =
        Environment.GetEnvironmentVariable("TASKPACK_DATA_DIR") is { Length: > 0 } ? "-test" : "";

    // 가방이 열려 있는 동안(포커스를 잃고 숨은 뒤 잠깐 포함) 세워 두는 표지. 받으면 가방을 닫는다 (작업표시줄 아이콘 재클릭)
    public static readonly string Drawer = @"Local\TaskPack.Drawer" + Suffix;

    // 상주 중인 TaskPack이 늘 세워 두는 표지. 받으면 가방을 연다
    public static readonly string Open = @"Local\TaskPack.Open" + Suffix;
}

// 시작할 때 무엇을 할지 정한다 (화면·시스템에 닿지 않는 판단만 모아 시험할 수 있게 했다)
public enum StartupAction
{
    CloseOpenBag,   // 열려 있는 가방에 닫으라고 알리고 끝낸다
    AskResident,    // 상주 중인 TaskPack에 가방을 열어 달라고 알리고 끝낸다
    ExitQuietly,    // 아무것도 하지 않고 끝낸다
    RunBag,         // 이 프로세스가 가방을 연다 (닫으면 프로그램도 끝)
    RunResident,    // 이 프로세스가 상주한다. openBag가 true면 가방도 바로 연다
}

public readonly record struct StartupPlan(StartupAction Action, bool OpenBag)
{
    // background: 자동 실행으로 뜬 경우(--background). 가방을 열지 않는다
    public static StartupPlan Decide(bool background, bool bagOpenSignalExists, bool residentExists, bool hoverOpen)
    {
        if (bagOpenSignalExists)
            return new(background ? StartupAction.ExitQuietly : StartupAction.CloseOpenBag, false);
        if (residentExists)
            return new(background ? StartupAction.ExitQuietly : StartupAction.AskResident, false);
        if (hoverOpen)
            return new(StartupAction.RunResident, !background);
        // 마우스오버가 꺼져 있으면 자동 실행으로 뜰 이유가 없다 (남은 자동 실행 항목)
        return background ? new(StartupAction.ExitQuietly, false) : new(StartupAction.RunBag, true);
    }
}
