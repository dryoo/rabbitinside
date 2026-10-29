namespace Rabbit.Models;

/// <summary>타일 정렬 기준입니다.</summary>
public enum SortMode
{
    /// <summary>이름 오름차순(탐색기와 같은 자연 정렬).</summary>
    Name,

    /// <summary>최근 실행한 순.</summary>
    RecentLaunch,

    /// <summary>많이 실행한 순.</summary>
    LaunchCount,

    /// <summary>설치 용량이 큰 순.</summary>
    Size,
}
