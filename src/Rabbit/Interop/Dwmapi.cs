using System.Runtime.InteropServices;

namespace Rabbit.Interop;

internal static class Dwmapi
{
    // Windows 10 1809 이하와 1903 이상에서 값이 다릅니다. 둘 다 시도합니다.
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE_OLD = 19;
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

    /// <summary>제목 표시줄을 다크로 그리게 합니다.</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
