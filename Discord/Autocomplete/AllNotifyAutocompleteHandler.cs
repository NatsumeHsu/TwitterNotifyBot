using Discord;
using Discord.Interactions;
using TwitterNotifyBot.Services;
using Microsoft.Extensions.DependencyInjection;

namespace TwitterNotifyBot.Discord.Autocomplete;

public sealed class AllNotifyAutocompleteHandler : AutocompleteHandler
{
    public override async Task<AutocompletionResult> GenerateSuggestionsAsync(
        IInteractionContext context,
        IAutocompleteInteraction autocompleteInteraction,
        IParameterInfo parameter,
        IServiceProvider services)
    {
        var svc = services.GetRequiredService<TwitterNotifyService>();
        var current = autocompleteInteraction.Data.Current?.Value?.ToString() ?? string.Empty;
        var names = await svc.SearchAllNotifyScreenNamesAsync(current);

        return AutocompletionResult.FromSuccess(names
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Take(25)
            .Select(x => new AutocompleteResult(x, x)));
    }
}
