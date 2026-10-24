using Discord;
using Discord.Interactions;
using TwitterNotifyBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TwitterNotifyBot.Discord.Autocomplete;

public sealed class GuildNotifyAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var guildId = context.Guild?.Id.ToString() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(guildId))
        {
            return AutocompletionResult.FromSuccess();
        }

        var svc = services.GetRequiredService<TwitterNotifyService>();
        var current = autocompleteInteraction.Data.Current?.Value?.ToString() ?? string.Empty;
        var names = await svc.SearchGuildNotifyScreenNamesAsync(guildId, current);

        return AutocompletionResult.FromSuccess(names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(25)
            .Select(x => new AutocompleteResult(x, x)));
    }
}
