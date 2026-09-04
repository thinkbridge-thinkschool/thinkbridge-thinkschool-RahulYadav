using Microsoft.EntityFrameworkCore;
using QuotesApi.Models;
using QuotesApi.Modules.Collections.Domain.Aggregates;
using QuotesApi.Modules.Collections.Infrastructure.Persistence;
using QuotesApi.Modules.Notifications.Infrastructure.Persistence;

namespace QuotesApi.Data;

public class QuotesDbContext : DbContext
{
    public QuotesDbContext(DbContextOptions<QuotesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Quote> Quotes => Set<Quote>();

    // Day 22 Piece 2: Collections owns this entity's mapping (see
    // Modules/Collections/Infrastructure/Persistence/CollectionEntityConfiguration.cs).
    // This DbContext only applies it — the same "one database, many
    // module-owned schemas" arrangement modular monoliths use in place of a
    // microservice's database-per-service split.
    public DbSet<Collection> Collections => Set<Collection>();

    // Day 22 Piece 2: owned by the Notifications module.
    public DbSet<NotificationRecord> Notifications => Set<NotificationRecord>();

    public DbSet<User> Users => Set<User>();
    

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    // Day 19: idempotency bookkeeping for the Service Bus subscription
    // workers (see Models/ProcessedMessage.cs).
    public DbSet<ProcessedMessage> ProcessedMessages => Set<ProcessedMessage>();

    // Day 20: transactional outbox rows written atomically alongside the
    // domain change they describe (see Models/OutboxMessage.cs).
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new CollectionEntityConfiguration());

        modelBuilder.ApplyConfiguration(new NotificationEntityConfiguration());

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

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(x => x.Id);

            // The relay reuses this value as the Service Bus MessageId on
            // every attempt (including retries), so no two rows may ever
            // claim the same one.
            entity.Property(x => x.MessageId)
                .IsRequired()
                .HasMaxLength(200);

            entity.HasIndex(x => x.MessageId)
                .IsUnique();

            entity.Property(x => x.EventType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(x => x.Payload)
                .IsRequired();

            // Rows with SentAtUtc == null are what the relay polls for;
            // indexing that lookup keeps it cheap as the table grows.
            entity.HasIndex(x => x.SentAtUtc);
        });
    }
}