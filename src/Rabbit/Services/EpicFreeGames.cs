using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using Rabbit.Models;

namespace Rabbit.Services;

/// <summary>
/// Epic Games Store에서 지금 무료로 받을 수 있는 게임을 찾습니다.
/// 스토어 화면이 쓰는 공개 주소를 그대로 읽으므로 로그인이나 API 키가 필요 없습니다.
/// </summary>
public static class EpicFreeGames
{
    private const string Endpoint = "https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions";

    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>
    /// 지역과 언어를 기준으로 무료 배포 중인 게임을 가져옵니다.
    /// 무료가 아니거나 그 지역에서 판매하지 않는 게임은 목록에 들어오지 않습니다.
    /// </summary>
    public static async Task<IReadOnlyList<FreeGameOffer>> FetchAsync(
        string locale,
        string country,
        CancellationToken token = default)
    {
        var url = $"{Endpoint}?locale={locale}&country={country}&allowCountries={country}";

        using var stream = await Client.GetStreamAsync(url, token).ConfigureAwait(false);

        return await ParseAsync(stream, locale, DateTimeOffset.UtcNow, token).ConfigureAwait(false);
    }

    /// <summary>스토어가 돌려준 JSON에서 지금 무료인 게임만 골라냅니다.</summary>
    public static async Task<IReadOnlyList<FreeGameOffer>> ParseAsync(
        Stream json,
        string locale,
        DateTimeOffset now,
        CancellationToken token = default)
    {
        using var document = await JsonDocument.ParseAsync(json, cancellationToken: token).ConfigureAwait(false);

        if (!TryGetPath(document.RootElement, out var elements, "data", "Catalog", "searchStore", "elements"))
        {
            return [];
        }

        var language = Language(locale);
        var offers = new List<FreeGameOffer>();

        foreach (var element in elements.EnumerateArray())
        {
            if (!TryGetCurrentEnd(element, now, out var endsAt))
            {
                continue;
            }

            var title = Text(element, "title");
            var slug = ResolveSlug(element);

            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(slug))
            {
                continue;
            }

            offers.Add(new FreeGameOffer(title, $"https://store.epicgames.com/{language}/p/{slug}", endsAt));
        }

        // 먼저 끝나는 게임부터 보여줍니다.
        offers.Sort((left, right) => left.EndsAt == right.EndsAt
            ? string.Compare(left.Title, right.Title, StringComparison.CurrentCulture)
            : left.EndsAt.CompareTo(right.EndsAt));

        return offers;
    }

    /// <summary>지금 진행 중인 무료 배포의 끝나는 시각을 찾습니다. 할인율 0%가 "무료"입니다.</summary>
    private static bool TryGetCurrentEnd(JsonElement element, DateTimeOffset now, out DateTimeOffset endsAt)
    {
        endsAt = default;

        if (!TryGetPath(element, out var groups, "promotions", "promotionalOffers"))
        {
            return false;
        }

        foreach (var group in groups.EnumerateArray())
        {
            if (!TryGetPath(group, out var items, "promotionalOffers"))
            {
                continue;
            }

            foreach (var item in items.EnumerateArray())
            {
                if (!TryGetPath(item, out var discount, "discountSetting") ||
                    !TryGet(discount, "discountPercentage", out var percentage) ||
                    percentage.ValueKind != JsonValueKind.Number ||
                    percentage.GetDouble() != 0)
                {
                    continue;
                }

                var start = Time(item, "startDate");
                var end = Time(item, "endDate");

                if (start is null || end is null || start > now || end < now)
                {
                    continue;
                }

                endsAt = end.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 스토어 주소에 쓸 주소 조각을 찾습니다. 화면 주소(productHome)를 먼저 보고,
    /// 없으면 판매 항목 주소(offer), 그것도 없으면 목록에 있는 주소를 씁니다.
    /// </summary>
    private static string? ResolveSlug(JsonElement element)
    {
        if (TryGetPath(element, out var mappings, "catalogNs", "mappings") &&
            FindSlug(mappings, "productHome") is { } productSlug)
        {
            return productSlug;
        }

        if (TryGetPath(element, out var offerMappings, "offerMappings") &&
            FindSlug(offerMappings, "offer") is { } offerSlug)
        {
            return offerSlug;
        }

        return Text(element, "urlSlug") is { Length: > 0 } fallback ? fallback : null;
    }

    private static string? FindSlug(JsonElement mappings, string pageType)
    {
        foreach (var mapping in mappings.EnumerateArray())
        {
            if (Text(mapping, "pageType") == pageType &&
                Text(mapping, "pageSlug") is { Length: > 0 } slug)
            {
                return slug;
            }
        }

        return null;
    }

    /// <summary>스토어 주소에 들어가는 언어 코드입니다(ko-KR → ko).</summary>
    private static string Language(string locale)
    {
        var language = locale.Split('-')[0].ToLowerInvariant();

        return language.Length > 0 ? language : "en";
    }

    private static DateTimeOffset? Time(JsonElement parent, string name)
    {
        if (Text(parent, name) is not { } value)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var parsed)
            ? parsed
            : null;
    }

    private static string? Text(JsonElement parent, string name) =>
        TryGet(parent, name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>
    /// 이름으로 값을 찾습니다. 스토어 응답은 같은 뜻의 키에도 대소문자가 섞여 있어서
    /// (data / Catalog / searchStore) 정확히 맞는 키를 먼저 보고, 없으면 대소문자를 무시하고 찾습니다.
    /// </summary>
    private static bool TryGet(JsonElement parent, string name, out JsonElement value)
    {
        value = default;

        if (parent.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (parent.TryGetProperty(name, out value))
        {
            return true;
        }

        foreach (var property in parent.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>중첩된 JSON 값을 경로로 따라가며 찾습니다. 하나라도 없으면 false입니다.</summary>
    private static bool TryGetPath(JsonElement root, out JsonElement found, params string[] path)
    {
        found = root;

        foreach (var name in path)
        {
            if (!TryGet(found, name, out var next))
            {
                found = default;
                return false;
            }

            found = next;
        }

        return found.ValueKind is JsonValueKind.Object or JsonValueKind.Array;
    }
}
