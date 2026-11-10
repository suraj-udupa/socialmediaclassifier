using System.Security.Cryptography;
using System.Text;

namespace SocialClassifier.XApi;

public sealed record OAuthCredentials(
    string ConsumerKey, string ConsumerSecret, string AccessToken, string AccessTokenSecret);

/// <summary>OAuth 1.0a (HMAC-SHA1) request signing for X API v1.1 user-context calls.</summary>
public static class OAuth1
{
    /// <param name="url">Base URL without the query string.</param>
    /// <param name="parameters">Query/form parameters that are part of the request (unencoded).</param>
    public static string BuildAuthorizationHeader(
        HttpMethod method, string url, IReadOnlyDictionary<string, string> parameters,
        OAuthCredentials creds, string? nonce = null, string? timestamp = null)
    {
        var oauth = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["oauth_consumer_key"] = creds.ConsumerKey,
            ["oauth_nonce"] = nonce ?? Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant(),
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
            ["oauth_token"] = creds.AccessToken,
            ["oauth_version"] = "1.0"
        };

        var signature = Sign(method, url, parameters, oauth, creds);

        var header = oauth.Append(new("oauth_signature", signature))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => $"{Encode(p.Key)}=\"{Encode(p.Value)}\"");
        return "OAuth " + string.Join(", ", header);
    }

    public static string Sign(
        HttpMethod method, string url, IReadOnlyDictionary<string, string> parameters,
        IReadOnlyDictionary<string, string> oauthParams, OAuthCredentials creds)
    {
        var all = parameters.Concat(oauthParams)
            .Select(p => (Key: Encode(p.Key), Value: Encode(p.Value)))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .ThenBy(p => p.Value, StringComparer.Ordinal)
            .Select(p => $"{p.Key}={p.Value}");

        var baseString = $"{method.Method.ToUpperInvariant()}&{Encode(url)}&{Encode(string.Join("&", all))}";
        var key = $"{Encode(creds.ConsumerSecret)}&{Encode(creds.AccessTokenSecret)}";

        using var hmac = new HMACSHA1(Encoding.ASCII.GetBytes(key));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.ASCII.GetBytes(baseString)));
    }

    /// <summary>RFC 3986 percent-encoding (unreserved characters left as-is).</summary>
    public static string Encode(string value) => Uri.EscapeDataString(value);
}
