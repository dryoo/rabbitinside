using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Rabbit.Models;

/// <summary>DCInside 갤러리 종류에 따라 목록과 글 주소가 달라집니다.</summary>
public enum DcGalleryKind
{
    MinorGallery,
    MiniGallery,
    Gallery,
}

/// <summary>사용자가 추가한 DCInside 갤러리 한 곳입니다.</summary>
public sealed class GallerySource
{
    public string Name { get; set; } = string.Empty;

    public string GalleryId { get; set; } = string.Empty;

    public DcGalleryKind Kind { get; set; } = DcGalleryKind.MinorGallery;

    /// <summary>이 갤러리에서만 적용할 최소 추천 수입니다.</summary>
    public int MinimumRecommendations { get; set; }

    [JsonIgnore]
    public string Key => $"{Kind}:{GalleryId}";

    [JsonIgnore]
    public string KindLabel => Kind switch
    {
        DcGalleryKind.MiniGallery => "미니갤러리",
        DcGalleryKind.Gallery => "일반갤러리",
        _ => "마이너갤러리",
    };

    [JsonIgnore]
    public string DisplayName => string.Equals(Name, GalleryId, StringComparison.OrdinalIgnoreCase)
        ? $"{GalleryId} · {KindLabel}"
        : $"{Name} · {KindLabel}";

    public static GallerySource CreateDune()
    {
        return new GallerySource
        {
            Name = "듄 어웨이크닝",
            GalleryId = "duneawakening",
            Kind = DcGalleryKind.MinorGallery,
        };
    }

    /// <summary>
    /// 갤러리 ID나 갤러리 주소를 받아 저장 가능한 갤러리로 바꿉니다.
    /// 주소에 종류가 들어 있으면 선택한 종류보다 주소의 종류를 우선합니다.
    /// </summary>
    public static GallerySource? Parse(string value, DcGalleryKind fallbackKind)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return null;
        }

        var kind = fallbackKind;
        if (Regex.IsMatch(text, @"/mini(?:/|$)", RegexOptions.IgnoreCase))
        {
            kind = DcGalleryKind.MiniGallery;
        }
        else if (Regex.IsMatch(text, @"/mgallery(?:/|$)", RegexOptions.IgnoreCase))
        {
            kind = DcGalleryKind.MinorGallery;
        }
        else if (Regex.IsMatch(text, @"(?:gall\.dcinside\.com)?/(?:board|[^/?#]+/board)(?:/|$)", RegexOptions.IgnoreCase))
        {
            kind = DcGalleryKind.Gallery;
        }

        var idMatch = Regex.Match(text, @"[?&]id=([^&#\s]+)", RegexOptions.IgnoreCase);
        var id = idMatch.Success
            ? Uri.UnescapeDataString(idMatch.Groups[1].Value)
            : text;

        if (!Regex.IsMatch(id, @"^[A-Za-z0-9_]+$"))
        {
            return null;
        }

        return new GallerySource
        {
            Name = id,
            GalleryId = id,
            Kind = kind,
        };
    }

    public string ListUrl(int page = 1)
    {
        var prefix = Kind switch
        {
            DcGalleryKind.MiniGallery => "mini",
            DcGalleryKind.Gallery => "gallery",
            _ => "mgallery",
        };

        return $"https://gall.dcinside.com/{prefix}/board/lists/?id={Uri.EscapeDataString(GalleryId)}&page={page}";
    }

    public string ViewUrl(string postNumber)
    {
        var prefix = Kind switch
        {
            DcGalleryKind.MiniGallery => "mini",
            DcGalleryKind.Gallery => "gallery",
            _ => "mgallery",
        };

        return $"https://gall.dcinside.com/{prefix}/board/view/?id={Uri.EscapeDataString(GalleryId)}&no={Uri.EscapeDataString(postNumber)}";
    }
}

public sealed record GalleryKindOption(DcGalleryKind Kind, string Label);
