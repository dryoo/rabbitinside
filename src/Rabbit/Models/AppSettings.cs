namespace Rabbit.Models;

/// <summary>목록과 무관한 앱 설정입니다.</summary>
public sealed class AppSettings
{
    public SortMode SortMode { get; set; } = SortMode.Name;

    /// <summary>마지막 창 위치와 크기입니다. 없으면 기본값으로 엽니다.</summary>
    public double? WindowLeft { get; set; }

    public double? WindowTop { get; set; }

    public double? WindowWidth { get; set; }

    public double? WindowHeight { get; set; }

    public bool WindowMaximized { get; set; }

    /// <summary>
    /// 사용자가 닫은 무료 게임 알림의 식별자입니다.
    /// 지금 떠 있는 묶음과 같으면 다시 띄우지 않고, 새 게임이 나오면 값이 달라져서 다시 뜹니다.
    /// </summary>
    public string? DismissedFreeGameKey { get; set; }

    /// <summary>
    /// 무료 게임을 물어볼 국가 코드입니다(예: "KR"). 비워 두면 Windows 지역 설정을 따릅니다.
    /// 스토어는 지역마다 무료 목록이 달라서, 보고 싶은 지역이 따로 있을 때만 적어 둡니다.
    /// </summary>
    public string? FreeGameCountry { get; set; }

    /// <summary>
    /// 마지막으로 확인한 무료 게임 목록입니다.
    /// 끝나는 기한을 함께 저장해 두었다가, 창을 켰을 때 그 기한이 아직 남아 있으면 스토어에 다시 묻지 않습니다.
    /// </summary>
    public List<FreeGameOffer> FreeGames { get; set; } = [];

    /// <summary>DC 갤러리 탭에서 선택할 수 있는 갤러리 목록입니다.</summary>
    public List<GallerySource> GallerySources { get; set; } = [GallerySource.CreateDune()];

    /// <summary>마지막으로 보던 갤러리를 다음 실행에서 다시 엽니다.</summary>
    public string? SelectedGalleryKey { get; set; }

    /// <summary>
    /// 예전 버전의 전역 최소 추천 수입니다. 새 버전은 GallerySource별 값을 쓰며,
    /// 처음 한 번만 선택된 갤러리로 옮깁니다.
    /// </summary>
    public int GalleryMinimumRecommendations { get; set; }

    public GallerySortMode GallerySortMode { get; set; } = GallerySortMode.Time;

    public GalleryViewMode GalleryViewMode { get; set; } = GalleryViewMode.Cards;
}
