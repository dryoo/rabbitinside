namespace Rabbit.Models;

public sealed record GalleryContentBlock(string? Text, string? ImageUrl);

public sealed record GalleryPostDetail(
    string Title,
    string Author,
    string DateText,
    int ViewCount,
    int RecommendCount,
    IReadOnlyList<GalleryContentBlock> Blocks);
