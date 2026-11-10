using SocialClassifier.Core;
using SocialClassifier.Excel;
using SocialClassifier.XApi;

namespace SocialClassifier.Tests;

public class DeleteTests
{
    private sealed class FakeSource(params TweetItem[] items) : ITweetSource
    {
        public Task<IReadOnlyList<TweetItem>> GetItemsAsync(CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<TweetItem>>(items);
    }

    private sealed class FakeDeleter(string? failOn = null) : ITweetDeleter
    {
        public List<string> Deleted { get; } = [];
        public Task<DeleteOutcome> DeleteAsync(string id, CancellationToken ct)
        {
            if (id == failOn) throw new HttpRequestException("403");
            Deleted.Add(id);
            return Task.FromResult(DeleteOutcome.Deleted);
        }
    }

    private sealed class FakeLog(params string[] existing) : IDeletionLog
    {
        public List<DeletionEntry> Entries { get; } = [];
        public int Saves { get; private set; }
        public IReadOnlySet<string> LoadDeletedIds() => existing.ToHashSet();
        public void Add(DeletionEntry entry) => Entries.Add(entry);
        public void Save() => Saves++;
    }

    private static TweetItem Reply(string id) =>
        new(id, DateTimeOffset.UtcNow, "reply " + id, PostType.MyReply, "parent");
    private static TweetItem Post(string id) =>
        new(id, DateTimeOffset.UtcNow, "post " + id, PostType.MyPost, null);

    private sealed class CapturingExporter : IExporter
    {
        public IReadOnlyList<AnalysisRow> Rows { get; private set; } = [];
        public void Export(string path, IReadOnlyList<AnalysisRow> rows, IReadOnlyList<Category> categories) => Rows = rows;
    }

    [Fact]
    public async Task Pipeline_without_classifier_exports_unclassified_rows()
    {
        var exporter = new CapturingExporter();
        var p = new Pipeline(new FakeSource(Reply("1"), Post("2")), classifier: null, exporter, []);

        await p.RunAsync("ignored.xlsx", null, CancellationToken.None);

        Assert.Equal(2, exporter.Rows.Count);
        Assert.All(exporter.Rows, r => { Assert.Empty(r.Probabilities); Assert.Equal("", r.TopLabel); });
    }

    [Fact]
    public void Excel_export_without_classification_omits_label_columns()
    {
        var path = Path.Combine(Path.GetTempPath(), $"exptest-{Guid.NewGuid():N}.xlsx");
        try
        {
            var row = new AnalysisRow(Reply("1"), new Dictionary<string, double>(), "", 0);
            new ExcelExporter().Export(path, [row], [new Category("hate", "Hate", "q")]);

            using var wb = new ClosedXML.Excel.XLWorkbook(path);
            var ws = wb.Worksheet("Analysis");
            var headers = ws.Row(1).CellsUsed().Select(c => c.GetString()).ToList();
            Assert.Equal(["Main Post", "My Reply", "Post Type", "Date (UTC)", "Tweet ID", "URL"], headers);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task Deletes_only_replies()
    {
        var deleter = new FakeDeleter();
        var log = new FakeLog();
        var p = new DeletePipeline(new FakeSource(Reply("1"), Post("2"), Reply("3")), deleter, log);

        var summary = await p.RunAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(["1", "3"], deleter.Deleted);
        Assert.Equal(2, summary.Deleted);
        Assert.Equal(["1", "3"], log.Entries.Select(e => e.Id));
    }

    [Fact]
    public async Task Dry_run_deletes_and_logs_nothing()
    {
        var deleter = new FakeDeleter();
        var log = new FakeLog();
        var p = new DeletePipeline(new FakeSource(Reply("1"), Reply("2")), deleter, log);

        var summary = await p.RunAsync(dryRun: true, CancellationToken.None);

        Assert.Empty(deleter.Deleted);
        Assert.Empty(log.Entries);
        Assert.True(summary.DryRun);
    }

    [Fact]
    public async Task Skips_ids_already_in_the_log()
    {
        var deleter = new FakeDeleter();
        var p = new DeletePipeline(new FakeSource(Reply("1"), Reply("2")), deleter, new FakeLog("1"));

        var summary = await p.RunAsync(dryRun: false, CancellationToken.None);

        Assert.Equal(["2"], deleter.Deleted);
        Assert.Equal(1, summary.AlreadyLogged);
    }

    [Fact]
    public async Task Saves_log_and_stops_when_a_delete_fails()
    {
        var deleter = new FakeDeleter(failOn: "2");
        var log = new FakeLog();
        var p = new DeletePipeline(new FakeSource(Reply("1"), Reply("2"), Reply("3")), deleter, log);

        await Assert.ThrowsAsync<HttpRequestException>(() => p.RunAsync(false, CancellationToken.None));

        Assert.Equal(["1"], deleter.Deleted);   // stopped at the failure
        Assert.Equal(["1"], log.Entries.Select(e => e.Id));
        Assert.True(log.Saves >= 1);            // progress was persisted
    }

    [Fact]
    public void ParseResult_requires_deleted_true()
    {
        Assert.Equal(DeleteOutcome.Deleted, XApiTweetDeleter.ParseResult("1", """{"data":{"deleted":true}}"""));
        Assert.Throws<HttpRequestException>(() => XApiTweetDeleter.ParseResult("1", """{"data":{"deleted":false}}"""));
    }

    [Fact]
    public void Excel_log_round_trips_and_appends()
    {
        var path = Path.Combine(Path.GetTempPath(), $"deltest-{Guid.NewGuid():N}.xlsx");
        try
        {
            var first = new ExcelDeletionLog(path);
            first.Add(new DeletionEntry("1900000000000000001", "a", DateTimeOffset.UtcNow, DeleteOutcome.Deleted));
            first.Save();

            var second = new ExcelDeletionLog(path);
            Assert.Equal(["1900000000000000001"], second.LoadDeletedIds());
            second.Add(new DeletionEntry("1900000000000000002", "b", DateTimeOffset.UtcNow, DeleteOutcome.AlreadyGone));
            second.Save();

            Assert.Equal(2, new ExcelDeletionLog(path).LoadDeletedIds().Count);
        }
        finally { File.Delete(path); }
    }
}
