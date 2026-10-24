using System.Globalization;
using Discord;
using Discord.Net;
using Discord.WebSocket;
using TwitterNotifyBot.Config;
using TwitterNotifyBot.DB;
using TwitterNotifyBot.Models;
using TwitterNotifyBot.Utils;
using Microsoft.EntityFrameworkCore;

namespace TwitterNotifyBot.Services;

public sealed class TwitterNotifyService(
    IDbContextFactory<BotDbContext> dbFactory,
    TwitterApiClient twitter,
    BotConfig config)
{
    private const int MaxEmbeds = 10;
    private const int MaxUrlFieldLength = 1024;

    private readonly IDbContextFactory<BotDbContext> _dbFactory = dbFactory;
    private readonly TwitterApiClient _twitter = twitter;
    private readonly BotConfig _config = config;
    private readonly SemaphoreSlim _tickLock = new(1, 1);

    private DiscordSocketClient? _client;
    private TwitterList? _list;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public string? ListId => _list?.Id;

    public async Task InitializeAsync(DiscordSocketClient client, CancellationToken ct = default)
    {
        _client = client;
        var lists = await _twitter.GetListsAsync(ct);
        _list = lists.FirstOrDefault(x => string.Equals(x.Name, "twitterNotify", StringComparison.OrdinalIgnoreCase))
                ?? await _twitter.CreateListAsync("twitterNotify", "Powered by Discord.Net", true, ct);

        Logger.Info($"Twitter list initialized: {_list.Name} ({_list.Id})");

        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => RunLoopAsync(_loopCts.Token), _loopCts.Token);
    }

    public async Task StopAsync()
    {
        try
        {
            if (_loopCts is not null)
            {
                await _loopCts.CancelAsync();
            }

            if (_loopTask is not null)
            {
                await _loopTask;
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
    }

    public static string NormalizeScreenName(string input)
    {
        var value = (input ?? string.Empty).Trim();
        value = value.Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("x.com/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("twitter.com/", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("@", string.Empty, StringComparison.Ordinal);

        var slashIdx = value.IndexOf('/');
        if (slashIdx >= 0)
        {
            value = value[..slashIdx];
        }

        var queryIdx = value.IndexOf('?');
        if (queryIdx >= 0)
        {
            value = value[..queryIdx];
        }

        return value.Trim();
    }

    public async Task<TwitterUserEntity> FindOrCreateUserAsync(string screenName, CancellationToken ct = default)
    {
        screenName = NormalizeScreenName(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);

        var user = await db.TwitterUsers
            .Include(x => x.Notifies)
            .FirstOrDefaultAsync(x => x.ScreenName == screenName, ct);

        if (user is not null)
        {
            return user;
        }

        var apiUser = await _twitter.GetUserByScreenNameAsync(screenName, ct);
        var byId = await db.TwitterUsers
            .Include(x => x.Notifies)
            .FirstOrDefaultAsync(x => x.TwitterId == apiUser.Id, ct);

        if (byId is not null)
        {
            byId.Name = apiUser.Name;
            byId.ScreenName = apiUser.ScreenName;
            byId.Icon = apiUser.Icon;
            await db.SaveChangesAsync(ct);
            return byId;
        }

        if (_list is null)
        {
            throw new InvalidOperationException("Twitter list has not been initialized.");
        }

        await _twitter.AddListMemberAsync(_list.Id, apiUser.Id, ct);
        var latestTweet = (await _twitter.GetUserTweetsAsync(apiUser.Id, ct))
            .OrderByDescending(x => ParseBigInt(x.Id))
            .FirstOrDefault();

        var entity = new TwitterUserEntity
        {
            TwitterId = apiUser.Id,
            Name = apiUser.Name,
            ScreenName = apiUser.ScreenName,
            Icon = apiUser.Icon,
            LastTweet = latestTweet?.Id ?? "0",
            Enabled = true
        };

        db.TwitterUsers.Add(entity);
        await db.SaveChangesAsync(ct);
        return entity;
    }

    public async Task<IReadOnlyList<string>> SearchGuildNotifyScreenNamesAsync(string guildId, string prefix, CancellationToken ct = default)
    {
        prefix = NormalizeScreenName(prefix);
        var hasPrefix = !string.IsNullOrWhiteSpace(prefix);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var names = await db.TwitterNotifies
            .Include(x => x.Target)
            .Where(x => x.GuildId == guildId)
            .Select(x => x.Target.ScreenName)
            .Distinct()
            .ToListAsync(ct);

        var result = names
            .Where(x => !hasPrefix || x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .Take(20)
            .ToList();

        Logger.Info($"[Query] guild={guildId} kind=guild_notify prefix={prefix} hits={result.Count}");
        return result;
    }

    public async Task<IReadOnlyList<string>> SearchAllNotifyScreenNamesAsync(string prefix, CancellationToken ct = default)
    {
        prefix = NormalizeScreenName(prefix);
        var hasPrefix = !string.IsNullOrWhiteSpace(prefix);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var names = await db.TwitterNotifies
            .Include(x => x.Target)
            .Select(x => x.Target.ScreenName)
            .Distinct()
            .ToListAsync(ct);

        var result = names
            .Where(x => !hasPrefix || x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .Take(20)
            .ToList();

        Logger.Info($"[Query] kind=all_notify prefix={prefix} hits={result.Count}");
        return result;
    }

    public async Task<IReadOnlyList<string>> SearchAllEnabledUsersAsync(string prefix, CancellationToken ct = default)
    {
        prefix = NormalizeScreenName(prefix);
        var hasPrefix = !string.IsNullOrWhiteSpace(prefix);

        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        var names = await db.TwitterUsers
            .Where(x => x.Enabled)
            .Select(x => x.ScreenName)
            .ToListAsync(ct);

        var matched = names
            .Where(x => !hasPrefix || x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x)
            .Take(20)
            .ToList();

        if (matched.Count == 0 && !string.IsNullOrWhiteSpace(prefix))
        {
            matched.Add(prefix);
        }

        Logger.Info($"[Query] kind=enabled_users prefix={prefix} hits={matched.Count}");
        return matched;
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await ProcessOnceAsync(ct);

        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_config.Ms));
        while (await timer.WaitForNextTickAsync(ct))
        {
            await ProcessOnceAsync(ct);
        }
    }

    private async Task ProcessOnceAsync(CancellationToken ct)
    {
        if (_list is null || _client is null)
        {
            return;
        }

        if (!await _tickLock.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            var dbUsers = await db.TwitterUsers
                .Include(x => x.Notifies)
                .Where(x => x.Enabled)
                .ToListAsync(ct);

            var tweets = (await _twitter.GetListTweetsAsync(_list.Id, ct))
                .OrderBy(x => ParseBigInt(x.Id))
                .ToArray();

            var newTweetCount = 0;
            var sentNotifyCount = 0;
            var removedUserCount = 0;

            foreach (var user in dbUsers)
            {
                if (user.Notifies.Count == 0)
                {
                    removedUserCount++;
                    await RemoveUserAsync(db, user, ct);
                    continue;
                }

                var userTweets = tweets
                    .Where(x => x.Author.Id == user.TwitterId && ParseBigInt(x.Id) > ParseBigInt(user.LastTweet))
                    .ToArray();
                newTweetCount += userTweets.Length;

                if (userTweets.Length == 0)
                {
                    continue;
                }

                var notifies = await ResolveNotifiesAsync(db, user, ct);
                if (notifies.Count == 0)
                {
                    removedUserCount++;
                    await RemoveUserAsync(db, user, ct);
                    continue;
                }

                foreach (var tweet in userTweets)
                {
                    foreach (var notify in notifies)
                    {
                        if (!notify.Types.Contains(tweet.Type))
                        {
                            continue;
                        }

                        try
                        {
                            var (embeds, mediaLinks) = BuildTweetMessage(tweet);
                            await SendWithRetryAsync(notify.Channel, notify.Content, embeds, ct);
                            if (!string.IsNullOrWhiteSpace(mediaLinks))
                            {
                                await SendWithRetryAsync(notify.Channel, mediaLinks, null, ct);
                            }

                            sentNotifyCount++;
                        }
                        catch (Exception ex)
                        {
                            Logger.Warn(ex, $"Send tweet notify failed. guild={notify.GuildId}, channel={notify.Channel.Id}, tweet={tweet.Id}");
                        }

                        await Task.Delay(300, ct);
                    }
                }

                var latest = userTweets[^1];
                user.LastTweet = latest.Id;
                user.ScreenName = latest.Author.ScreenName;
                user.Name = latest.Author.Name;
                user.Icon = latest.Author.Icon;
                await db.SaveChangesAsync(ct);
            }

            sw.Stop();
            Logger.Info($"[Twitter Poll] fetched={newTweetCount} sent={sentNotifyCount} removed={removedUserCount} elapsedMs={sw.ElapsedMilliseconds}\n", ConsoleColor.DarkCyan);
            Console.ResetColor();
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Twitter timer failed.");
        }
        finally
        {
            _tickLock.Release();
        }
    }

    private async Task RemoveUserAsync(BotDbContext db, TwitterUserEntity user, CancellationToken ct)
    {
        if (_list is not null)
        {
            try
            {
                await _twitter.RemoveListMemberAsync(_list.Id, user.TwitterId, ct);
            }
            catch (Exception ex)
            {
                Logger.Warn(ex, $"Failed removing user from Twitter list: {user.TwitterId}");
            }
        }

        db.TwitterUsers.Remove(user);
        await db.SaveChangesAsync(ct);
        Logger.Info($"Removed user with no notify targets: {user.ScreenName}");
    }

    private async Task<List<ResolvedNotify>> ResolveNotifiesAsync(BotDbContext db, TwitterUserEntity user, CancellationToken ct)
    {
        var list = new List<ResolvedNotify>();
        if (_client is null)
        {
            return list;
        }

        foreach (var notify in user.Notifies)
        {
            if (!ulong.TryParse(notify.GuildId, out var guildId)
                || !ulong.TryParse(notify.ChannelId, out var channelId))
            {
                db.TwitterNotifies.Remove(notify);
                continue;
            }

            var guild = _client.GetGuild(guildId);
            var channel = guild?.GetTextChannel(channelId);

            if (channel is null || !CanSendToChannel(channel))
            {
                db.TwitterNotifies.Remove(notify);
                continue;
            }

            list.Add(new ResolvedNotify
            {
                GuildId = notify.GuildId,
                GuildName = guild!.Name,
                Content = notify.TextOrNull ?? string.Empty,
                Channel = channel,
                Types = [.. TwitterTypeMapper.FromFlags(notify.Post, notify.Retweet, notify.Quote, notify.Reply)]
            });
        }

        await db.SaveChangesAsync(ct);
        return list;
    }

    private bool CanSendToChannel(SocketTextChannel channel)
    {
        if (_client is null)
        {
            return false;
        }

        // CurrentUser is the bot member in this guild and is available without full member intent.
        var me = channel.Guild.CurrentUser;
        if (me is null)
        {
            return false;
        }

        var perm = me.GetPermissions(channel);
        return perm.ViewChannel && perm.SendMessages;
    }

    private static async Task SendWithRetryAsync(SocketTextChannel channel, string? content, IReadOnlyList<Embed>? embeds, CancellationToken ct)
    {
        const int maxRetries = 3;
        var delayMs = 500;

        for (var attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                await channel.SendMessageAsync(text: content, embeds: embeds?.ToArray());
                return;
            }
            catch (HttpException ex) when ((int)ex.HttpCode == 429 && attempt < maxRetries - 1)
            {
                await Task.Delay(delayMs, ct);
                delayMs *= 2;
            }
        }
    }

    private static (IReadOnlyList<Embed> Embeds, string? MediaLinks) BuildTweetMessage(TwitterTweet tweet)
    {
        var first = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithAuthor($"{tweet.Author.Name}(@{tweet.Author.ScreenName})", tweet.Author.Icon, tweet.Author.Url)
            .WithTitle($"{tweet.Author.Name} {TwitterTypeMapper.ToDisplay(tweet.Type)}")
            .WithUrl(tweet.Url)
            .WithDescription(tweet.Text ?? "\u200B")
            .WithTimestamp(tweet.CreatedAt);

        if (tweet.Urls.Count > 0)
        {
            var urlText = string.Join("\n", tweet.Urls);
            if (urlText.Length > MaxUrlFieldLength)
            {
                urlText = urlText[..(MaxUrlFieldLength - 3)] + "...";
            }

            if (!string.IsNullOrWhiteSpace(urlText))
            {
                first.AddField("連結", urlText);
            }
        }

        if (tweet.Medias.Count == 0)
        {
            return ([first.Build()], null);
        }

        var allPhotos = tweet.Medias.All(x => string.Equals(x.Type, "photo", StringComparison.OrdinalIgnoreCase));
        if (!allPhotos)
        {
            var links = string.Join("\n", tweet.Medias.Select(x => $"[{x.Type}]({x.BestUrl})"));
            return ([first.Build()], links);
        }

        first.WithImageUrl(tweet.Medias[0].BestUrl);
        var embeds = new List<Embed> { first.Build() };
        foreach (var media in tweet.Medias.Skip(1).Take(MaxEmbeds - 1))
        {
            embeds.Add(new EmbedBuilder().WithColor(Color.Blue).WithImageUrl(media.BestUrl).Build());
        }

        return (embeds, null);
    }

    private static System.Numerics.BigInteger ParseBigInt(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return System.Numerics.BigInteger.Zero;
        }

        return System.Numerics.BigInteger.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : System.Numerics.BigInteger.Zero;
    }

    private sealed class ResolvedNotify
    {
        public required string GuildId { get; init; }
        public required string GuildName { get; init; }
        public required SocketTextChannel Channel { get; init; }
        public required string Content { get; init; }
        public required HashSet<TwitterNotifyType> Types { get; init; }
    }
}
