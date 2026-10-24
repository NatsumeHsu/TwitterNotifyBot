namespace TwitterNotifyBot.Models;

public enum TwitterNotifyType
{
    Post,
    Retweet,
    Quote,
    Reply
}

public sealed class TwitterAuthUser
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string ScreenName { get; init; }
    public required string Icon { get; init; }
    public DateTime CreatedAt { get; init; }

    public string Url => $"https://x.com/{ScreenName}";
}

public sealed class TwitterMedia
{
    public required string Url { get; init; }
    public required string MediaUrlHttps { get; init; }
    public required string Type { get; init; }
    public IReadOnlyList<string> VariantUrls { get; init; } = [];

    public string BestUrl => VariantUrls.Count > 0 ? VariantUrls[^1] : MediaUrlHttps;
}

public sealed class TwitterTweet
{
    public required string Id { get; init; }
    public required string Url { get; init; }
    public required TwitterAuthUser Author { get; init; }
    public required DateTime CreatedAt { get; init; }
    public required TwitterNotifyType Type { get; init; }
    public string? Text { get; init; }
    public IReadOnlyList<string> Urls { get; init; } = [];
    public IReadOnlyList<TwitterMedia> Medias { get; init; } = [];
}

public sealed class TwitterList
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Mode { get; init; }
    public required string Description { get; init; }
    public int MemberCount { get; init; }
    public DateTime CreatedAt { get; init; }
}
