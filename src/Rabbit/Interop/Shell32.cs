using System.Runtime.InteropServices;
using System.Text;

namespace Rabbit.Interop;

/// <summary>
/// 아이콘 추출에 쓰는 셸 API 선언입니다.
/// SHGetFileInfo는 .lnk를 대상 실행 파일로 해석해서 그 아이콘의 시스템 인덱스를 돌려줍니다.
/// </summary>
internal static class Shell32
{
    public const uint SHGFI_SYSICONINDEX = 0x000004000;
    public const uint SHGFI_PIDL = 0x000000008;

    // SHGetNameFromIDList에 넘기는 이름 형식
    public const int SIGDN_NORMALDISPLAY = 0x00000000;
    public const int SIGDN_DESKTOPABSOLUTEPARSING = unchecked((int)0x80028000);

    // 시스템 이미지 목록 크기
    public const int SHIL_LARGE = 0;
    public const int SHIL_SMALL = 1;
    public const int SHIL_EXTRALARGE = 2;
    public const int SHIL_JUMBO = 4;

    public const int ILD_TRANSPARENT = 0x00000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    /// <summary>PIDL로 아이콘 정보를 얻는 오버로드입니다. 가상 항목(스토어 앱)에 씁니다.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SHGetFileInfo(
        IntPtr pidl,
        uint dwFileAttributes,
        ref SHFILEINFO psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SHGetPathFromIDList(IntPtr pidl, StringBuilder pszPath);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHGetNameFromIDList(IntPtr pidl, int sigdnName, out IntPtr ppszName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern int SHParseDisplayName(
        string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll")]
    public static extern IntPtr ILCombine(IntPtr pidl1, IntPtr pidl2);

    [DllImport("shell32.dll")]
    public static extern void ILFree(IntPtr pidl);

    [DllImport("ole32.dll")]
    public static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("shell32.dll")]
    public static extern int SHGetImageList(int iImageList, ref Guid riid, out IImageList ppv);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// 시스템 이미지 목록입니다. COM 호출은 vtable 순서로 이뤄지므로, 실제로 호출하는
    /// GetIcon의 자리만 맞추면 앞쪽 메서드들은 선언만 해두면 됩니다.
    /// </summary>
    [ComImport]
    [Guid("46EB5926-582E-4017-9FDF-E8998DAA0950")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IImageList
    {
        void Add();
        void ReplaceIcon();
        void SetOverlayImage();
        void Replace();
        void AddMasked();
        void Draw();
        void Remove();

        [PreserveSig]
        int GetIcon(int i, int flags, out IntPtr picon);
    }
}
