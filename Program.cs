using TwitterNotifyBot.Config;
using TwitterNotifyBot.DB;
using TwitterNotifyBot.Discord;
using TwitterNotifyBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
Directory.CreateDirectory(dataDir);

var services = new ServiceCollection()
    .AddDbContextFactory<BotDbContext>(options => options.UseSqlite($"Data Source={Path.Combine(dataDir, "database.db")}"))
    .AddSingleton(BotConfig.Load(Path.Combine(dataDir, "config.json")))
    .AddSingleton<TwitterNotifyService>()
    .AddSingleton<BotI18n>()
    .AddSingleton<Bot>();

services.AddHttpClient<TwitterApiClient>();

using var provider = services.BuildServiceProvider();
var bot = provider.GetRequiredService<Bot>();

using var shutdownCts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    shutdownCts.Cancel();
};

await bot.RunAndBlockAsync(shutdownCts.Token);
