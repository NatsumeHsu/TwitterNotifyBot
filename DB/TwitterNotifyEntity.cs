using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TwitterNotifyBot.DB;

[Table("twitter_notify")]
public sealed class TwitterNotifyEntity
{
    [Key]
    [Column("idx")]
    public int Idx { get; set; }

    [Required]
    [Column("guildId")]
    public string GuildId { get; set; } = string.Empty;

    [Required]
    [Column("channelId")]
    public string ChannelId { get; set; } = string.Empty;

    [Required]
    [Column("text")]
    public string Text { get; set; } = string.Empty;

    [Required]
    [Column("targetId")]
    public string TargetId { get; set; } = string.Empty;

    [Column("post")]
    public bool Post { get; set; } = true;

    [Column("retweet")]
    public bool Retweet { get; set; }

    [Column("quote")]
    public bool Quote { get; set; } = true;

    [Column("reply")]
    public bool Reply { get; set; }

    public TwitterUserEntity Target { get; set; } = null!;

    [NotMapped]
    public string? TextOrNull => string.IsNullOrWhiteSpace(Text) ? null : Text;
}
