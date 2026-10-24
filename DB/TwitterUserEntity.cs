using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TwitterNotifyBot.DB;

[Table("twitter_user")]
public sealed class TwitterUserEntity
{
    [Key]
    [Column("idx")]
    public int Idx { get; set; }

    [Required]
    [Column("id")]
    public string TwitterId { get; set; } = string.Empty;

    [Required]
    [Column("name")]
    public string Name { get; set; } = string.Empty;

    [Required]
    [Column("screen_name")]
    public string ScreenName { get; set; } = string.Empty;

    [Required]
    [Column("icon")]
    public string Icon { get; set; } = string.Empty;

    [Required]
    [Column("last_tweet")]
    public string LastTweet { get; set; } = "0";

    [Column("enabled")]
    public bool Enabled { get; set; } = true;

    public List<TwitterNotifyEntity> Notifies { get; set; } = new();

    [NotMapped]
    public string Url => $"https://x.com/{ScreenName}";
}
