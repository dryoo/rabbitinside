using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using Rabbit.Models;

namespace Rabbit.Services;

/// <summary>
/// DCInside 갤러리의 공개 HTML을 읽어 글 목록과 첫 사진을 가져옵니다.
/// 외부 패키지를 쓰지 않고 목록 행과 이미지 메타데이터만 읽습니다.
/// </summary>
public static partial class DcGalleryClient
{
    private static readonly HttpClient Client = CreateClient();

    public static async Task<IReadOnlyList<GalleryPost>> FetchPostsAsync(
        GallerySource source,
        int page,
        CancellationToken token = default)
    {
        var html = await Client.GetStringAsync(source.ListUrl(page), token).ConfigureAwait(false);
        return ParsePosts(html, source);
    }

    /// <summary>글 상세 페이지의 대표 이미지(첫 이미지)를 내려받아 화면용 비트맵으로 만듭니다.</summary>
    public static async Task<(string? ImageUrl, BitmapSource? Image)> FetchFirstImageAsync(
        GalleryPost post,
        CancellationToken token = default)
    {
        var html = await Client.GetStringAsync(post.Url, token).ConfigureAwait(false);
        var imageUrl = FindFirstImageUrl(html, post.Url);
        if (imageUrl is null)
        {
            return (null, null);
        }

        var image = await FetchImageAsync(imageUrl, post.Url, 480, token).ConfigureAwait(false);
        return (imageUrl, image);
    }

    public static async Task<GalleryPostDetail> FetchPostDetailAsync(
        GalleryPost post,
        CancellationToken token = default)
    {
        var html = await Client.GetStringAsync(post.Url, token).ConfigureAwait(false);
        return ParsePostDetail(html, post);
    }

    public static async Task<BitmapSource?> FetchImageAsync(
        string imageUrl,
        string referer,
        int decodePixelWidth,
        CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
        request.Headers.Referrer = new Uri(referer);
        request.Headers.Accept.ParseAdd("image/avif,image/webp,image/apng,image/*,*/*;q=0.8");

        using var response = await Client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null &&
            mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var memory = new MemoryStream();
        var buffer = new byte[64 * 1024];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(buffer, token).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > 16 * 1024 * 1024)
            {
                return null;
            }

