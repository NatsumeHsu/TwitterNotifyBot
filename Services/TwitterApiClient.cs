using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using TwitterNotifyBot.Config;
using TwitterNotifyBot.Models;
using TwitterNotifyBot.Utils;

namespace TwitterNotifyBot.Services;

public sealed class TwitterApiClient
{
    private const string UserByScreenNameEndpoint = "https://twitter.com/i/api/graphql/oUZZZ8Oddwxs8Cd3iW3UEA/UserByScreenName";
    private const string UserTweetsEndpoint = "https://twitter.com/i/api/graphql/WzJjibAcDa-oCjCcLOotcg/UserTweets";
    private const string ListsEndpoint = "https://twitter.com/i/api/graphql/xoietOOE63W0cH9LFt4yRA/ListsManagementPageTimeline";
    private const string ListMembersEndpoint = "https://twitter.com/i/api/graphql/WWxrex_8HmKW2dzlPnwtTg/ListMembers";
    private const string ListTweetsEndpoint = "https://twitter.com/i/api/graphql/TXyJ3x6-VnEbkV09UzebUQ/ListLatestTweetsTimeline";
    private const string CreateListEndpoint = "https://twitter.com/i/api/graphql/nHFMQuE4PMED1R0JTN4d-Q/CreateList";
    private const string AddListMemberEndpoint = "https://twitter.com/i/api/graphql/sw71TVciw1b2nRwV6eDZNA/ListAddMember";
    private const string RemoveListMemberEndpoint = "https://twitter.com/i/api/graphql/kHdBGndqf_JX3ef1T1931A/ListRemoveMember";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    public TwitterApiClient(HttpClient http, BotConfig config)
    {
        _http = http;

        _http.DefaultRequestHeaders.Clear();
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-TW,zh;q=0.9,en-US;q=0.8,en;q=0.7");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", "Bearer AAAAAAAAAAAAAAAAAAAAANRILgAAAAAAnNwIzUejRCOuH5E6I8xnZz4puTs%3D1Zv7ttfk8LF81IUq16cHjhLTvJu4FA33AGWWjCpTnA");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Referer", "https://twitter.com");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Twitter-Active-User", "yes");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Twitter-Auth-Type", "OAuth2Session");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Twitter-Client-Language", "zh-tw");

