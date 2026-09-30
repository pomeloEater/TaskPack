using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace TaskPack;

// 가방 창의 색. 전체 테마(시스템/라이트/다크)와 가방별 테마(전체 따르기/시스템/라이트/다크/커스텀)를 합쳐 정한다
internal static class Theme
{
    public const string FollowSystem = "system";
    public const string Light = "light";
    public const string Dark = "dark";
    public const string Custom = "custom";

    public static readonly (string Label, string Value)[] GlobalModes =
    {
        ("시스템 설정 따라가기", FollowSystem),
        ("라이트", Light),
        ("다크", Dark),
    };

    // 가방별 선택지. null = 전체 설정 따르기
    public static readonly (string Label, string? Value)[] BagModes =
    {
        ("전체 설정", null),
        ("시스템", FollowSystem),
        ("라이트", Light),
        ("다크", Dark),
        ("커스텀", Custom),
    };

    // 커스텀 색 추천 목록
    public static readonly (string Name, string Hex)[] Presets =
    {
        ("빨강", "#E5484D"), ("주황", "#F76B15"), ("노랑", "#FFC53D"), ("초록", "#30A46C"),
        ("청록", "#12A594"), ("하늘", "#0090FF"), ("남색", "#3E63DD"), ("보라", "#8E4EC6"),
        ("분홍", "#D6409F"), ("갈색", "#AD7F58"), ("먹색", "#2B2D31"), ("회색", "#8B8D98"),
    };

    public const string DefaultCustomColor = "#12A594";

    private static readonly Color DarkBase = Color.FromRgb(0x26, 0x26, 0x26);
    private static readonly Color LightBase = Color.FromRgb(0xF7, 0xF7, 0xF7);
    private static readonly Color DefaultAccent = Color.FromRgb(0x12, 0xA5, 0x94); // 앱 아이콘의 청록

    private static bool SystemUsesLightTheme()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
        return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
    }

    public static Color? ParseColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
            return null;
        try { return (Color)ColorConverter.ConvertFromString(hex.Trim()); }
        catch (FormatException) { return null; }
    }

    // "#RRGGBB" 형식만 받는다 (직접 입력 검증용)
    public static string? NormalizeHex(string input)
    {
        var text = input.Trim();
        if (!text.StartsWith('#')) text = "#" + text;
        return text.Length == 7 && ParseColor(text) is { } c ? $"#{c.R:X2}{c.G:X2}{c.B:X2}" : null;
    }

    // 가방 창 자원(XAML의 DynamicResource)을 바꾼다
    public static void Apply(ResourceDictionary resources, string globalMode, Bag bag)
    {
        var mode = bag.Theme ?? globalMode;
        Color panel, accent;
        bool dark;
        if (mode == Custom && ParseColor(bag.Color) is { } custom)
        {
            // 커스텀: 가방 창 전체를 그 색으로. 글자·칸은 배경 밝기에 맞춰 흰색 또는 검은색
            panel = custom;
            dark = Luminance(custom) < 0.55;
            accent = dark ? Blend(custom, Colors.White, 0.55) : Blend(custom, Colors.Black, 0.45);
        }
        else
        {
            dark = mode switch { Light => false, Dark => true, _ => !SystemUsesLightTheme() };
            panel = dark ? DarkBase : LightBase;
            accent = DefaultAccent;
        }

        var ink = dark ? Colors.White : Colors.Black; // 칸·강조를 밝게(어두운 배경) 또는 어둡게(밝은 배경) 겹친다
        Set(resources, "PanelBrush", panel); // 불투명. 조금이라도 비치면 뒤 창의 글자가 보인다
        Set(resources, "PanelBorderBrush", WithAlpha(ink, dark ? 0x50 : 0x30));
        Set(resources, "SlotBrush", WithAlpha(ink, dark ? 0x16 : 0x0E));
        Set(resources, "SlotHoverBrush", WithAlpha(ink, dark ? 0x30 : 0x1E));
        Set(resources, "HoverBrush", WithAlpha(ink, dark ? 0x20 : 0x14));
        Set(resources, "SelectedTabBrush", WithAlpha(ink, dark ? 0x38 : 0x22));
        Set(resources, "TextBrush", dark ? Color.FromRgb(0xF0, 0xF0, 0xF0) : Color.FromRgb(0x1A, 0x1A, 0x1A));
        Set(resources, "SubtleTextBrush", WithAlpha(ink, 0x99));
        Set(resources, "AccentBrush", accent);
        // 강조색 위에 올리는 글자·아이콘 색 (강조색 밝기에 따라 흰색 또는 검은색)
        Set(resources, "AccentTextBrush", Luminance(accent) < 0.55 ? Colors.White : Color.FromRgb(0x1A, 0x1A, 0x1A));
    }

    // 설정 창 같은 대화상자용: 전체 테마로 색을 정하고, 어두우면 창 제목 표시줄도 어둡게 한다
    public static void ApplyToDialog(Window window, string globalMode)
    {
        Apply(window.Resources, globalMode, new Bag());
        MatchTitleBar(window);
    }

    // 창 색이 어두우면(글자가 밝으면) Windows 제목 표시줄도 어둡게 한다
    public static void MatchTitleBar(Window window)
    {
        var dark = window.Resources["TextBrush"] is SolidColorBrush text && text.Color.R > 0x80;
        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            var value = dark ? 1 : 0;
            NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref value, sizeof(int));
        };
    }

    // 가방 창의 지금 색을 대화상자에 그대로 옮긴다 (가방 위에 뜨는 작은 창용)
    public static void CopyColors(ResourceDictionary from, ResourceDictionary to)
    {
        foreach (var key in new[] { "PanelBrush", "PanelBorderBrush", "SlotBrush", "SlotHoverBrush", "HoverBrush",
                                    "SelectedTabBrush", "TextBrush", "SubtleTextBrush", "AccentBrush", "AccentTextBrush" })
            if (from.Contains(key))
                to[key] = from[key];
    }

    private static void Set(ResourceDictionary resources, string key, Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        resources[key] = brush;
    }

    // 사람 눈에 느껴지는 밝기 (0 어두움 ~ 1 밝음)
    private static double Luminance(Color c) => (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255;

    private static Color WithAlpha(Color c, int alpha) => Color.FromArgb((byte)alpha, c.R, c.G, c.B);

    private static Color Blend(Color from, Color to, double amount) => Color.FromRgb(
        (byte)Math.Round(from.R + (to.R - from.R) * amount),
        (byte)Math.Round(from.G + (to.G - from.G) * amount),
        (byte)Math.Round(from.B + (to.B - from.B) * amount));
}
