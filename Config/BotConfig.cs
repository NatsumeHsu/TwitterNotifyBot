using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwitterNotifyBot.Config;

public sealed class BotConfig
{
    [JsonPropertyName("ms")]
    public int Ms { get; set; } = 30000;

    [JsonPropertyName("ownerId")]
    public string OwnerId { get; set; } = string.Empty;

    [JsonPropertyName("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [JsonPropertyName("token")]
    public string Token { get; set; } = string.Empty;

    [JsonPropertyName("helpurl")]
    public string HelpUrl { get; set; } = string.Empty;

    [JsonPropertyName("twitter")]
    public TwitterCookieConfig Twitter { get; set; } = new();

        private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static BotConfig Load(string configPath)
    {
        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException(
                $"Config file not found: {configPath}. Copy data/temp-config.json to data/config.json and fill values.",
                configPath);
        }

        using var stream = File.OpenRead(configPath);
        var config = JsonSerializer.Deserialize<BotConfig>(stream, JsonOptions)
                     ?? throw new InvalidDataException($"Invalid config JSON: {configPath}");

        config.Token = config.Token.Trim();
        config.OwnerId = config.OwnerId.Trim();
        config.GuildId = config.GuildId.Trim();
        config.HelpUrl = config.HelpUrl.Trim();
        config.Twitter.AuthToken = config.Twitter.AuthToken.Trim();
        config.Twitter.Ct0 = config.Twitter.Ct0.Trim();

        if (string.IsNullOrWhiteSpace(config.Token))
        {
            throw new InvalidDataException("Config token is empty.");
        }

        if (config.Ms < 5000)
        {
            config.Ms = 5000;
        }

        return config;
    }

}

public sealed class TwitterCookieConfig
{
    [JsonPropertyName("auth_token")]
    public string AuthToken { get; set; } = string.Empty;

    [JsonPropertyName("ct0")]
    public string Ct0 { get; set; } = string.Empty;
}
