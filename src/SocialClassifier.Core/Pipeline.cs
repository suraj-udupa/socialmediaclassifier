namespace SocialClassifier.Core;

/// <param name="classifier">Null = skip classification; rows are exported without probabilities or a label.</param>
public sealed class Pipeline(
    ITweetSource source,
    IClassifier? classifier,
    IExporter exporter,
    IReadOnlyList<Category> categories)
{
    private static readonly IReadOnlyDictionary<string, double> NoProbabilities = new Dictionary<string, double>();

    public async Task RunAsync(string outputPath, IProgress<(int Done, int Total)>? progress, CancellationToken ct)
    {
        var items = await source.GetItemsAsync(ct);
        var rows = new List<AnalysisRow>(items.Count);

        foreach (var item in items)
        {
            ct.ThrowIfCancellationRequested();

            if (classifier is null)
            {
                rows.Add(new AnalysisRow(item, NoProbabilities, "", 0));
            }
            else
            {
                var probs = await classifier.ClassifyAsync(item.Text, ct);
                var top = probs.OrderByDescending(p => p.Value).First();
                var label = categories.First(c => c.Key == top.Key).DisplayName;
                rows.Add(new AnalysisRow(item, probs, label, top.Value));
            }
            progress?.Report((rows.Count, items.Count));
        }

        exporter.Export(outputPath, rows, categories);
    }
}
