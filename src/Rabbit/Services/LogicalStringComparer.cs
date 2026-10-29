using Rabbit.Interop;

namespace Rabbit.Services;

/// <summary>
/// 탐색기와 같은 순서로 이름을 정렬합니다. 기본 문자열 비교로는 "게임 2"가 "게임 10"보다
/// 뒤로 가는 문제가 있어서 셸의 StrCmpLogicalW를 그대로 씁니다.
/// </summary>
public sealed class LogicalStringComparer : IComparer<string>
{
    public static LogicalStringComparer Instance { get; } = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return Shlwapi.StrCmpLogicalW(x, y);
    }
}
