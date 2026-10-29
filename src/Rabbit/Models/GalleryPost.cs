using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace Rabbit.Models;

public enum GalleryImageState
{
    Pending,
    Loading,
    Ready,
    Missing,
}

public enum GallerySortMode
{
    Time,
    RecommendationGrade,
    WeightedViews,
}

public enum GalleryViewMode
{
    Cards,
    List,
}

/// <summary>DCInside 갤러리 목록의 글과 지연 로딩한 첫 사진입니다.</summary>
public sealed class GalleryPost : INotifyPropertyChanged
{
    private BitmapSource? _image;
    private GalleryImageState _imageState = GalleryImageState.Pending;
    private double _viewWeight;
    private double _recommendationRate;
    private string _recommendationGrade = "D";

    public required string Number { get; init; }

    public required string Title { get; init; }

    public required string Url { get; init; }

    public required string GalleryKey { get; init; }

    public required string GalleryId { get; init; }

    public required string GalleryName { get; init; }

    public required string GalleryKindLabel { get; init; }

    public required string Author { get; init; }

    public required string DateText { get; init; }

    public DateTimeOffset PublishedAt { get; init; }

    public int ViewCount { get; init; }

    public int RecommendCount { get; init; }

    /// <summary>DCInside 목록에서 사진 글(icon_pic)로 표시된 글인지 여부입니다.</summary>
    public bool HasImageHint { get; init; }

    public string UniqueKey => $"{GalleryKey}:{Number}";

    public string MobileUrl =>
        $"https://m.dcinside.com/board/{Uri.EscapeDataString(GalleryId)}/{Uri.EscapeDataString(Number)}";

    public string? ThumbnailUrl { get; set; }

    public BitmapSource? Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value))
            {
                return;
            }

            _image = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasImage));
        }
    }

    public GalleryImageState ImageState
    {
        get => _imageState;
        set
        {
            if (_imageState == value)
            {
                return;
            }

            _imageState = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ImageStatusText));
        }
    }

    public bool HasImage => Image is not null;

    public string ImageStatusText => ImageState switch
    {
        GalleryImageState.Loading => "사진 불러오는 중…",
        GalleryImageState.Missing => "사진 없음",
        _ => "사진",
    };

    /// <summary>같은 갤러리의 최고 조회수를 100으로 놓은 상대 조회수입니다.</summary>
    public double ViewWeight
    {
        get => _viewWeight;
        private set
        {
            if (Math.Abs(_viewWeight - value) < 0.0001)
            {
                return;
            }

            _viewWeight = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScoreText));
        }
    }

    /// <summary>조회수 대비 추천 수 비율입니다.</summary>
    public double RecommendationRate
    {
        get => _recommendationRate;
        private set
        {
            if (Math.Abs(_recommendationRate - value) < 0.0001)
            {
                return;
            }

            _recommendationRate = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ScoreText));
        }
    }

    public string RecommendationGrade
    {
        get => _recommendationGrade;
        private set
        {
            if (string.Equals(_recommendationGrade, value, StringComparison.Ordinal))
            {
                return;
            }

            _recommendationGrade = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RecommendText));
            OnPropertyChanged(nameof(RowScoreText));
        }
    }

    public string MetaText => $"{GalleryName} · {Author} · {DateText}";

    public string RecommendText => $"{RecommendationGrade} · 추천 {RecommendCount}";

    public string ScoreText =>
        $"조회 {ViewCount:N0} · 상대 {ViewWeight:0.#}% · 추천률 {RecommendationRate:0.#}%";

    public string RowScoreText =>
        $"{GalleryName} · 조회 {ViewCount:N0} · 추천 {RecommendCount:N0} · {RecommendationGrade}";

    public void UpdateScores(double maximumViewsInGallery)
    {
        ViewWeight = maximumViewsInGallery > 0
            ? ViewCount / maximumViewsInGallery * 100
            : 0;

        RecommendationRate = ViewCount > 0
            ? (double)RecommendCount / ViewCount * 100
            : 0;

        RecommendationGrade = RecommendationRate switch
        {
            >= 10 => "S",
            >= 5 => "A",
            >= 2 => "B",
            >= 1 => "C",
            _ => "D",
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
