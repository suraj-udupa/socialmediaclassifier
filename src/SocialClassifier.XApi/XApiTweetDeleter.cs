using System.Net;
using System.Text.Json;
using SocialClassifier.Core;

namespace SocialClassifier.XApi;

/// <summary>
/// Deletes the authenticated user's own posts with <c>DELETE /2/tweets/:id</c> (OAuth 1.0a user context).
/// The app needs "Read and write" permission, otherwise X answers 403.
/// </summary>
public sealed class XApiTweetDeleter(HttpClient http, XApiOptions options, Action<string>? log = null) : ITweetDeleter
{
    private readonly OAuthCredentials _creds = new(
        options.ConsumerKey, options.ConsumerSecret, options.AccessToken, options.AccessTokenSecret);

    public async Task<DeleteOutcome> DeleteAsync(string id, CancellationToken ct)
    {
        var url = $"{options.BaseUrl.TrimEnd('/')}/tweets/{id}";

        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Delete, url);
            request.Headers.TryAddWithoutValidation("Authorization",
                OAuth1.BuildAuthorizationHeader(HttpMethod.Delete, url, new Dictionary<string, string>(), _creds));

            using var response = await http.SendAsync(request, ct);

            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < 3)
            {
                var wait = XApiTweetSource.RateLimitWait(response);
                log?.Invoke($"  rate limited, waiting {wait.TotalSeconds:0}s");
                await Task.Delay(wait, ct);
                continue;
            }

            var body = await response.Content.ReadAsStringAsync(ct);
            if (response.StatusCode == HttpStatusCode.NotFound) return DeleteOutcome.AlreadyGone;
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"X API DELETE tweets/{id} returned {(int)response.StatusCode}: {body}");

            return ParseResult(id, body);
        }
    }

    /// <summary>Reads <c>{"data":{"deleted":true}}</c>. Public for testing.</summary>
    public static DeleteOutcome ParseResult(string id, string body)
    {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("deleted", out var d) && d.ValueKind == JsonValueKind.True)
            return DeleteOutcome.Deleted;
        throw new HttpRequestException($"X API did not confirm deletion of {id}: {body}");
    }
}
