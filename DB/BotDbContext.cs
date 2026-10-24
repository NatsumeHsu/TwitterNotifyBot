using Microsoft.EntityFrameworkCore;

namespace TwitterNotifyBot.DB;

public sealed class BotDbContext(DbContextOptions<BotDbContext> options) : DbContext(options)
{
    public DbSet<TwitterUserEntity> TwitterUsers => Set<TwitterUserEntity>();
    public DbSet<TwitterNotifyEntity> TwitterNotifies => Set<TwitterNotifyEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<TwitterUserEntity>(entity =>
        {
            entity.HasIndex(x => x.TwitterId).IsUnique();
            entity.Property(x => x.LastTweet).HasDefaultValue("0");
            entity.Property(x => x.Enabled).HasDefaultValue(true);
        });

        modelBuilder.Entity<TwitterNotifyEntity>(entity =>
        {
            entity.Property(x => x.Text).HasDefaultValue(string.Empty);
            entity.Property(x => x.Post).HasDefaultValue(true);
            entity.Property(x => x.Retweet).HasDefaultValue(false);
            entity.Property(x => x.Quote).HasDefaultValue(true);
            entity.Property(x => x.Reply).HasDefaultValue(false);
            entity.HasOne(x => x.Target)
                .WithMany(x => x.Notifies)
                .HasPrincipalKey(x => x.TwitterId)
                .HasForeignKey(x => x.TargetId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
