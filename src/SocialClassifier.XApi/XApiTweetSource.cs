using System.Net;
using System.Text.Json;
using SocialClassifier.Core;

namespace SocialClassifier.XApi;

public sealed class XApiOptions
{
    public string BaseUrl { get; set; } = "https://api.x.com/2";
    public string ConsumerKey { get; set; } = "";
    public string ConsumerSecret { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string AccessTokenSecret { get; set; } = "";
    /// <summary>Optional. If omitted, the id is fetched once from GET /2/users/me.</summary>
    public string? UserId { get; set; }
}

/// <summary>
/// Reads the user's own posts and replies through X API v2 <c>GET /2/users/:id/tweets</c>
/// (OAuth 1.0a user context). Date filtering is done server-side with start_time/end_time,
/// and the posts being replied to arrive in <c>includes.tweets</c> via the referenced_tweets.id expansion.
/// </summary>
/// <param name="from">Inclusive lower bound (UTC). Null = no lower bound.</param>
/// <param name="toExclusive">Exclusive upper bound (UTC). Null = no upper bound.</param>
public sealed class XApiTweetSource(
    HttpClient http, XApiOptions options, DateTimeOffset? from, DateTimeOffset? toExclusive,
    Action<string>? log = null) : ITweetSource
{
    private const int PageSize = 100;
    private readonly OAuthCredentials _creds = new(
        options.ConsumerKey, options.ConsumerSecret, options.AccessToken, options.AccessTokenSecret);

    public async Task<IReadOnlyList<TweetItem>> GetItemsAsync(CancellationToken ct)
    {
        var userId = options.UserId ?? await GetMyUserIdAsync(ct);
        var items = new List<TweetItem>();
        string? next = null;

        do
        {
            var query = new Dictionary<string, string>
            {
                ["max_results"] = PageSize.ToString(),
                ["exclude"] = "retweets",
                ["tweet.fields"] = "created_at,referenced_tweets,note_tweet",
                ["expansions"] = "referenced_tweets.id",
            };
            if (from is { } f) query["start_time"] = Iso(f);
            // X rejects an end_time too close to "now"; omit it when the range runs up to the present.
            if (toExclusive is { } t && t < DateTimeOffset.UtcNow.AddMinutes(-1)) query["end_time"] = Iso(t);
            if (next is not null) query["pagination_token"] = next;

            using var doc = await GetJsonAsync($"users/{userId}/tweets", query, ct);
            var page = ParsePage(doc.RootElement);
            items.AddRange(page);
            next = doc.RootElement.TryGetProperty("meta", out var meta)
                   && meta.TryGetProperty("next_token", out var nt) ? nt.GetString() : null;
            log?.Invoke($"  fetched {page.Count} posts (total {items.Count}){(next is null ? "" : ", more pages")}");
        } while (next is not null);

        return items.OrderBy(i => i.CreatedAt).ToList();
    }

    /// <summary>Maps one v2 timeline response (data + includes) to TweetItems. Public for testing.</summary>
    public static List<TweetItem> ParsePage(JsonElement root)
    {
        var result = new List<TweetItem>();
        if (!root.TryGetProperty("data", out var data)) return result; // empty page

        var included = new Dictionary<string, string>();
        if (root.TryGetProperty("includes", out var inc) && inc.TryGetProperty("tweets", out var incTweets))
            foreach (var t in incTweets.EnumerateArray())
                included[t.GetProperty("id").GetString()!] = TextOf(t);

        foreach (var t in data.EnumerateArray())
        {
            string? parentId = null;
            if (t.TryGetProperty("referenced_tweets", out var refs))
                foreach (var r in refs.EnumerateArray())
                    if (r.GetProperty("type").GetString() == "replied_to")
                        parentId = r.GetProperty("id").GetString();

            var parentText = parentId is not null && included.TryGetValue(parentId, out var pt) ? pt : null;
            result.Add(new TweetItem(
                t.GetProperty("id").GetString()!,
                DateTimeOffset.Parse(t.GetProperty("created_at").GetString()!, null,
                    System.Globalization.DateTimeStyles.AssumeUniversal),
                TextOf(t),
                parentId is null ? PostType.MyPost : PostType.MyReply,
                parentText));
        }
        return result;
    }

    // Posts over 280 characters carry their full text in note_tweet.text; "text" is truncated.
    private static string TextOf(JsonElement tweet) =>
        tweet.TryGetProperty("note_tweet", out var note) && note.TryGetProperty("text", out var nt)
            ? nt.GetString() ?? ""
            : tweet.GetProperty("text").GetString() ?? "";

    private static string Iso(DateTimeOffset d) => d.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");

    private async Task<string> GetMyUserIdAsync(CancellationToken ct)
    {
        using var doc = await GetJsonAsync("users/me", new Dictionary<string, string>(), ct);
        var id = doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
        log?.Invoke($"  authenticated user id {id}");
        return id;
    }

    private async Task<JsonDocument> GetJsonAsync(string path, Dictionary<string, string> query, CancellationToken ct)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/{path}";
        var qs = query.Count == 0 ? "" : "?" + string.Join("&", query.Select(q => $"{OAuth1.Encode(q.Key)}={OAuth1.Encode(q.Value)}"));

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url + qs);
            request.Headers.TryAddWithoutValidation("Authorization",
                OAuth1.BuildAuthorizationHeader(HttpMethod.Get, url, query, _creds));

            using var response = await http.SendAsync(request, ct);
            

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                var wait = RateLimitWait(response);
                log?.Invoke($"  rate limited, waiting {wait.TotalSeconds:0}s");
                await Task.Delay(wait, ct);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"X API {path} returned {(int)response.StatusCode}: {body}");
            return JsonDocument.Parse(body);
        }
    }

    internal static TimeSpan RateLimitWait(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("x-rate-limit-reset", out var v)
            && long.TryParse(v.FirstOrDefault(), out var reset))
        {
            var wait = DateTimeOffset.FromUnixTimeSeconds(reset) - DateTimeOffset.UtcNow;
            if (wait > TimeSpan.Zero) return wait + TimeSpan.FromSeconds(2);
        }
        return TimeSpan.FromSeconds(60);
    }
}
