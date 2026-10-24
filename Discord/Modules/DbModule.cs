using Discord;
using Discord.Interactions;
using TwitterNotifyBot.DB;
using TwitterNotifyBot.Discord.Autocomplete;
using TwitterNotifyBot.Models;
using TwitterNotifyBot.Services;
using TwitterNotifyBot.Utils;
using Microsoft.EntityFrameworkCore;

namespace TwitterNotifyBot.Discord.Modules;

[RequireTeam]
[Group("db", "Manage Twitter notifies for this guild")]
public sealed class DbModule(
    TwitterNotifyService notifyService,
    TwitterApiClient twitterApi,
    IDbContextFactory<BotDbContext> dbFactory,
    BotI18n i18n) : BaseSlashModule(i18n)
{
    private readonly TwitterNotifyService _notifyService = notifyService;
    private readonly TwitterApiClient _twitterApi = twitterApi;
    private readonly IDbContextFactory<BotDbContext> _dbFactory = dbFactory;

    [SlashCommand("insert", "Insert notify entry by channel id")]
    public async Task InsertNotifyAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(AllNameAutocompleteHandler))]
        string screenName,
        [Summary(description: "Discord channel id")]
        string channelId,
        [Summary(description: "Message text")]
        string? text = null)
    {
        if (!ulong.TryParse(channelId, out var channelSnowflake))
        {
            await ErrorAsync(T("common.invalid_channel_id"));
            return;
        }

        var channel = await Context.Client.GetChannelAsync(channelSnowflake);
        if (channel is not ITextChannel textChannel || channel is not IGuildChannel guildChannel)
        {
            await ErrorAsync(T("common.channel_must_text"));
            return;
        }

        var user = await _notifyService.FindOrCreateUserAsync(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var exists = await db.TwitterNotifies.AnyAsync(x =>
            x.GuildId == guildChannel.GuildId.ToString() && x.TargetId == user.TwitterId);

        if (exists)
        {
            await SuccessAsync(T("support.notify_exists"));
            return;
        }

        db.TwitterNotifies.Add(new TwitterNotifyEntity
        {
            GuildId = guildChannel.GuildId.ToString(),
            TargetId = user.TwitterId,
            ChannelId = textChannel.Id.ToString(),
            Text = text ?? string.Empty
        });

        await db.SaveChangesAsync();
        await SuccessAsync(T("support.notify_inserted", user.ScreenName));
    }

    [SlashCommand("delete", "Delete user and all notify entries")]
    public async Task DeleteNotifyAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(AllNotifyAutocompleteHandler))]
        string screenName)
    {
        var normalized = TwitterNotifyService.NormalizeScreenName(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.TwitterUsers
            .Include(x => x.Notifies)
            .FirstOrDefaultAsync(x => x.ScreenName == normalized);

        if (user is null)
        {
            await SuccessAsync(T("support.user_not_found"));
            return;
        }

        db.TwitterNotifies.RemoveRange(user.Notifies);
        db.TwitterUsers.Remove(user);
        await db.SaveChangesAsync();

        await SuccessAsync(T("support.deleted_user_and_notify", normalized));
    }

    [SlashCommand("notify-list", "List enabled tracked users")]
    public async Task ListNotifyAsync([Summary(description: "0-based page index")] int page = 0)
    {
        if (page < 0)
        {
            page = 0;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.TwitterUsers
            .Where(x => x.Enabled)
            .OrderBy(x => x.ScreenName)
            .Skip(page * 20)
            .Take(20)
            .ToListAsync();

        if (users.Count == 0)
        {
            await SuccessAsync(T("support.no_enabled_users_page"));
            return;
        }

        var lines = string.Join("\n", users.Select(x => $"[{x.Name}]({x.Url})"));
        await SuccessAsync(new EmbedBuilder().WithDescription(lines));
    }

    [SlashCommand("disable", "Disable one tracked twitter user")]
    public async Task DisableNotifyAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(AllNameAutocompleteHandler))]
        string screenName)
    {
        var normalized = TwitterNotifyService.NormalizeScreenName(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.TwitterUsers
            .Include(x => x.Notifies)
            .FirstOrDefaultAsync(x => x.ScreenName == normalized);

        if (user is null)
        {
            await SuccessAsync(T("support.user_not_found"));
            return;
        }

        user.Enabled = false;
        db.TwitterNotifies.RemoveRange(user.Notifies);

        var listId = _notifyService.ListId;
        if (!string.IsNullOrWhiteSpace(listId))
        {
            await _twitterApi.RemoveListMemberAsync(listId, user.TwitterId);
        }

        await db.SaveChangesAsync();
        await SuccessAsync(T("support.disabled_user", user.ScreenName));
    }

    [SlashCommand("disable-list", "List disabled users")]
    public async Task DisableListAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.TwitterUsers
            .Where(x => !x.Enabled)
            .OrderBy(x => x.ScreenName)
            .Select(x => x.ScreenName)
            .ToListAsync();

        if (users.Count == 0)
        {
            await SuccessAsync(T("support.no_disabled_users"));
            return;
        }

        await SuccessAsync(T("support.disabled_users", users.Count, string.Join("\n", users)));
    }
}
