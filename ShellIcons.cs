using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using static TaskPack.NativeMethods;

namespace TaskPack;

internal static class ShellIcons
{
    private static IntPtr _imageList;

    // 탐색기와 같은 아이콘(48px)을 가져온다. 바로가기 화살표는 붙이지 않는다. shell: 경로도 받는다.
    public static ImageSource? Get(string path)
    {
        var info = new SHFILEINFO();
        var size = (uint)Marshal.SizeOf<SHFILEINFO>();
        if (ShellItems.IsShellPath(path))
        {
            var pidl = ShellItems.Parse(path);
            if (pidl == IntPtr.Zero)
                return null;
            try
            {
                if (SHGetFileInfoPidl(pidl, 0, ref info, size, SHGFI_SYSICONINDEX | SHGFI_PIDL) == IntPtr.Zero)
                    return null;
            }
            finally
            {
                ILFree(pidl);
            }
        }
        else if (SHGetFileInfo(path, 0, ref info, size, SHGFI_SYSICONINDEX) == IntPtr.Zero)
        {
            return null;
        }

        if (_imageList == IntPtr.Zero)
        {
            var iid = IID_IImageList;
            if (SHGetImageList(SHIL_EXTRALARGE, ref iid, out _imageList) != 0)
                return null;
        }

        var hIcon = ImageList_GetIcon(_imageList, info.iIcon, ILD_TRANSPARENT);
        if (hIcon == IntPtr.Zero)
            return null;

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    // 탭·작업표시줄 아이콘 표시용. exe·dll은 그 파일의 아이콘, ico·그림은 가장 큰 이미지. 읽지 못하면 null
    public static ImageSource? LoadIconFile(string path)
    {
        if (!File.Exists(path))
            return null;
        if (IconFile.IsExecutable(path))
            return Get(path);
        try
        {
            var decoder = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
            frame.Freeze();
            return frame;
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException or FileFormatException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
