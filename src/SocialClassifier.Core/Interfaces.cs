namespace SocialClassifier.Core;

public interface ITweetSource
{
    Task<IReadOnlyList<TweetItem>> GetItemsAsync(CancellationToken ct);
}

public interface IClassifier
{
    /// <summary>Returns the yes-probability for each category key.</summary>
    Task<IReadOnlyDictionary<string, double>> ClassifyAsync(string text, CancellationToken ct);
}

public interface IExporter
{
    void Export(string path, IReadOnlyList<AnalysisRow> rows, IReadOnlyList<Category> categories);
}

public interface ITweetDeleter
{
    /// <summary>Deletes one post. Returns AlreadyGone if X reports it no longer exists; throws on any other failure.</summary>
    Task<DeleteOutcome> DeleteAsync(string id, CancellationToken ct);
}

public interface IDeletionLog
{
    /// <summary>IDs recorded by earlier runs; these are skipped.</summary>
    IReadOnlySet<string> LoadDeletedIds();
    void Add(DeletionEntry entry);
    /// <summary>Persists entries added since the last save.</summary>
    void Save();
}
