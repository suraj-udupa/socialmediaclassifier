using System.Text.Json;
using System.Text.Json.Serialization;

namespace SocialClassifier.Core;

/// <summary>Reads TweetItems from a JSON file. Used to test the pipeline before X access exists.</summary>
public sealed class JsonFileTweetSource(string path) : ITweetSource
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<IReadOnlyList<TweetItem>> GetItemsAsync(CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        var items = await JsonSerializer.DeserializeAsync<List<TweetItem>>(stream, Options, ct);
        return items ?? [];
    }
}
