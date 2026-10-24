using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using TwitterNotifyBot.Config;
using TwitterNotifyBot.DB;
using TwitterNotifyBot.Services;
using TwitterNotifyBot.Utils;
using Microsoft.EntityFrameworkCore;
using System.Diagnostics;
using System.Reflection;

namespace TwitterNotifyBot.Discord;

public sealed class Bot
{
    private readonly IServiceProvider _services;
    private readonly BotConfig _config;
    private readonly TwitterNotifyService _notifyService;
    private readonly IDbContextFactory<BotDbContext> _dbFactory;
    private readonly BotI18n _i18n;

    private readonly DiscordSocketClient _client;
    private readonly InteractionService _interactions;

    private CancellationTokenSource? _presenceCts;
    private Task? _presenceTask;
    private bool _eventsBound;
    private bool _modulesLoaded;
    private bool _started;

    public DiscordSocketClient Client => _client;

    public Bot(
        IServiceProvider services,
        BotConfig config,
        TwitterNotifyService notifyService,
        IDbContextFactory<BotDbContext> dbFactory,
        BotI18n i18n)
    {
        _services = services;
        _config = config;
        _notifyService = notifyService;
        _dbFactory = dbFactory;
        _i18n = i18n;

        _client = new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds | GatewayIntents.DirectMessages,
        });

        _interactions = new InteractionService(_client.Rest, new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Info,
            DefaultRunMode = RunMode.Async,
            EnableAutocompleteHandlers = true,
            UseCompiledLambda = true,
            LocalizationManager = new JsonLocalizationManager(
                Path.Combine(AppContext.BaseDirectory, "data"),
                "commands")
        });
    }

    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        BindEvents();

        var sw = Stopwatch.StartNew();
        var loginTask = LoginAndWaitReadyAsync(cancellationToken);
        var modulesTask = LoadModulesAsync(cancellationToken);
        await Task.WhenAll(loginTask, modulesTask);

        await InitializeAfterReadyAsync(cancellationToken);

        sw.Stop();
        Logger.Info($"Bot ready in {sw.Elapsed.TotalSeconds:F2}s");
    }

    public async Task RunAndBlockAsync(CancellationToken cancellationToken = default)
    {
        await RunAsync(cancellationToken);
        Logger.Info("Bot started. Press Ctrl+C to stop.");

        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }

        await StopAsync();
    }

    public async Task StopAsync()
    {
        _started = false;
        _presenceCts?.Cancel();

        if (_presenceTask is not null)
        {
            try
            {
                await _presenceTask;
            }
            catch (OperationCanceledException)
            {
                // shutdown
            }
        }

        await _notifyService.StopAsync();

        try
        {
            await _client.StopAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Discord client stop failed.");
        }

        try
        {
            await _client.LogoutAsync();
        }
        catch (Exception ex)
        {
            Logger.Warn(ex, "Discord client logout failed.");
        }

        _presenceTask = null;
        _presenceCts = null;
    }

    private void BindEvents()
    {
        if (_eventsBound)
        {
            return;
        }

        _eventsBound = true;
        _client.Log += LogAsync;
        _interactions.Log += LogAsync;
        _client.InteractionCreated += OnInteractionCreatedAsync;
        _interactions.InteractionExecuted += OnInteractionExecutedAsync;
    }

    private async Task LoginAndWaitReadyAsync(CancellationToken cancellationToken)
    {
        var clientReady = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task SetClientReady()
        {
            clientReady.TrySetResult();
            return Task.CompletedTask;
        }

        Logger.Info("Logging in...");
        _client.Ready += SetClientReady;

        try
        {
            await _client.LoginAsync(TokenType.Bot, _config.Token);
            await _client.StartAsync();

            using var registration = cancellationToken.Register(() => clientReady.TrySetCanceled(cancellationToken));
            await clientReady.Task.ConfigureAwait(false);
        }
        finally
        {
            _client.Ready -= SetClientReady;
        }
    }

    private async Task LoadModulesAsync(CancellationToken cancellationToken)
    {
        if (_modulesLoaded)
        {
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();
        await _interactions.AddModulesAsync(Assembly.GetExecutingAssembly(), _services);
        _modulesLoaded = true;
    }

    private async Task InitializeAfterReadyAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using (var db = await _dbFactory.CreateDbContextAsync(cancellationToken))
        {
            await db.Database.EnsureCreatedAsync(cancellationToken);
        }

        await RegisterCommandsAsync();
        await _notifyService.InitializeAsync(_client, cancellationToken);

        _presenceCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _presenceTask = Task.Run(() => PresenceLoopAsync(_presenceCts.Token), _presenceCts.Token);

        Logger.Info($"Bot logged in as {_client.CurrentUser.Username}");
    }

    private async Task RegisterCommandsAsync()
    {
        await _interactions.RegisterCommandsGloballyAsync(deleteMissing: true);
        Logger.Info("Commands registered globally");
    }

    private async Task PresenceLoopAsync(CancellationToken ct)
    {
        var flip = false;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));

        await UpdatePresenceAsync(flip, ct);
        while (await timer.WaitForNextTickAsync(ct))
        {
            flip = !flip;
            await UpdatePresenceAsync(flip, ct);
        }
    }

    private async Task UpdatePresenceAsync(bool flip, CancellationToken ct)
    {
        var text = flip
            ? $"Guilds: {_client.Guilds.Count}"
            : $"Users: {await GetEnabledUserCountAsync(ct)}";

        await _client.SetActivityAsync(new Game(text, ActivityType.Watching));
    }

    private async Task<int> GetEnabledUserCountAsync(CancellationToken ct)
    {
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        return await db.TwitterUsers.CountAsync(x => x.Enabled, ct);
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        try
        {
            var context = new SocketInteractionContext(_client, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Interaction execution crashed.");

            if (interaction.Type == InteractionType.ApplicationCommand && !interaction.HasResponded)
            {
                await interaction.RespondAsync(_i18n.T(interaction, "common.command_failed"), ephemeral: true);
            }
        }
    }

    private Task OnInteractionExecutedAsync(ICommandInfo? command, IInteractionContext context, IResult result)
    {
        if (result.IsSuccess)
        {
            return Task.CompletedTask;
        }

        var commandName = command?.Name ?? "(unknown)";
        var reason = string.IsNullOrWhiteSpace(result.ErrorReason)
            ? "(no reason)"
            : result.ErrorReason;

        Logger.Warn($"Interaction failed: {context.Interaction.Type} {commandName} {reason}");
        return Task.CompletedTask;
    }

    private Task LogAsync(LogMessage msg)
    {
        var text = $"[{msg.Source}] {msg.Message}";
        switch (msg.Severity)
        {
            case LogSeverity.Critical:
            case LogSeverity.Error:
                if (msg.Exception is not null)
                {
                    Logger.Error(msg.Exception, text);
                }
                else
                {
                    Logger.Error(text);
                }
                break;
            case LogSeverity.Warning:
                if (msg.Exception is not null)
                {
                    Logger.Warn(msg.Exception, text);
                }
                else
                {
                    Logger.Warn(text);
                }
                break;
            default:
                if (msg.Exception is not null)
                {
                    Logger.Info($"{text}{Environment.NewLine}{msg.Exception}");
                }
                else
                {
                    Logger.Info(text);
                }
                break;
        }

        return Task.CompletedTask;
    }
}
