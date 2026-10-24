using Discord;
using Discord.Interactions;
using TwitterNotifyBot.Services;

namespace TwitterNotifyBot.Discord.Modules;

public abstract class BaseSlashModule(BotI18n i18n) : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotI18n _i18n = i18n;

    protected string T(string key, params object[] args)
        => _i18n.T(Context?.Interaction, key, args);

    protected Task SuccessAsync(string message, bool ephemeral = true)
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.Green)
            .WithDescription(message)
            .Build();

        return RespondEmbedAsync(embed, ephemeral);
    }

    protected Task SuccessAsync(EmbedBuilder builder, bool ephemeral = true)
    {
        builder.Color ??= Color.Green;
        return RespondEmbedAsync(builder.Build(), ephemeral);
    }

    protected Task ErrorAsync(string message, bool ephemeral = true)
    {
        var embed = new EmbedBuilder()
            .WithColor(Color.DarkRed)
            .WithDescription(message)
            .Build();

        return RespondEmbedAsync(embed, ephemeral);
    }

    protected async Task RespondEmbedAsync(Embed embed, bool ephemeral)
    {
        if (Context.Interaction.HasResponded)
        {
            await FollowupAsync(embed: embed, ephemeral: ephemeral);
            return;
        }

        await RespondAsync(embed: embed, ephemeral: ephemeral);
    }
}
