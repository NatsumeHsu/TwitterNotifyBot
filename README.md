# TwitterNotifyBot (.NET 10 + Discord.Net)

## Setup
1. Copy `data/config.json.temp` to `data/config.json`.
2. Fill `token`, `guildId` (optional), `ownerId`, and `twitter.auth_token` / `twitter.ct0`.
3. Run `dotnet restore`.
4. Run `dotnet run`.

## Notes
- Target framework: `net10.0`.
- Database: SQLite (`data/database.db`) via EF Core.
- Slash commands are registered to `guildId` when provided; otherwise registered globally.
- Slash command localization uses `data/commands.<locale>.json` (for example `data/commands.zh-TW.json`).

## Example `config.json`
```
{
  "ms": 30000,
  "ownerId": "",
  "guildId": "",
  "token": "",
  "twitter": {
    "auth_token": "",
    "ct0": ""
  }
}
```
