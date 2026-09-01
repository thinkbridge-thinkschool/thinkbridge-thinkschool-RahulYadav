using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;

namespace QuotesApi.Data;

public class QuotesDbContext : DbContext
{
    public QuotesDbContext(DbContextOptions<QuotesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();

    public DbSet<Collection> Collections => Set<Collection>();

    public DbSet<User> Users => Set<User>();
    

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Day 19: idempotency bookkeeping for the Service Bus subscription
    // workers (see Models/ProcessedMessage.cs).
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Collection>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(80);

            entity.Property(x => x.OwnerId)
                .IsRequired();

            entity.OwnsMany(x => x.Items, item =>
            {
                item.ToTable("CollectionItems");

                item.WithOwner()
                    .HasForeignKey("CollectionId");

                item.Property(x => x.QuoteId)
                    .IsRequired();

                item.Property(x => x.AddedAt)
                    .IsRequired();

                item.HasKey("CollectionId", "QuoteId");
            });
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.Token)
                .IsRequired();

            entity.Property(x => x.UserId)
                .IsRequired();

            entity.Property(x => x.ExpiresAt)
                .IsRequired();
        });

        modelBuilder.Entity<ProcessedMessage>(entity =>
        {
            // Composite key: each subscription independently tracks which
            // MessageIds it has already handled (see ProcessedMessage.cs).
            entity.HasKey(x => new { x.SubscriptionName, x.MessageId });

            entity.Property(x => x.SubscriptionName)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.MessageId)
                .IsRequired()
                .HasMaxLength(200);
        });
    }
}