namespace SocialClassifier.Core;

public enum PostType { MyPost, MyReply }

/// <summary>One post or reply authored by the user.</summary>
/// <param name="ParentText">Text of the post being replied to; null for original posts or unresolved parents.</param>
public sealed record TweetItem(
    string Id,
    DateTimeOffset CreatedAt,
    string Text,
    PostType Type,
    string? ParentText);

/// <summary>A yes/no question Laya answers per text. Key must be a stable identifier.</summary>
public sealed record Category(string Key, string DisplayName, string Instruction);

public sealed record AnalysisRow(
    TweetItem Item,
    IReadOnlyDictionary<string, double> Probabilities,
    string TopLabel,
    double DecisionProbability);
