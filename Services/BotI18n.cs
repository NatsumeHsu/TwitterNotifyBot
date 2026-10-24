using System.Globalization;
using Discord;

namespace TwitterNotifyBot.Services;

public sealed class BotI18n
{
    private static readonly Dictionary<string, string> ZhTw = new(StringComparer.Ordinal)
    {
        ["common.command_failed"] = "指令執行失敗。",
        ["common.guild_only"] = "此指令只能在伺服器中使用。",
        ["common.invalid_channel_id"] = "頻道 ID 格式不正確。",
        ["common.channel_must_text"] = "頻道必須是伺服器文字/公告頻道。",
        ["common.empty"] = "（空）",

        ["support.notify_exists"] = "通知已存在。",
        ["support.notify_inserted"] = "已為 @{0} 新增通知。",
        ["support.user_not_found"] = "找不到使用者。",
        ["support.deleted_user_and_notify"] = "已刪除 @{0} 與其所有通知設定。",
        ["support.no_enabled_users_page"] = "這一頁沒有啟用中的使用者。",
        ["support.disabled_user"] = "已停用 @{0}。",
        ["support.no_disabled_users"] = "目前沒有停用中的使用者。",
        ["support.disabled_users"] = "停用使用者（{0}）：\n{1}",
        ["support.owner_id_not_configured"] = "data/config.json 尚未設定 ownerId。",
        ["support.owner_not_found"] = "找不到擁有者帳號。",
        ["support.report_sent"] = "回報已送出。",

        ["twitter.notify_exists_use_update"] = "通知已存在，請使用 /通知更新。",
        ["twitter.notify_created"] = "已為 @{0} 建立通知。",
        ["twitter.notify_not_exists_use_create"] = "通知不存在，請使用 /通知新增。",
        ["twitter.updated_notify_for"] = "已更新 @{0} 的通知設定",
        ["twitter.notify_not_exists"] = "通知不存在。",
        ["twitter.notify_removed"] = "已移除 @{0} 的通知。",
        ["twitter.no_notify_entry_page"] = "這一頁沒有通知資料。",
        ["twitter.list_title"] = "Twitter 通知列表",
        ["twitter.field_user"] = "使用者",
        ["twitter.field_channel_type"] = "頻道/類型",
        ["twitter.field_text"] = "文字",
        ["twitter.field_channel"] = "頻道",
        ["twitter.notify_types_for"] = "@{0} 的通知類型：{1}",
        ["twitter.type.post"] = "貼文",
        ["twitter.type.retweet"] = "轉推",
        ["twitter.type.quote"] = "引用",
        ["twitter.type.reply"] = "回覆",
    };

    private static readonly Dictionary<string, string> EnUs = new(StringComparer.Ordinal)
    {
        ["common.command_failed"] = "Command failed.",
        ["common.guild_only"] = "Guild only command.",
        ["common.invalid_channel_id"] = "Invalid channel id.",
        ["common.channel_must_text"] = "Channel must be a guild text/announcement channel.",
        ["common.empty"] = "(empty)",

        ["support.notify_exists"] = "Notify already exists.",
        ["support.notify_inserted"] = "Notify inserted for @{0}.",
        ["support.user_not_found"] = "User not found.",
        ["support.deleted_user_and_notify"] = "Deleted @{0} and related notify entries.",
        ["support.no_enabled_users_page"] = "No enabled users in this page.",
        ["support.disabled_user"] = "Disabled @{0}.",
        ["support.no_disabled_users"] = "No disabled users.",
        ["support.disabled_users"] = "Disabled users ({0}):\n{1}",
        ["support.owner_id_not_configured"] = "ownerId is not configured in data/config.json.",
        ["support.owner_not_found"] = "Owner user not found.",
        ["support.report_sent"] = "Report sent.",

        ["twitter.notify_exists_use_update"] = "Notify already exists. Use /notify-update.",
        ["twitter.notify_created"] = "Notify created for @{0}.",
        ["twitter.notify_not_exists_use_create"] = "Notify does not exist. Use /notify-create.",
        ["twitter.updated_notify_for"] = "Updated notify for @{0}",
        ["twitter.notify_not_exists"] = "Notify does not exist.",
        ["twitter.notify_removed"] = "Notify removed for @{0}.",
        ["twitter.no_notify_entry_page"] = "No notify entry in this page.",
        ["twitter.list_title"] = "Twitter notify list",
        ["twitter.field_user"] = "User",
        ["twitter.field_channel_type"] = "Channel/Type",
        ["twitter.field_text"] = "Text",
        ["twitter.field_channel"] = "Channel",
        ["twitter.notify_types_for"] = "Notify types for @{0}: {1}",
        ["twitter.type.post"] = "Post",
        ["twitter.type.retweet"] = "Retweet",
        ["twitter.type.quote"] = "Quote",
        ["twitter.type.reply"] = "Reply",
    };

    public string T(IDiscordInteraction? interaction, string key, params object[] args)
    {
        var locale = interaction?.UserLocale;
        if (string.IsNullOrWhiteSpace(locale))
        {
            locale = interaction?.GuildLocale;
        }

        return T(locale, key, args);
    }

    public string T(string? locale, string key, params object[] args)
    {
        var table = IsChinese(locale) ? ZhTw : EnUs;
        if (!table.TryGetValue(key, out var template))
        {
            template = EnUs.TryGetValue(key, out var fallback) ? fallback : key;
        }

        return args.Length == 0
            ? template
            : string.Format(CultureInfo.InvariantCulture, template, args);
    }

    private static bool IsChinese(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return true;
        }

        return locale.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }
}