            await memory.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
        }

        memory.Position = 0;
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
        bitmap.DecodePixelWidth = Math.Max(1, decodePixelWidth);
        bitmap.StreamSource = memory;
        bitmap.EndInit();
        bitmap.Freeze();

        return bitmap;
    }

    internal static IReadOnlyList<GalleryPost> ParsePosts(string html, GallerySource source)
    {
        var posts = new List<GalleryPost>();

        foreach (Match rowMatch in PostRowRegex().Matches(html))
        {
            var row = rowMatch.Value;
            var rowAttributes = rowMatch.Groups["attributes"].Value;

            // DCInside의 공지 표식이 있는 행은 일반 글 목록에서 제외합니다.
            if (rowAttributes.Contains("icon_notice", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var numberMatch = PostNumberRegex().Match(rowAttributes);
            if (!numberMatch.Success)
            {
                continue;
            }

            var titleCellMatch = TitleCellRegex().Match(row);
            if (!titleCellMatch.Success)
            {
                continue;
            }

            var titleHtml = titleCellMatch.Groups["html"].Value;
            var titleAnchor = FindTitleAnchor(titleHtml);
            if (titleAnchor is null)
            {
                continue;
            }

            var title = CleanText(titleAnchor.Groups["text"].Value);
            if (title.Length == 0)
            {
                continue;
            }

            var number = numberMatch.Groups[1].Value;
            var recommendMatch = RecommendCellRegex().Match(row);
            var dateMatch = DateCellRegex().Match(row);
            var viewMatch = ViewCellRegex().Match(row);
            var authorMatch = AuthorRegex().Match(row);
            var hasImageHint =
                rowAttributes.Contains("icon_pic", StringComparison.OrdinalIgnoreCase) ||
                titleHtml.Contains("icon_pic", StringComparison.OrdinalIgnoreCase);
            var publishedText = dateMatch.Success
                ? (AttributeValue(dateMatch.Groups[1].Value, "title") ??
                   CleanText(dateMatch.Groups[2].Value))
                : null;

            posts.Add(new GalleryPost
            {
                Number = number,
                Title = title,
                Url = source.ViewUrl(number),
                GalleryKey = source.Key,
                GalleryId = source.GalleryId,
                GalleryName = source.Name,
                GalleryKindLabel = source.KindLabel,
                Author = authorMatch.Success
                    ? CleanText(WebUtility.HtmlDecode(authorMatch.Groups[1].Value))
                    : "익명",
                DateText = publishedText ?? string.Empty,
                PublishedAt = ParsePublishedAt(publishedText),
                ViewCount = ParseCount(viewMatch.Success ? viewMatch.Groups[1].Value : null),
                RecommendCount = ParseCount(recommendMatch.Success ? recommendMatch.Groups[1].Value : null),
                HasImageHint = hasImageHint,
                ImageState = hasImageHint
                    ? GalleryImageState.Pending
                    : GalleryImageState.Missing,
            });
        }

        return posts;
    }

    private static DateTimeOffset ParsePublishedAt(string? value)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var parsed)
            ? parsed
            : DateTimeOffset.MinValue;
    }

    internal static GalleryPostDetail ParsePostDetail(string html, GalleryPost post)
    {
        var blocks = new List<GalleryContentBlock>();
        var bodyMatch = BodyStartRegex().Match(html);

        if (bodyMatch.Success)
        {
            var bodyStart = bodyMatch.Index + bodyMatch.Length;
            var tail = html[bodyStart..];
            var bodyEnd = BodyEndMarkerRegex().Match(tail);
            var bodyHtml = bodyEnd.Success
                ? tail[..bodyEnd.Index]
                : tail[..Math.Min(tail.Length, 300_000)];

            bodyHtml = ScriptStyleRegex().Replace(bodyHtml, string.Empty);
            var imageUrls = new List<string>();

            bodyHtml = ImageSourceRegex().Replace(bodyHtml, match =>
            {
                if (imageUrls.Count >= 20 ||
                    NormalizeImageUrl(match.Groups["src"].Value, post.Url) is not { } imageUrl ||
                    !IsUsableImageUrl(imageUrl))
                {
                    return " ";
                }

                imageUrls.Add(imageUrl);
                return $"\n[[RABBIT_IMAGE_{imageUrls.Count - 1}]]\n";
            });

            bodyHtml = BlockTagRegex().Replace(bodyHtml, "\n");
            bodyHtml = AnyTagRegex().Replace(bodyHtml, " ");
            var text = WebUtility.HtmlDecode(bodyHtml);

            var cursor = 0;
            foreach (Match marker in ImageMarkerRegex().Matches(text))
            {
                AddTextBlocks(blocks, text[cursor..marker.Index]);

                if (int.TryParse(marker.Groups[1].Value, out var imageIndex) &&
                    imageIndex >= 0 &&
                    imageIndex < imageUrls.Count)
                {
                    blocks.Add(new GalleryContentBlock(null, imageUrls[imageIndex]));
                }

                cursor = marker.Index + marker.Length;
            }

            AddTextBlocks(blocks, text[cursor..]);
        }

        if (!blocks.Any(block => block.ImageUrl is not null) &&
            FindFirstImageUrl(html, post.Url) is { } fallbackImage)
        {
            blocks.Add(new GalleryContentBlock(null, fallbackImage));
        }

        if (!blocks.Any(block => !string.IsNullOrWhiteSpace(block.Text)))
        {
            blocks.Add(new GalleryContentBlock("본문 내용을 읽지 못했습니다.", null));
        }

        return new GalleryPostDetail(
            post.Title,
            post.Author,
            post.DateText,
            post.ViewCount,
            post.RecommendCount,
            blocks);
    }

    private static void AddTextBlocks(List<GalleryContentBlock> blocks, string text)
    {
        foreach (var line in Regex.Split(text, @"\r?\n"))
        {
            var cleaned = Regex.Replace(WebUtility.HtmlDecode(line), @"[ \t\f\v]+", " ").Trim();
            if (cleaned.Length > 0)
            {
                blocks.Add(new GalleryContentBlock(cleaned, null));
            }
        }
    }

    internal static string? FindFirstImageUrl(string html, string postUrl)
    {
        var candidates = new List<string>();

        foreach (Match match in MetaTagRegex().Matches(html))
        {
            var attributes = match.Groups["attributes"].Value;
            var property = AttributeValue(attributes, "property") ??
                           AttributeValue(attributes, "name");

            if (string.Equals(property, "og:image", StringComparison.OrdinalIgnoreCase) &&
                AttributeValue(attributes, "content") is { Length: > 0 } content)
            {
                candidates.Add(content);
            }
        }

        var bodyStart = html.IndexOf("class=\"write_div\"", StringComparison.OrdinalIgnoreCase);
        if (bodyStart < 0)
        {
            bodyStart = Math.Max(0, html.IndexOf("write_div", StringComparison.OrdinalIgnoreCase));
        }

        if (bodyStart >= 0)
        {
            var bodyEnd = html.IndexOf("class=\"gall_comment", bodyStart, StringComparison.OrdinalIgnoreCase);
            if (bodyEnd < 0)
            {
                bodyEnd = Math.Min(html.Length, bodyStart + 250_000);
            }

            var body = html[bodyStart..bodyEnd];
            foreach (Match match in ImageSourceRegex().Matches(body))
            {
                candidates.Add(match.Groups["src"].Value);
            }
        }

        foreach (var candidate in candidates)
        {
            if (NormalizeImageUrl(candidate, postUrl) is { } imageUrl && IsUsableImageUrl(imageUrl))
            {
                return imageUrl;
            }
        }

        return null;
    }

    private static HttpClient CreateClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            CookieContainer = new CookieContainer(),
            UseCookies = true,
        };

        var client = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25),
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36");
        client.DefaultRequestHeaders.Referrer = new Uri("https://gall.dcinside.com/");
        client.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/xhtml+xml");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ko-KR,ko;q=0.9,en;q=0.7");

        return client;
    }

    private static Match? FindTitleAnchor(string cellHtml)
    {
        foreach (Match match in AnchorRegex().Matches(cellHtml))
        {
            var attributes = match.Groups["attributes"].Value;
            if (attributes.Contains("reply_numbox", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!attributes.Contains("href", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return match;
        }

        return null;
    }

    private static string? AttributeValue(string tagAttributes, string name)
    {
        var match = Regex.Match(
            tagAttributes,
            $@"\b{Regex.Escape(name)}\s*=\s*(?:""(?<value>[^""]*)""|'(?<value>[^']*)')",
            RegexOptions.IgnoreCase);

        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static string? NormalizeImageUrl(string value, string postUrl)
    {
        var text = WebUtility.HtmlDecode(value.Trim());
        if (text.StartsWith("//", StringComparison.Ordinal))
        {
            text = "https:" + text;
        }

        if (!Uri.TryCreate(new Uri(postUrl), text, out var uri) ||
            uri.Scheme is not ("http" or "https"))
        {
            return null;
        }

        return uri.AbsoluteUri;
    }

    private static bool IsUsableImageUrl(string url)
    {
        return !Regex.IsMatch(
            url,
            @"(?:logo|noimage|no_image|loading|blank|icon|btn_|ad(?:_|\.)|profile|emoticon)",
            RegexOptions.IgnoreCase);
    }

    private static int ParseCount(string? value)
    {
        var digits = Regex.Replace(WebUtility.HtmlDecode(value ?? string.Empty), "[^0-9]", string.Empty);
        return int.TryParse(digits, out var number) ? number : 0;
    }

    private static string CleanText(string value)
    {
        var withoutTags = Regex.Replace(value, "<[^>]+>", " ");
        var decoded = WebUtility.HtmlDecode(withoutTags);
        return Regex.Replace(decoded, @"\s+", " ").Trim();
    }

    [GeneratedRegex(
        @"<tr\b(?<attributes>[^>]*\bclass\s*=\s*[""'][^""']*\bus-post\b[^""']*[""'][^>]*)>(?<row>.*?)</tr>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex PostRowRegex();

    [GeneratedRegex(@"\bdata-no\s*=\s*[""'](\d+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex PostNumberRegex();

    [GeneratedRegex(
        @"<td\b[^>]*\bclass\s*=\s*[""'][^""']*\bgall_tit\b[^""']*[""'][^>]*>(?<html>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex TitleCellRegex();

    [GeneratedRegex(@"<a\b(?<attributes>[^>]*)>(?<text>.*?)</a>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex AnchorRegex();

    [GeneratedRegex(
        @"<td\b[^>]*\bclass\s*=\s*[""'][^""']*\bgall_recommend\b[^""']*[""'][^>]*>(.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex RecommendCellRegex();

    [GeneratedRegex(
        @"<td\b(?<attributes>[^>]*\bclass\s*=\s*[""'][^""']*\bgall_date\b[^""']*[""'][^>]*)>(?<text>.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex DateCellRegex();

    [GeneratedRegex(
        @"<td\b[^>]*\bclass\s*=\s*[""'][^""']*\bgall_count\b[^""']*[""'][^>]*>(.*?)</td>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ViewCellRegex();

    [GeneratedRegex(
        @"\bdata-nick\s*=\s*[""']([^""']*)[""']",
        RegexOptions.IgnoreCase)]
    private static partial Regex AuthorRegex();

    [GeneratedRegex(@"<meta\b(?<attributes>[^>]*)>", RegexOptions.IgnoreCase)]
    private static partial Regex MetaTagRegex();

    [GeneratedRegex(
        @"<div\b(?=[^>]*\bclass\s*=\s*[""'][^""']*\bwrite_div\b[^""']*[""'])[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BodyStartRegex();

    [GeneratedRegex(
        @"<(?:div|section)\b[^>]*\bclass\s*=\s*[""'][^""']*(?:appending_file_box|gall_comment|comment_box)[^""']*[""'][^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BodyEndMarkerRegex();

    [GeneratedRegex(
        @"<(?:script|style)\b[^>]*>.*?</(?:script|style)>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptStyleRegex();

    [GeneratedRegex(
        @"</?(?:p|div|br|li|tr|h[1-6]|blockquote)\b[^>]*>",
        RegexOptions.IgnoreCase)]
    private static partial Regex BlockTagRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex AnyTagRegex();

    [GeneratedRegex(@"\[\[RABBIT_IMAGE_(\d+)\]\]")]
    private static partial Regex ImageMarkerRegex();

    [GeneratedRegex(
        @"<img\b[^>]*\bsrc\s*=\s*(?:""(?<src>[^""]+)""|'(?<src>[^']+)')[^>]*>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ImageSourceRegex();
}
