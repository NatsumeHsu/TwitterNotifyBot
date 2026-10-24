using Discord;
using Discord.Interactions;
using TwitterNotifyBot.DB;
using TwitterNotifyBot.Discord.Autocomplete;
using TwitterNotifyBot.Models;
using TwitterNotifyBot.Services;
using TwitterNotifyBot.Utils;
using Microsoft.EntityFrameworkCore;

namespace TwitterNotifyBot.Discord.Modules;

[DefaultMemberPermissions(GuildPermission.Administrator)]
[Group("notify", "Manage Twitter notifies for this guild")]
public sealed class TwitterModule(
    TwitterNotifyService notifyService,
    IDbContextFactory<BotDbContext> dbFactory,
    BotI18n i18n) : BaseSlashModule(i18n)
{
    private readonly TwitterNotifyService _notifyService = notifyService;
    private readonly IDbContextFactory<BotDbContext> _dbFactory = dbFactory;

    public enum YesNoChoice
    {
        [ChoiceDisplay("Yes")]
        Yes = 1,
        [ChoiceDisplay("No")]
        No = 0
    }

    [SlashCommand("create", "Create twitter notify for this guild")]
    public async Task NotifyCreateAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(AllNameAutocompleteHandler))]
        string screenName,
        [Summary(description: "Target channel")]
        [ChannelTypes(ChannelType.Text, ChannelType.News)]
        IChannel channel,
        [Summary(description: "Message text sent before embeds")]
        string? text = null)
    {
        if (Context.Guild is null)
        {
            await ErrorAsync(T("common.guild_only"));
            return;
        }

        if (!TryExtractTextChannel(channel, out var channelId))
        {
            await ErrorAsync(T("common.channel_must_text"));
            return;
        }

        var dbUser = await _notifyService.FindOrCreateUserAsync(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var exists = await db.TwitterNotifies.AnyAsync(x =>
            x.GuildId == Context.Guild.Id.ToString() && x.TargetId == dbUser.TwitterId);

        if (exists)
        {
            await SuccessAsync(T("twitter.notify_exists_use_update"));
            return;
        }

        var notify = new TwitterNotifyEntity
        {
            GuildId = Context.Guild.Id.ToString(),
            ChannelId = channelId,
            TargetId = dbUser.TwitterId,
            Text = text ?? string.Empty
        };

        db.TwitterNotifies.Add(notify);
        await db.SaveChangesAsync();

        await SuccessAsync(T("twitter.notify_created", dbUser.ScreenName));
    }

    [SlashCommand("update", "Update notify channel/text")]
    public async Task NotifyUpdateAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(GuildNotifyAutocompleteHandler))]
        string screenName,
        [Summary(description: "New channel")]
        [ChannelTypes(ChannelType.Text, ChannelType.News)]
        IChannel? channel = null,
        [Summary(description: "New message text")]
        string? text = null)
    {
        if (Context.Guild is null)
        {
            await ErrorAsync(T("common.guild_only"));
            return;
        }

        var user = await _notifyService.FindOrCreateUserAsync(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var notify = await db.TwitterNotifies.FirstOrDefaultAsync(x =>
            x.GuildId == Context.Guild.Id.ToString() && x.TargetId == user.TwitterId);

        if (notify is null)
        {
            await SuccessAsync(T("twitter.notify_not_exists_use_create"));
            return;
        }

        if (channel is not null)
        {
            if (!TryExtractTextChannel(channel, out var channelId))
            {
                await ErrorAsync(T("common.channel_must_text"));
                return;
            }

            notify.ChannelId = channelId;
        }

        if (text is not null)
        {
            notify.Text = text;
        }

        EnsureAtLeastOneType(notify);

        await db.SaveChangesAsync();

        var embed = new EmbedBuilder()
            .WithDescription(T("twitter.updated_notify_for", user.ScreenName))
            .AddField(T("twitter.field_channel"), $"<#${notify.ChannelId}>".Replace("$", string.Empty), true)
            .AddField(T("twitter.field_text"), string.IsNullOrWhiteSpace(notify.Text) ? T("common.empty") : notify.Text, true);

        await SuccessAsync(embed);
    }

    [SlashCommand("remove", "Remove notify")]
    public async Task NotifyRemoveAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(GuildNotifyAutocompleteHandler))]
        string screenName)
    {
        if (Context.Guild is null)
        {
            await ErrorAsync(T("common.guild_only"));
            return;
        }

        var normalized = TwitterNotifyService.NormalizeScreenName(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var user = await db.TwitterUsers.FirstOrDefaultAsync(x => x.ScreenName == normalized);
        if (user is null)
        {
            await SuccessAsync(T("twitter.notify_not_exists"));
            return;
        }

        var notify = await db.TwitterNotifies.FirstOrDefaultAsync(x =>
            x.GuildId == Context.Guild.Id.ToString() && x.TargetId == user.TwitterId);

        if (notify is null)
        {
            await SuccessAsync(T("twitter.notify_not_exists"));
            return;
        }

        db.TwitterNotifies.Remove(notify);
        await db.SaveChangesAsync();

        await SuccessAsync(T("twitter.notify_removed", user.ScreenName));
    }

    [SlashCommand("list", "List guild notify entries")]
    public async Task NotifyListAsync([Summary(description: "1-based page index")] int page = 1)
    {
        if (Context.Guild is null)
        {
            await ErrorAsync(T("common.guild_only"));
            return;
        }

        if (page < 1)
        {
            page = 1;
        }

        await using var db = await _dbFactory.CreateDbContextAsync();
        var list = await db.TwitterNotifies
            .Include(x => x.Target)
            .Where(x => x.GuildId == Context.Guild.Id.ToString())
            .OrderBy(x => x.Target.ScreenName)
            .Skip((page - 1) * 10)
            .Take(10)
            .ToListAsync();

        if (list.Count == 0)
        {
            await SuccessAsync(T("twitter.no_notify_entry_page"));
            return;
        }

        var users = string.Join("\n", list.Select(x => $"`{x.Target.ScreenName}`"));
        var channels = string.Join("\n", list.Select(x =>
        {
            var types = string.Join(" ", GetNotifyTypes(x).Select(t => $"`{TwitterTypeMapper.ToDisplay(t)}`"));
            return $"<#{x.ChannelId}>/{types}";
        }));
        var texts = string.Join("\n", list.Select(x => string.IsNullOrWhiteSpace(x.Text) ? $"`{T("common.empty")}`" : x.Text));

        var embed = new EmbedBuilder()
            .WithTitle(T("twitter.list_title"))
            .AddField(T("twitter.field_user"), users, true)
            .AddField(T("twitter.field_channel_type"), channels, true)
            .AddField(T("twitter.field_text"), texts, true);

        await SuccessAsync(embed);
    }

    [SlashCommand("subscribe", "Set notify tweet type subscriptions")]
    public async Task SubscribeNotifyTypeAsync(
        [Summary(description: "Twitter account name")]
        [Autocomplete(typeof(GuildNotifyAutocompleteHandler))]
        string screenName,
        [Summary(description: "Include post")]
        YesNoChoice? post = null,
        [Summary(description: "Include retweet")]
        YesNoChoice? retweet = null,
        [Summary(description: "Include quote")]
        YesNoChoice? quote = null,
        [Summary(description: "Include reply")]
        YesNoChoice? reply = null)
    {
        if (Context.Guild is null)
        {
            await ErrorAsync(T("common.guild_only"));
            return;
        }

        var user = await _notifyService.FindOrCreateUserAsync(screenName);

        await using var db = await _dbFactory.CreateDbContextAsync();
        var notify = await db.TwitterNotifies.FirstOrDefaultAsync(x =>
            x.GuildId == Context.Guild.Id.ToString() && x.TargetId == user.TwitterId);

        if (notify is null)
        {
            await SuccessAsync(T("twitter.notify_not_exists_use_create"));
            return;
        }

        notify.Post = ApplySwitch(notify.Post, post);
        notify.Retweet = ApplySwitch(notify.Retweet, retweet);
        notify.Quote = ApplySwitch(notify.Quote, quote);
        notify.Reply = ApplySwitch(notify.Reply, reply);
        EnsureAtLeastOneType(notify);
        await db.SaveChangesAsync();

        var types = GetNotifyTypes(notify);
        var msg = string.Join(" ", types.Select(x => $"`{TwitterTypeMapper.ToDisplay(x)}`"));
        await SuccessAsync(T("twitter.notify_types_for", user.ScreenName, msg));
    }

    private static bool ApplySwitch(bool current, YesNoChoice? value)
    {
        if (value is null)
        {
            return current;
        }

        return value == YesNoChoice.Yes;
    }

    private static IReadOnlyCollection<TwitterNotifyType> GetNotifyTypes(TwitterNotifyEntity notify)
        => TwitterTypeMapper.FromFlags(notify.Post, notify.Retweet, notify.Quote, notify.Reply);

    private static void EnsureAtLeastOneType(TwitterNotifyEntity notify)
    {
        if (notify.Post || notify.Retweet || notify.Quote || notify.Reply)
        {
            return;
        }

        notify.Post = true;
        notify.Quote = true;
    }

    private static bool TryExtractTextChannel(IChannel channel, out string channelId)
    {
        channelId = string.Empty;

        if (channel is not IGuildChannel)
        {
            return false;
        }

        if (channel is not ITextChannel)
        {
            return false;
        }

        channelId = channel.Id.ToString();
        return true;
    }
}
