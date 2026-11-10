using System.Text.Json;
using SocialClassifier.Core;
using SocialClassifier.XApi;
using Xunit;

namespace SocialClassifier.Tests;

public class XApiTests
{
    // Worked example from X's "Creating a signature" documentation.
    [Fact]
    public void OAuth1_signature_matches_documented_example()
    {
        var creds = new OAuthCredentials(
            "xvz1evFS4wEEPTGEFPHBog",
            "kAcSOqF21Fu85e7zjz7ZN2U4ZRhfV3WpwPAoE3Z7kBw",
            "370773112-GmHxMAgYyLbNEtIKZeRNFsMKPR9EyMZeS9weJAEb",
            "LswwdoUaIvS8ltyTt5jkRh4J50vUPVVHtR2YPi5kE");

        var oauth = new Dictionary<string, string>
        {
            ["oauth_consumer_key"] = creds.ConsumerKey,
            ["oauth_nonce"] = "kYjzVBB8Y0ZFabxSWbWovY3uYSQ2pTgmZeNu2VS4cg",
            ["oauth_signature_method"] = "HMAC-SHA1",
            ["oauth_timestamp"] = "1318622958",
            ["oauth_token"] = creds.AccessToken,
            ["oauth_version"] = "1.0"
        };
        var parameters = new Dictionary<string, string>
        {
            ["status"] = "Hello Ladies + Gentlemen, a signed OAuth request!",
            ["include_entities"] = "true"
        };

        var sig = OAuth1.Sign(HttpMethod.Post, "https://api.twitter.com/1/statuses/update.json",
            parameters, oauth, creds);

        Assert.Equal("tnnArxj06cWHq44gCs1OSKk/jLY=", sig);
    }

    [Fact]
    public void ParsePage_maps_posts_replies_and_parent_text()
    {
        const string json = """
        {
          "data": [
            { "id": "20", "text": "my reply", "created_at": "2026-10-05T10:00:00.000Z",
              "referenced_tweets": [ { "type": "replied_to", "id": "10" } ] },
            { "id": "21", "text": "my own post", "created_at": "2026-10-06T11:30:00.000Z" },
            { "id": "22", "text": "reply to a deleted post", "created_at": "2026-10-07T09:00:00.000Z",
              "referenced_tweets": [ { "type": "replied_to", "id": "99" } ] },
            { "id": "23", "text": "truncated...", "created_at": "2026-10-08T09:00:00.000Z",
              "note_tweet": { "text": "the full long text" } }
          ],
          "includes": { "tweets": [ { "id": "10", "text": "someone else's post" } ] },
          "meta": { "result_count": 4 }
        }
        """;

        using var doc = JsonDocument.Parse(json);
        var items = XApiTweetSource.ParsePage(doc.RootElement);

        Assert.Equal(4, items.Count);
        Assert.Equal(PostType.MyReply, items[0].Type);
        Assert.Equal("someone else's post", items[0].ParentText);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 10, 0, 0, TimeSpan.Zero), items[0].CreatedAt);
        Assert.Equal(PostType.MyPost, items[1].Type);
        Assert.Null(items[1].ParentText);
        Assert.Equal(PostType.MyReply, items[2].Type);
        Assert.Null(items[2].ParentText);            // parent not returned (deleted/protected)
        Assert.Equal("the full long text", items[3].Text);
    }

    [Fact]
    public void ParsePage_handles_empty_page()
    {
        using var doc = JsonDocument.Parse("""{ "meta": { "result_count": 0 } }""");
        Assert.Empty(XApiTweetSource.ParsePage(doc.RootElement));
    }
}
