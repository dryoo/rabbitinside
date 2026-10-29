using System.Runtime.InteropServices;
using System.Text;

namespace Rabbit.Interop;

internal static class Kernel32
{
    /// <summary>8.3 단축 경로를 원래의 긴 경로로 되돌립니다.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetLongPathName(
        string lpszShortPath,
        [Out] StringBuilder lpszLongPath,
        uint cchBuffer);
}
