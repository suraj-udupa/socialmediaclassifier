using Microsoft.Extensions.Configuration;
using SocialClassifier.Core;
using SocialClassifier.Excel;
using SocialClassifier.Laya;
using SocialClassifier.XApi;

var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddUserSecrets<Program>(optional: true)
    .AddCommandLine(args)
    .Build();

// Delete mode: removes the user's replies (never original posts). Dry run unless --Delete:Confirm=true.
//   --Mode=delete --Input:From=2020-01-01 [--Input:To=2026-11-07] [--Delete:Confirm=true]
if (string.Equals(config["Mode"], "delete", StringComparison.OrdinalIgnoreCase))
{
    var delOptions = config.GetSection("XApi").Get<XApiOptions>() ?? new XApiOptions();
    if (string.IsNullOrWhiteSpace(delOptions.ConsumerKey) || string.IsNullOrWhiteSpace(delOptions.AccessToken))
    {
        Console.Error.WriteLine("X credentials missing. Set them with dotnet user-secrets (XApi:ConsumerKey, " +
            "XApi:ConsumerSecret, XApi:AccessToken, XApi:AccessTokenSecret).");
        return 3;
    }

    var delFrom = ParseDay(config["Input:From"], 0);
    var delToExclusive = ParseDay(config["Input:To"], 1);
    var dryRun = !string.Equals(config["Delete:Confirm"], "true", StringComparison.OrdinalIgnoreCase);

    Console.WriteLine($"Mode: delete replies, range {delFrom?.ToString("yyyy-MM-dd") ?? "(start)"} to " +
        $"{(delToExclusive?.AddDays(-1).ToString("yyyy-MM-dd")) ?? "(now)"} inclusive" +
        (dryRun ? " [DRY RUN]" : " [LIVE]"));

    using var delHttp = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    var delPipeline = new DeletePipeline(
        new XApiTweetSource(delHttp, delOptions, delFrom, delToExclusive, Console.WriteLine),
        new XApiTweetDeleter(delHttp, delOptions, Console.WriteLine),
        new ExcelDeletionLog(config["Output:ExcelPath"]!),
        Console.WriteLine);

    using var delCts = new CancellationTokenSource();
    Console.CancelKeyPress += (_, e) => { e.Cancel = true; delCts.Cancel(); };

    var summary = await delPipeline.RunAsync(dryRun, delCts.Token);
    Console.WriteLine(summary.DryRun
        ? $"Dry run complete: {summary.RepliesFound - summary.AlreadyLogged} replies would be deleted."
        : $"Done: deleted {summary.Deleted} replies. Log: {Path.GetFullPath(config["Output:ExcelPath"]!)} (sheet 'Deleted')");
    return 0;
}

var layaOptions = config.GetSection("Laya").Get<LayaOptions>() ?? new LayaOptions();
var categories = config.GetSection("Categories").Get<List<Category>>() ?? [];
if (layaOptions.Enabled && categories.Count == 0) { Console.Error.WriteLine("No categories configured."); return 1; }

var inputFile = config["Input:JsonFile"]!;
var outputPath = config["Output:ExcelPath"]!;
if (!Path.IsPathRooted(inputFile)) inputFile = Path.Combine(AppContext.BaseDirectory, inputFile);

if (layaOptions.RawLogPath is not null)
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(layaOptions.RawLogPath))!);

// Laya:Enabled=false (or --Laya:Enabled=false) skips classification entirely.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
LayaClassifier? classifier = null;
if (layaOptions.Enabled)
{
    classifier = new LayaClassifier(http, layaOptions, categories);
    if (!await classifier.IsHealthyAsync(CancellationToken.None))
    {
        Console.Error.WriteLine($"Laya is not reachable at {layaOptions.BaseUrl}/health");
        return 2;
    }
    Console.WriteLine($"Laya OK at {layaOptions.BaseUrl} (model: {layaOptions.Model})");
}
else
{
    Console.WriteLine("Laya disabled: posts will be exported without classification.");
}

ITweetSource source;
if (string.Equals(config["Input:Source"], "x", StringComparison.OrdinalIgnoreCase))
{
    // Dates are UTC calendar days: --Input:From=2026-01-01 --Input:To=2026-03-31 (To is inclusive).
    DateTimeOffset? from = ParseDay(config["Input:From"], 0);
    DateTimeOffset? toExclusive = ParseDay(config["Input:To"], 1);

    var xOptions = config.GetSection("XApi").Get<XApiOptions>() ?? new XApiOptions();
    if (string.IsNullOrWhiteSpace(xOptions.ConsumerKey) || string.IsNullOrWhiteSpace(xOptions.AccessToken))
    {
        Console.Error.WriteLine("X credentials missing. Set them with dotnet user-secrets (XApi:ConsumerKey, " +
            "XApi:ConsumerSecret, XApi:AccessToken, XApi:AccessTokenSecret).");
        return 3;
    }

    Console.WriteLine($"Source: X API, range {from?.ToString("yyyy-MM-dd") ?? "(start)"} to " +
        $"{(toExclusive?.AddDays(-1).ToString("yyyy-MM-dd")) ?? "(now)"} inclusive");
    source = new XApiTweetSource(new HttpClient { Timeout = TimeSpan.FromSeconds(60) }, xOptions,
        from, toExclusive, Console.WriteLine);
}
else
{
    Console.WriteLine($"Source: JSON file {inputFile}");
    source = new JsonFileTweetSource(inputFile);
}

var pipeline = new Pipeline(source, classifier, new ExcelExporter(), categories);
var progress = new Progress<(int Done, int Total)>(p => Console.WriteLine($"  {(classifier is null ? "processed" : "classified")} {p.Done}/{p.Total}"));

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

await pipeline.RunAsync(outputPath, progress, cts.Token);
Console.WriteLine($"Wrote {Path.GetFullPath(outputPath)}");
return 0;

static DateTimeOffset? ParseDay(string? value, int addDays) =>
    string.IsNullOrWhiteSpace(value)
        ? null
        : new DateTimeOffset(DateOnly.Parse(value).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).AddDays(addDays);
