using TwitterNotifyBot.Models;

namespace TwitterNotifyBot.Utils;

public static class TwitterTypeMapper
{
    private static readonly string[] DefaultTypeTokens = ["post", "quote"];
    private static readonly TwitterNotifyType[] DefaultTypes = [TwitterNotifyType.Post, TwitterNotifyType.Quote];

    public static IReadOnlyCollection<TwitterNotifyType> FromFlags(bool post, bool retweet, bool quote, bool reply)
    {
        var result = new List<TwitterNotifyType>(4);
        if (post)
        {
            result.Add(TwitterNotifyType.Post);
        }

        if (retweet)
        {
            result.Add(TwitterNotifyType.Retweet);
        }

        if (quote)
        {
            result.Add(TwitterNotifyType.Quote);
        }

        if (reply)
        {
            result.Add(TwitterNotifyType.Reply);
        }

        return result.Count == 0 ? DefaultTypes : result;
    }

    public static IReadOnlyCollection<TwitterNotifyType> ParseTypes(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return DefaultTypes;
        }

        try
        {
            var tokens = System.Text.Json.JsonSerializer.Deserialize<string[]>(raw);
            if (tokens is null || tokens.Length == 0)
            {
                return DefaultTypes;
            }

            var result = new HashSet<TwitterNotifyType>();
            foreach (var token in tokens)
            {
                if (TryMapToken(token, out var type))
                {
                    result.Add(type);
                }
            }

            return result.Count == 0
                ? DefaultTypes
                : result.ToArray();
        }
        catch
        {
            return DefaultTypes;
        }
    }

    public static string ToStorageJson(IEnumerable<TwitterNotifyType> types)
    {
        var tokens = types.Select(ToToken).Distinct().ToArray();
        if (tokens.Length == 0)
        {
            tokens = DefaultTypeTokens;
        }
        return System.Text.Json.JsonSerializer.Serialize(tokens);
    }

    public static string ToToken(TwitterNotifyType type) => type switch
    {
        TwitterNotifyType.Post => "post",
        TwitterNotifyType.Retweet => "retweet",
        TwitterNotifyType.Quote => "quote",
        TwitterNotifyType.Reply => "reply",
        _ => "post"
    };

    public static string ToDisplay(TwitterNotifyType type) => type switch
    {
        TwitterNotifyType.Post => "貼文",
        TwitterNotifyType.Retweet => "轉推",
        TwitterNotifyType.Quote => "引用",
        TwitterNotifyType.Reply => "回覆",
        _ => "貼文"
    };

    private static bool TryMapToken(string? token, out TwitterNotifyType type)
    {
        type = TwitterNotifyType.Post;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var value = token.Trim();
        var lower = value.ToLowerInvariant();

        switch (lower)
        {
            case "post":
            case "tweet":
            case "normal":
            case "發佈":
            case "貼文":
            case "推文":
                type = TwitterNotifyType.Post;
                return true;
            case "retweet":
            case "rt":
            case "轉推":
                type = TwitterNotifyType.Retweet;
                return true;
            case "quote":
            case "qt":
            case "引用":
            case "轉推並留言":
                type = TwitterNotifyType.Quote;
                return true;
            case "reply":
            case "rp":
            case "回復":
            case "回覆":
                type = TwitterNotifyType.Reply;
                return true;
            default:
                return false;
        }
    }
}
