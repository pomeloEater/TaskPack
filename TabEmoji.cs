using System.Globalization;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskPack;

// 탭 아이콘으로 고를 수 있는 컬러 이모티콘(Microsoft Fluent Emoji 3D, MIT 라이선스 — assets\emoji\LICENSE).
// 목록 밖의 이모티콘은 직접 입력할 수 있고, 그림이 없어 글꼴로 그려서 단색으로 보인다
internal static class TabEmoji
{
    public sealed record Entry(string Emoji, string Slug, string Name);

    // 이모티콘 문자, assets\emoji 의 파일 이름(.png 제외), 풍선 도움말
    public static readonly IReadOnlyList<Entry> Catalog = new Entry[]
    {
        new("\U0001F4BC", "briefcase", "서류가방"),
        new("\U0001F4BB", "laptop", "노트북"),
        new("\U0001F5A5", "desktop-computer", "데스크톱"),
        new("⌨", "keyboard", "키보드"),
        new("\U0001F5B1", "computer-mouse", "마우스"),
        new("\U0001F4BE", "floppy-disk", "플로피 디스크"),
        new("\U0001F4C1", "file-folder", "폴더"),
        new("\U0001F4C2", "open-file-folder", "열린 폴더"),
        new("\U0001F5C2", "card-index-dividers", "색인 칸막이"),
        new("\U0001F4CB", "clipboard", "클립보드"),
        new("\U0001F4DD", "memo", "메모"),
        new("\U0001F4C4", "page-facing-up", "문서"),
        new("\U0001F516", "bookmark", "책갈피"),
        new("\U0001F4DA", "books", "책 더미"),
        new("\U0001F4D6", "open-book", "펼친 책"),
        new("\U0001F4F0", "newspaper", "신문"),
        new("\U0001F4C5", "calendar", "달력"),
        new("\U0001F4C8", "chart-increasing", "오르는 그래프"),
        new("\U0001F4CA", "bar-chart", "막대 그래프"),
        new("\U0001F4CC", "pushpin", "압정"),
        new("\U0001F4CE", "paperclip", "클립"),
        new("\U0001F517", "link", "링크"),
        new("\U0001F512", "locked", "잠금"),
        new("\U0001F511", "key", "열쇠"),
        new("\U0001F527", "wrench", "렌치"),
        new("\U0001F528", "hammer", "망치"),
        new("\U0001F6E0", "hammer-and-wrench", "망치와 렌치"),
        new("⚙", "gear", "톱니바퀴"),
        new("\U0001F529", "nut-and-bolt", "너트와 볼트"),
        new("\U0001F9F0", "toolbox", "공구 상자"),
        new("\U0001F50D", "magnifying-glass-tilted-left", "돋보기"),
        new("\U0001F4A1", "light-bulb", "전구"),
        new("\U0001F680", "rocket", "로켓"),
        new("⭐", "star", "별"),
        new("\U0001F525", "fire", "불"),
        new("\U0001F4E6", "package", "상자"),
        new("\U0001F4E5", "inbox-tray", "받은 편지함"),
        new("✉", "envelope", "편지"),
        new("\U0001F4AC", "speech-balloon", "말풍선"),
        new("\U0001F514", "bell", "종"),
        new("\U0001F30F", "globe-showing-asia-australia", "지구"),
        new("\U0001F3B5", "musical-note", "음표"),
        new("\U0001F3A7", "headphone", "헤드폰"),
        new("\U0001F3AE", "video-game", "게임"),
        new("\U0001F3A8", "artist-palette", "팔레트"),
        new("\U0001F4F7", "camera", "카메라"),
        new("\U0001F4B0", "money-bag", "돈 주머니"),
        new("\U0001F4B3", "credit-card", "신용카드"),
        new("\U0001F6D2", "shopping-cart", "장바구니"),
        new("\U0001F3E0", "house", "집"),
        new("\U0001F3E2", "office-building", "사무실"),
        new("\U0001F393", "graduation-cap", "학사모"),
        new("\U0001F9EA", "test-tube", "시험관"),
        new("\U0001F9E0", "brain", "뇌"),
        new("\U0001F916", "robot", "로봇"),
        new("❤", "red-heart", "하트"),
        new("✅", "check-mark-button", "확인"),
        new("⚠", "warning", "경고"),
        new("\U0001F9ED", "compass", "나침반"),
        new("\U0001F381", "wrapped-gift", "선물"),
        new("☕", "hot-beverage", "커피"),
        new("⏰", "alarm-clock", "알람 시계"),
        new("\U0001F9E9", "puzzle-piece", "퍼즐"),
        new("\U0001F3C6", "trophy", "트로피"),
        new("\U0001F3AF", "direct-hit", "과녁"),
        new("\U0001F331", "seedling", "새싹"),
        new("☁", "cloud", "구름"),
        new("⚡", "high-voltage", "번개"),
    };

    // 글자 모양 선택자(FE0E/FE0F)는 같은 이모티콘으로 보고 비교한다
    private static string Normalize(string emoji) => emoji.Replace("️", "").Replace("︎", "");

    public static Entry? Find(string? emoji) =>
        string.IsNullOrEmpty(emoji) ? null : Catalog.FirstOrDefault(e => Normalize(e.Emoji) == Normalize(emoji));

    // 입력에서 이모티콘 하나를 뽑는다. 이모티콘이 아니면 null (글자·숫자로 시작하면 거부)
    public static string? Extract(string? input)
    {
        var text = (input ?? "").Trim();
        if (text.Length == 0)
            return null;

        // 피부색·가족 같은 이어진 이모티콘도 한 글자(문자소)로 읽는다
        var first = StringInfo.GetNextTextElement(text);
        var rune = Rune.GetRuneAt(first, 0);
        var category = Rune.GetUnicodeCategory(rune);
        var isSymbol = category is UnicodeCategory.OtherSymbol or UnicodeCategory.MathSymbol or UnicodeCategory.ModifierSymbol
                       || rune.Value >= 0x1F000;
        return isSymbol && rune.Value >= 0x2190 ? first : null;
    }

    private static readonly Dictionary<string, BitmapImage> Images = new();

    private static BitmapImage? Load(string slug)
    {
        if (Images.TryGetValue(slug, out var cached))
            return cached;
        try
        {
            var image = new BitmapImage(new Uri($"pack://application:,,,/TaskPack;component/assets/emoji/{slug}.png"));
            image.Freeze();
            return Images[slug] = image;
        }
        catch (IOException)
        {
            return null;
        }
    }

    // 목록에 있는 이모티콘의 컬러 그림. 없으면 null
    public static ImageSource? Image(string? emoji) => Find(emoji) is { } entry ? Load(entry.Slug) : null;

    // 탭에 그릴 모양: 목록에 있으면 컬러 그림, 없으면 글꼴 글자(단색). 이모티콘이 없으면 null
    public static FrameworkElement? CreateVisual(string? emoji, double size)
    {
        if (string.IsNullOrEmpty(emoji))
            return null;

        if (Image(emoji) is { } source)
        {
            var image = new System.Windows.Controls.Image { Source = source, Width = size, Height = size };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            return image;
        }

        var glyph = new TextBlock
        {
            Text = emoji,
            FontFamily = new FontFamily("Segoe UI Emoji, Segoe UI Symbol"),
            FontSize = size * 0.85,
            Width = size,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        glyph.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush"); // 컬러 글꼴을 못 그려서 탭 글자색 한 가지로 보인다
        return glyph;
    }
}
