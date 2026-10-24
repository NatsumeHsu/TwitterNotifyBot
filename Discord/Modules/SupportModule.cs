using Discord;
using Discord.Interactions;
using TwitterNotifyBot.Config;
using TwitterNotifyBot.Services;

namespace TwitterNotifyBot.Discord.Modules;

public sealed class SupportModule(
    BotConfig config,
    BotI18n i18n) : BaseSlashModule(i18n)
{

    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("report", "Send report to owner DM")]
    public async Task ReportAsync([Summary(description: "Report message")] string message)
    {
        if (!ulong.TryParse(config.OwnerId, out var ownerId))
        {
            await ErrorAsync(T("support.owner_id_not_configured"));
            return;
        }

        var owner = await Context.Client.GetUserAsync(ownerId);
        if (owner is null)
        {
            await ErrorAsync(T("support.owner_not_found"));
            return;
        }

        var embed = new EmbedBuilder()
            .WithColor(Color.Blue)
            .WithDescription(message)
            .WithAuthor(Context.User.Username, Context.User.GetAvatarUrl() ?? Context.User.GetDefaultAvatarUrl());

        await owner.SendMessageAsync(embed: embed.Build());
        await SuccessAsync(T("support.report_sent"));
    }


    [DefaultMemberPermissions(GuildPermission.Administrator)]
    [SlashCommand("help", "Show help image")]
    public async Task HelpAsync()
    {
        var embed = new EmbedBuilder()
            .WithDescription($"[Help image]({config.HelpUrl})")
            .WithImageUrl(config.HelpUrl);

        await SuccessAsync(embed, ephemeral: false);
    }

    [SlashCommand("invite", "Bot invite link")]
    public async Task InviteAsync()
    {
        var appId = Context.Client.CurrentUser.Id;
        await SuccessAsync($"https://discord.com/api/oauth2/authorize?client_id={appId}&permissions=8&scope=bot+applications.commands");
    }
}
