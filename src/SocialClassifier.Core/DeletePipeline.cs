namespace SocialClassifier.Core;

public enum DeleteOutcome { Deleted, AlreadyGone }

public sealed record DeletionEntry(string Id, string Text, DateTimeOffset DeletedAt, DeleteOutcome Outcome);

public sealed record DeleteSummary(int RepliesFound, int AlreadyLogged, int Deleted, bool DryRun);

/// <summary>
/// Deletes the user's replies (never original posts). With dryRun = true nothing is deleted or logged.
/// </summary>
public sealed class DeletePipeline(
    ITweetSource source,
    ITweetDeleter deleter,
    IDeletionLog deletionLog,
    Action<string>? log = null)
{
    private const int SaveEvery = 25;

    public async Task<DeleteSummary> RunAsync(bool dryRun, CancellationToken ct)
    {
        var items = await source.GetItemsAsync(ct);
        var replies = items.Where(i => i.Type == PostType.MyReply).ToList();

        var done = deletionLog.LoadDeletedIds();
        var pending = replies.Where(r => !done.Contains(r.Id)).ToList();
        var alreadyLogged = replies.Count - pending.Count;

        log?.Invoke($"Found {replies.Count} replies ({alreadyLogged} already in the log, {pending.Count} to delete)");

        if (dryRun)
        {
            foreach (var r in pending)
                log?.Invoke($"  would delete {r.Id} [{r.CreatedAt:yyyy-MM-dd}] {Shorten(r.Text)}");
            log?.Invoke("Dry run: nothing deleted. Add --Delete:Confirm=true to delete.");
            return new DeleteSummary(replies.Count, alreadyLogged, 0, true);
        }

        var deleted = 0;
        try
        {
            foreach (var r in pending)
            {
                ct.ThrowIfCancellationRequested();
                var outcome = await deleter.DeleteAsync(r.Id, ct);
                deletionLog.Add(new DeletionEntry(r.Id, r.Text, DateTimeOffset.UtcNow, outcome));
                deleted++;
                log?.Invoke($"  deleted {deleted}/{pending.Count}");
                if (deleted % SaveEvery == 0) deletionLog.Save();
            }
        }
        finally
        {
            deletionLog.Save();
        }

        return new DeleteSummary(replies.Count, alreadyLogged, deleted, false);
    }

    private static string Shorten(string text)
    {
        var t = text.ReplaceLineEndings(" ");
        return t.Length <= 80 ? t : t[..80] + "...";
    }
}
