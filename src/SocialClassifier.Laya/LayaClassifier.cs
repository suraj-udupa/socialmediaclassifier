using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using SocialClassifier.Core;

namespace SocialClassifier.Laya;

public sealed class LayaOptions
{
    /// <summary>When false, posts are fetched and exported without being classified (Laya is not contacted).</summary>
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://localhost:8000";
    public string Model { get; set; } = "english";
    public int MaxRetries { get; set; } = 3;
    /// <summary>If set, the raw JSON of every Laya response is appended here (one line each).</summary>
    public string? RawLogPath { get; set; }
}

public sealed class LayaClassifier : IClassifier
{
    private readonly HttpClient _http;
    private readonly LayaOptions _options;
    private readonly IReadOnlyList<Category> _categories;
    private readonly JsonObject _questions;

    public LayaClassifier(HttpClient http, LayaOptions options, IReadOnlyList<Category> categories)
    {
        _http = http;
        _options = options;
        _categories = categories;
        _http.BaseAddress = new Uri(options.BaseUrl.TrimEnd('/') + "/");

        // One yes/no ("noul") question per category; the answer is P(yes).
        _questions = new JsonObject();
        foreach (var c in categories)
            _questions[c.Key] = new JsonObject { ["type"] = "noul", ["instructions"] = c.Instruction };
    }

    public async Task<bool> IsHealthyAsync(CancellationToken ct)
    {
        try { return (await _http.GetAsync("health", ct)).IsSuccessStatusCode; }
        catch (HttpRequestException) { return false; }
    }

    public async Task<IReadOnlyDictionary<string, double>> ClassifyAsync(string text, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["state"] = text,
            ["questions"] = _questions.DeepClone(),
            ["model"] = _options.Model
        };

        for (var attempt = 0; ; attempt++)
        {
            using var response = await _http.PostAsJsonAsync("v1/systemone", body, ct);

            // 503 = Laya concurrency cap; it sends Retry-After: 1.
            if (response.StatusCode == HttpStatusCode.ServiceUnavailable && attempt < _options.MaxRetries)
            {
                await Task.Delay(TimeSpan.FromSeconds(1 + attempt), ct);
                continue;
            }

            var raw = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"Laya returned {(int)response.StatusCode}: {raw}");

            if (_options.RawLogPath is { } logPath)
                await File.AppendAllTextAsync(logPath, raw.ReplaceLineEndings(" ") + Environment.NewLine, ct);

            return Parse(raw);
        }
    }

    private IReadOnlyDictionary<string, double> Parse(string raw)
    {
        using var doc = JsonDocument.Parse(raw);
        var answers = doc.RootElement.GetProperty("answers");
        var result = new Dictionary<string, double>();

        foreach (var c in _categories)
        {
            if (!answers.TryGetProperty(c.Key, out var answer) || !answer.TryGetProperty("noul", out var noul))
                throw new InvalidOperationException($"Laya response has no 'noul' answer for '{c.Key}': {raw}");
            result[c.Key] = noul.GetDouble();
        }
        return result;
    }
}
