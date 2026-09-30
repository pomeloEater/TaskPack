using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace TaskPack;

internal static class IconFile
{
    private const int MaxSize = 256;

    public const string DialogFilter = "아이콘·그림·프로그램|*.ico;*.png;*.jpg;*.jpeg;*.bmp;*.exe;*.dll|모든 파일|*.*";

    public static bool IsExecutable(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".exe" or ".dll";

    // 고른 아이콘을 쓸 수 있는 파일로 만든다. exe·dll은 그대로 가리키고, ico는 복사, 그림은 ico로 변환한다.
    // Windows는 아이콘을 경로 기준으로 기억하므로 매번 새 파일 이름(<prefix>-<시각>.ico)으로 만든다.
    public static string Import(string source, string destDir, string prefix)
    {
        if (IsExecutable(source))
            return source;

        Directory.CreateDirectory(destDir);
        var target = Path.Combine(destDir, $"{prefix}-{DateTime.Now:yyyyMMddHHmmssfff}.ico");
        if (Path.GetExtension(source).Equals(".ico", StringComparison.OrdinalIgnoreCase))
            File.Copy(source, target);
        else
            WriteFromImage(source, target);
        return target;
    }

    // Import로 만든 이전 아이콘 파일을 지운다 (destDir 안의 파일만)
    public static void DeleteImported(string? path, string destDir)
    {
        if (path is null || IsExecutable(path) || !File.Exists(path))
            return;
        if (!Path.GetFullPath(path).StartsWith(Path.GetFullPath(destDir) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return;
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    // png·jpg 같은 그림을 정사각형 ico로 만든다.
    // 긴 변을 256px 이하로 맞추고 가운데 정렬한 뒤, PNG 그대로 ico 안에 넣는다 (Windows Vista 이후 지원 형식).
    public static void WriteFromImage(string imagePath, string icoPath)
    {
        var decoder = BitmapDecoder.Create(new Uri(imagePath), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var image = decoder.Frames[0];

        int longest = Math.Max(image.PixelWidth, image.PixelHeight);
        int size = Math.Min(MaxSize, longest);
        double scale = (double)size / longest;
        double w = image.PixelWidth * scale, h = image.PixelHeight * scale;

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
            dc.DrawImage(image, new Rect((size - w) / 2, (size - h) / 2, w, h));

        var canvas = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        canvas.Render(visual);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(canvas));
        using var png = new MemoryStream();
        encoder.Save(png);

        using var output = new BinaryWriter(File.Create(icoPath));
        // ICONDIR
        output.Write((ushort)0);            // 예약
        output.Write((ushort)1);            // 종류: 아이콘
        output.Write((ushort)1);            // 이미지 개수
        // ICONDIRENTRY
        output.Write((byte)(size >= 256 ? 0 : size)); // 0은 256을 뜻함
        output.Write((byte)(size >= 256 ? 0 : size));
        output.Write((byte)0);              // 팔레트 색 수
        output.Write((byte)0);              // 예약
        output.Write((ushort)1);            // 색 평면
        output.Write((ushort)32);           // 픽셀당 비트
        output.Write((uint)png.Length);     // 이미지 크기
        output.Write((uint)22);             // 이미지 시작 위치 (6 + 16)
        png.WriteTo(output.BaseStream);
    }
}