        if (!string.IsNullOrWhiteSpace(config.Twitter.AuthToken) && !string.IsNullOrWhiteSpace(config.Twitter.Ct0))
        {
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "Cookie",
                $"auth_token={config.Twitter.AuthToken}; ct0={config.Twitter.Ct0}");
            _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Csrf-Token", config.Twitter.Ct0);
        }
        else
        {
            Logger.Warn("Twitter cookie is empty. Calls to Twitter API will fail until data/config.json is configured.");
        }
    }

    public async Task<TwitterAuthUser> GetUserByScreenNameAsync(string screenName, CancellationToken ct = default)
    {
        var variables = JsonSerializer.Serialize(new
        {
            screen_name = screenName,
            withSafetyModeUserFields = true
        });

        var features = JsonSerializer.Serialize(new
        {
            responsive_web_graphql_exclude_directive_enabled = true,
            verified_phone_label_enabled = false,
            responsive_web_graphql_skip_user_profile_image_extensions_enabled = false,
            responsive_web_graphql_timeline_navigation_enabled = true
        });

        using var json = await GetJsonAsync(UserByScreenNameEndpoint, new Dictionary<string, string>
        {
            ["variables"] = variables,
            ["features"] = features
        }, ct);

        var root = json.RootElement;
        if (!root.TryGetProperty("data", out var data)
            || !data.TryGetProperty("user", out var user)
            || !user.TryGetProperty("result", out var result))
        {
            throw new InvalidOperationException($"Twitter user not found: {screenName}");
        }

        return ParseUser(result);
    }

    public async Task<IReadOnlyList<TwitterTweet>> GetUserTweetsAsync(string userId, CancellationToken ct = default)
    {
        var variables = JsonSerializer.Serialize(new
        {
            userId,
            count = 20,
            includePromotedContent = true,
            withCommunity = true,
            withVoice = true,
            withV2Timeline = true,
            withQuickPromoteEligibilityTweetFields = true
        });

        var features = JsonSerializer.Serialize(new
        {
            responsive_web_graphql_exclude_directive_enabled = true,
            verified_phone_label_enabled = false,
            responsive_web_graphql_timeline_navigation_enabled = true,
            responsive_web_graphql_skip_user_profile_image_extensions_enabled = false,
            tweetypie_unmention_optimization_enabled = true,
            responsive_web_edit_tweet_api_enabled = true,
            view_counts_everywhere_api_enabled = true,
            longform_notetweets_consumption_enabled = true,
            responsive_web_enhance_cards_enabled = false
        });

        using var json = await GetJsonAsync(UserTweetsEndpoint, new Dictionary<string, string>
        {
            ["variables"] = variables,
            ["features"] = features
        }, ct);

        return ParseTweetsDocument(json.RootElement);
    }

    public async Task<IReadOnlyList<TwitterList>> GetListsAsync(CancellationToken ct = default)
    {
        var variables = JsonSerializer.Serialize(new { count = 100 });
        var features = JsonSerializer.Serialize(new
        {
            responsive_web_graphql_exclude_directive_enabled = true,
            verified_phone_label_enabled = false,
            responsive_web_graphql_timeline_navigation_enabled = true,
            responsive_web_graphql_skip_user_profile_image_extensions_enabled = false
        });

        using var json = await GetJsonAsync(ListsEndpoint, new Dictionary<string, string>
        {
            ["variables"] = variables,
            ["features"] = features
        }, ct);

        var items = new List<TwitterList>();
        foreach (var listNode in JsonWalker.DescendantsByPropertyName(json.RootElement, "list"))
        {
            if (TryParseList(listNode, out var list))
            {
                items.Add(list);
            }
        }

        return [.. items
            .GroupBy(x => x.Id)
            .Select(x => x.First())];
    }

    public async Task<TwitterList> CreateListAsync(string name, string description, bool isPrivate = true, CancellationToken ct = default)
    {
        var payload = new
        {
            variables = new { name, description, isPrivate },
            features = new
            {
                responsive_web_graphql_exclude_directive_enabled = true,
                verified_phone_label_enabled = false,
                responsive_web_graphql_skip_user_profile_image_extensions_enabled = false,
                responsive_web_graphql_timeline_navigation_enabled = true
            },
            queryId = CreateQueryId()
        };

        using var json = await PostJsonAsync(CreateListEndpoint, payload, ct);
        foreach (var listNode in JsonWalker.DescendantsByPropertyName(json.RootElement, "list"))
        {
            if (TryParseList(listNode, out var list))
            {
                return list;
            }
        }

        throw new InvalidOperationException("Twitter list create succeeded but list payload was not found.");
    }

    public async Task<IReadOnlyList<TwitterAuthUser>> GetListMembersAsync(string listId, CancellationToken ct = default)
    {
        var variables = JsonSerializer.Serialize(new
        {
            listId,
            count = 50,
            withSafetyModeUserFields = true
        });

        var features = JsonSerializer.Serialize(new
        {
            responsive_web_graphql_exclude_directive_enabled = true,
            verified_phone_label_enabled = false,
            responsive_web_graphql_timeline_navigation_enabled = true,
            responsive_web_graphql_skip_user_profile_image_extensions_enabled = false
        });

        using var json = await GetJsonAsync(ListMembersEndpoint, new Dictionary<string, string>
        {
            ["variables"] = variables,
            ["features"] = features
        }, ct);

        var result = new List<TwitterAuthUser>();
        foreach (var userResult in JsonWalker.DescendantsByPropertyName(json.RootElement, "user_results"))
        {
            if (userResult.ValueKind != JsonValueKind.Object || !userResult.TryGetProperty("result", out var userNode))
            {
                continue;
            }

            try
            {
                result.Add(ParseUser(userNode));
            }
            catch
            {
                // skip malformed payload items
            }
        }

        return [.. result
            .GroupBy(x => x.Id)
            .Select(x => x.First())];
    }

    public async Task<IReadOnlyList<TwitterTweet>> GetListTweetsAsync(string listId, CancellationToken ct = default)
    {
        var variables = JsonSerializer.Serialize(new { listId, count = 50 });
        var features = JsonSerializer.Serialize(new
        {
            responsive_web_graphql_exclude_directive_enabled = true,
            verified_phone_label_enabled = false,
            responsive_web_graphql_timeline_navigation_enabled = true,
            responsive_web_graphql_skip_user_profile_image_extensions_enabled = false,
            tweetypie_unmention_optimization_enabled = true,
            responsive_web_edit_tweet_api_enabled = true,
            view_counts_everywhere_api_enabled = true,
            longform_notetweets_consumption_enabled = true,
            responsive_web_enhance_cards_enabled = false
        });

        using var json = await GetJsonAsync(ListTweetsEndpoint, new Dictionary<string, string>
        {
            ["variables"] = variables,
            ["features"] = features
        }, ct);

        return ParseTweetsDocument(json.RootElement);
    }

    public async Task AddListMemberAsync(string listId, string userId, CancellationToken ct = default)
    {
        var payload = new
        {
            variables = new { listId, userId },
            features = new
            {
                responsive_web_graphql_exclude_directive_enabled = true,
                verified_phone_label_enabled = false,
                responsive_web_graphql_skip_user_profile_image_extensions_enabled = false,
                responsive_web_graphql_timeline_navigation_enabled = true
            },
            queryId = CreateQueryId()
        };

        using var _ = await PostJsonAsync(AddListMemberEndpoint, payload, ct);
    }

    public async Task RemoveListMemberAsync(string listId, string userId, CancellationToken ct = default)
    {
        var payload = new
        {
            variables = new { listId, userId },
            features = new
            {
                responsive_web_graphql_exclude_directive_enabled = true,
                verified_phone_label_enabled = false,
                responsive_web_graphql_skip_user_profile_image_extensions_enabled = false,
                responsive_web_graphql_timeline_navigation_enabled = true
            },
            queryId = CreateQueryId()
        };

        using var _ = await PostJsonAsync(RemoveListMemberEndpoint, payload, ct);
    }

    private async Task<JsonDocument> GetJsonAsync(string url, IReadOnlyDictionary<string, string>? parameters = null, CancellationToken ct = default)
    {
        var uri = BuildUri(url, parameters);
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private async Task<JsonDocument> PostJsonAsync(string url, object payload, CancellationToken ct = default)
    {
        var jsonString = JsonSerializer.Serialize(payload, JsonOptions);
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(jsonString, Encoding.UTF8, "application/json")
        };

        using var response = await _http.SendAsync(request, ct);
        await EnsureSuccessAsync(response, ct);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        return await JsonDocument.ParseAsync(stream, cancellationToken: ct);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        throw new HttpRequestException($"Twitter API error {(int)response.StatusCode}: {body}", null, response.StatusCode);
    }

    private static string BuildUri(string baseUrl, IReadOnlyDictionary<string, string>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return baseUrl;
        }

        var query = string.Join("&", parameters.Select(x =>
            string.Concat(Uri.EscapeDataString(x.Key), "=", Uri.EscapeDataString(x.Value))));

        return string.Concat(baseUrl, "?", query);
    }

    private static string CreateQueryId()
    {
        const string chars = "1234567890abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";
        Span<char> buffer = stackalloc char[22];
        for (var i = 0; i < buffer.Length; i++)
        {
            buffer[i] = chars[Random.Shared.Next(chars.Length)];
        }

        return buffer.ToString();
    }

    private static List<TwitterTweet> ParseTweetsDocument(JsonElement root)
    {
        var tweets = new List<TwitterTweet>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var tweetResultsNode in JsonWalker.DescendantsByPropertyName(root, "tweet_results"))
        {
            if (tweetResultsNode.ValueKind != JsonValueKind.Object || !tweetResultsNode.TryGetProperty("result", out var resultNode))
            {
                continue;
            }

            var node = resultNode;
            if (node.ValueKind == JsonValueKind.Object && node.TryGetProperty("tweet", out var tweetNode))
            {
                node = tweetNode;
            }

            if (!TryParseTweet(node, out var tweet))
            {
                continue;
            }

            if (seen.Add(tweet.Id))
            {
                tweets.Add(tweet);
            }
        }

        tweets.Sort((a, b) => CompareTweetId(a.Id, b.Id));
        return tweets;
    }

    private static int CompareTweetId(string left, string right)
    {
        if (BigIntegerTryParse(left, out var a) && BigIntegerTryParse(right, out var b))
        {
            return a.CompareTo(b);
        }

        return string.CompareOrdinal(left, right);
    }

    private static bool BigIntegerTryParse(string value, out System.Numerics.BigInteger result)
    {
        return System.Numerics.BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
    }

    private static bool TryParseTweet(JsonElement node, out TwitterTweet tweet)
    {
        tweet = null!;

        if (node.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (!node.TryGetProperty("legacy", out var legacy))
        {
            return false;
        }

        var id = JsonWalker.GetString(node, "rest_id");
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (!node.TryGetProperty("core", out var core)
            || !core.TryGetProperty("user_results", out var userResults)
            || !userResults.TryGetProperty("result", out var userNode))
        {
            return false;
        }

        var user = ParseUser(userNode);
        var createdAtRaw = JsonWalker.GetString(legacy, "created_at");
        var createdAt = DateTime.TryParse(createdAtRaw, out var parsedTime) ? parsedTime : DateTime.UtcNow;
        var fullText = JsonWalker.GetString(legacy, "full_text") ?? string.Empty;

        var urls = new List<(string shortUrl, string expandedUrl)>();
        var medias = new List<TwitterMedia>();

        if (legacy.TryGetProperty("entities", out var entities))
        {
            if (entities.TryGetProperty("urls", out var urlNodes) && urlNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var urlNode in urlNodes.EnumerateArray())
                {
                    var shortUrl = JsonWalker.GetString(urlNode, "url") ?? string.Empty;
                    var expandedUrl = JsonWalker.GetString(urlNode, "expanded_url") ?? shortUrl;
                    urls.Add((shortUrl, expandedUrl));
                }
            }

            if (entities.TryGetProperty("media", out var mediaNodes) && mediaNodes.ValueKind == JsonValueKind.Array)
            {
                medias.AddRange(ParseMedia(mediaNodes));
            }
        }

        if (legacy.TryGetProperty("extended_entities", out var extEntities)
            && extEntities.TryGetProperty("media", out var extMediaNodes)
            && extMediaNodes.ValueKind == JsonValueKind.Array
            && medias.Count == 0)
        {
            medias.AddRange(ParseMedia(extMediaNodes));
        }

        var isRetweet = legacy.TryGetProperty("retweeted_status_result", out _);
        var isQuote = node.TryGetProperty("quoted_status_result", out _);
        var isReply = !string.IsNullOrWhiteSpace(JsonWalker.GetString(legacy, "in_reply_to_status_id_str"));

        var type = isReply
            ? TwitterNotifyType.Reply
            : isRetweet
                ? TwitterNotifyType.Retweet
                : isQuote
                    ? TwitterNotifyType.Quote
                    : TwitterNotifyType.Post;

        var text = fullText;
        foreach (var media in medias)
        {
            text = text.Replace(media.Url, string.Empty, StringComparison.Ordinal);
        }

        foreach (var (shortUrl, expandedUrl) in urls)
        {
            if (!string.IsNullOrEmpty(shortUrl) && !string.IsNullOrEmpty(expandedUrl))
            {
                text = text.Replace(shortUrl, expandedUrl, StringComparison.Ordinal);
            }
        }

        if (text.StartsWith("RT @", StringComparison.Ordinal))
        {
            var colonIndex = text.IndexOf(':');
            if (colonIndex >= 0 && colonIndex < text.Length - 1)
            {
                text = text[(colonIndex + 1)..].TrimStart();
            }
        }

        text = text.Trim();

        tweet = new TwitterTweet
        {
            Id = id,
            Url = $"https://x.com/{user.ScreenName}/status/{id}",
            Author = user,
            CreatedAt = createdAt,
            Type = type,
            Text = string.IsNullOrWhiteSpace(text) ? null : text,
            Urls = [.. urls.Select(x => x.expandedUrl).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct()],
            Medias = medias
        };

        return true;
    }

    private static List<TwitterMedia> ParseMedia(JsonElement mediaArray)
    {
        var list = new List<TwitterMedia>();
        foreach (var mediaNode in mediaArray.EnumerateArray())
        {
            var url = JsonWalker.GetString(mediaNode, "url") ?? string.Empty;
            var mediaUrl = JsonWalker.GetString(mediaNode, "media_url_https") ?? string.Empty;
            var type = JsonWalker.GetString(mediaNode, "type") ?? "photo";
            var variants = new List<string>();

            if (mediaNode.TryGetProperty("video_info", out var videoInfo)
                && videoInfo.TryGetProperty("variants", out var variantNodes)
                && variantNodes.ValueKind == JsonValueKind.Array)
            {
                foreach (var variant in variantNodes.EnumerateArray())
                {
                    var variantUrl = JsonWalker.GetString(variant, "url");
                    if (!string.IsNullOrWhiteSpace(variantUrl))
                    {
                        variants.Add(variantUrl);
                    }
                }
            }

            list.Add(new TwitterMedia
            {
                Url = url,
                MediaUrlHttps = mediaUrl,
                Type = type,
                VariantUrls = variants
            });
        }

        return list;
    }

    private static bool TryParseList(JsonElement node, out TwitterList list)
    {
        list = null!;
        if (node.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var id = JsonWalker.GetString(node, "id_str");
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        var createdAtRaw = JsonWalker.GetLong(node, "created_at") ?? 0L;
        // Twitter list payload may return milliseconds while other payloads use seconds.
        // Normalize to seconds before converting.
        var createdAtUnix = createdAtRaw > 253402300799L ? createdAtRaw / 1000L : createdAtRaw;
        createdAtUnix = Math.Clamp(createdAtUnix, 0L, 253402300799L);
        var createdAt = DateTimeOffset.FromUnixTimeSeconds(createdAtUnix).UtcDateTime;

        list = new TwitterList
        {
            Id = id,
            Name = JsonWalker.GetString(node, "name") ?? string.Empty,
            Mode = JsonWalker.GetString(node, "mode") ?? string.Empty,
            Description = JsonWalker.GetString(node, "description") ?? string.Empty,
            MemberCount = (int)(JsonWalker.GetLong(node, "member_count") ?? 0L),
            CreatedAt = createdAt
        };

        return true;
    }

    private static TwitterAuthUser ParseUser(JsonElement node)
    {
        var restId = JsonWalker.GetString(node, "rest_id") ?? throw new InvalidOperationException("Twitter user missing rest_id");
        if (!node.TryGetProperty("legacy", out var legacy))
        {
            throw new InvalidOperationException("Twitter user legacy payload missing.");
        }

        var createdAtRaw = JsonWalker.GetString(legacy, "created_at");
        var createdAt = DateTime.TryParse(createdAtRaw, out var parsedAt) ? parsedAt : DateTime.UtcNow;

        return new TwitterAuthUser
        {
            Id = restId,
            Name = JsonWalker.GetString(legacy, "name") ?? string.Empty,
            ScreenName = JsonWalker.GetString(legacy, "screen_name") ?? string.Empty,
            Icon = JsonWalker.GetString(legacy, "profile_image_url_https") ?? string.Empty,
            CreatedAt = createdAt
        };
    }
}
