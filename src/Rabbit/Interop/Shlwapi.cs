using System.Runtime.InteropServices;

namespace Rabbit.Interop;

/// <summary>탐색기와 같은 방식으로 문자열을 비교하는 셸 API입니다.</summary>
internal static class Shlwapi
{
    /// <summary>
    /// 숫자를 숫자로 비교합니다("게임 2"가 "게임 10"보다 앞). 한글은 사전 순서대로 정렬됩니다.
    /// </summary>
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    public static extern int StrCmpLogicalW(string psz1, string psz2);
}
