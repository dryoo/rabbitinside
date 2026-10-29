using System.Text.Json.Serialization;

namespace Rabbit.Models;

/// <summary>
/// Epic Games Store에서 지금 무료로 받을 수 있는 게임 하나입니다.
/// 끝나는 기한을 알아야 다음에 언제 확인할지 정할 수 있어서 설정 파일에 그대로 저장합니다.
/// </summary>
public sealed record FreeGameOffer(string Title, string Url, DateTimeOffset EndsAt)
{
    /// <summary>
    /// 이번 무료 배포 묶음을 알아보는 식별자입니다.
    /// 주소와 끝나는 시각이 같으면 같은 게임으로 보고, 새 게임이 나오면 달라집니다.
    /// </summary>
    [JsonIgnore]
    public string Key => $"{Url}#{EndsAt.UtcDateTime:O}";
}
