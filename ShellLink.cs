using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace TaskPack;

// Windows 바로가기(.lnk) 파일을 만들고 고친다.
// 앱 식별자(AppUserModelID)는 WScript.Shell로는 쓸 수 없어서 셸 COM 인터페이스를 직접 쓴다.
internal static class ShellLink
{
    private const int STGM_READ = 0x0;
    private const int STGM_READWRITE = 0x2;
    private const ushort VT_LPWSTR = 31;

    // PKEY_AppUserModel_ID
    private static readonly PropertyKey AppIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static void Create(string lnkPath, string target, string arguments,
        string iconPath, int iconIndex, string appId, string description)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            link.SetPath(target);
            link.SetArguments(arguments);
            link.SetWorkingDirectory(Path.GetDirectoryName(target) ?? "");
            link.SetIconLocation(iconPath, iconIndex);
            link.SetDescription(description);
            SetAppId((IPropertyStore)link, appId);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    public static void SetIcon(string lnkPath, string iconPath, int iconIndex)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, STGM_READWRITE);
            link.SetIconLocation(iconPath, iconIndex);
            ((IPersistFile)link).Save(lnkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    // 바로가기가 가리키는 대상 경로와 실행 인자. 읽을 수 없으면 null, 대상 경로가 없는 바로가기(설치 관리자형)는 Path가 빈 문자열
    public static (string Path, string Arguments)? ReadTarget(string lnkPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, STGM_READ);
            var path = new StringBuilder(1024);
            link.GetPath(path, path.Capacity, IntPtr.Zero, 0);
            var args = new StringBuilder(1024);
            link.GetArguments(args, args.Capacity);
            return (path.ToString(), args.ToString());
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    // 읽을 수 없는 바로가기는 null
    public static string? ReadAppId(string lnkPath)
    {
        var link = (IShellLinkW)new CShellLink();
        try
        {
            ((IPersistFile)link).Load(lnkPath, STGM_READ);
            var key = AppIdKey;
            ((IPropertyStore)link).GetValue(ref key, out var value);
            try
            {
                return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointer) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        catch (COMException)
        {
            return null;
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    private static void SetAppId(IPropertyStore store, string appId)
    {
        var key = AppIdKey;
        var value = new PropVariant { vt = VT_LPWSTR, pointer = Marshal.StringToCoTaskMemUni(appId) };
        try
        {
            store.SetValue(ref key, ref value);
            store.Commit();
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [DllImport("ole32.dll")]
    private static extern int PropVariantClear(ref PropVariant pvar);

    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    private class CShellLink { }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("000214F9-0000-0000-C000-000000000046")]
    private interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr findData, uint flags);
        void GetIDList(out IntPtr pidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        void GetHotkey(out short hotkey);
        void SetHotkey(short hotkey);
        void GetShowCmd(out int showCmd);
        void SetShowCmd(int showCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder iconPath, int cch, out int iconIndex);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string iconPath, int iconIndex);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string relativePath, uint reserved);
        void Resolve(IntPtr hwnd, uint flags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, InterfaceType(ComInterfaceType.InterfaceIsIUnknown), Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    private interface IPropertyStore
    {
        void GetCount(out uint count);
        void GetAt(uint index, out PropertyKey key);
        void GetValue(ref PropertyKey key, out PropVariant value);
        void SetValue(ref PropertyKey key, ref PropVariant value);
        void Commit();
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private readonly struct PropertyKey(Guid formatId, uint propertyId)
    {
        public readonly Guid FormatId = formatId;
        public readonly uint PropertyId = propertyId;
    }

    // 문자열 값만 쓰므로 필요한 필드만 둔다 (x64 기준 24바이트 크기 유지)
    [StructLayout(LayoutKind.Sequential)]
    private struct PropVariant
    {
        public ushort vt;
        private ushort reserved1, reserved2, reserved3;
        public IntPtr pointer;
        private IntPtr padding;
    }
}
