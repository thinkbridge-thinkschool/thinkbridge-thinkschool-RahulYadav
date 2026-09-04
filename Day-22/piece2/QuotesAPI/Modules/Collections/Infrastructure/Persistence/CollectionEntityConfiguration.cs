using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using QuotesApi.Modules.Collections.Domain.Aggregates;

namespace QuotesApi.Modules.Collections.Infrastructure.Persistence;

// The Collections module owns this mapping; QuotesDbContext only applies it
// (see Data/QuotesDbContext.cs). Table/column names match the original
// flat Models.Collection mapping so this restructuring is a namespace move,
// not a data migration.
public sealed class CollectionEntityConfiguration : IEntityTypeConfiguration<Collection>
{
    public void Configure(EntityTypeBuilder<Collection> builder)
    {
        builder.ToTable("Collections");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name)
            .IsRequired()
            .HasMaxLength(80);

        builder.Property(x => x.OwnerId)
            .IsRequired();

        builder.OwnsMany(x => x.QuoteMemberships, membership =>
        {
            membership.ToTable("CollectionItems");

            membership.WithOwner()
                .HasForeignKey("CollectionId");

            membership.Property(x => x.QuoteId)
                .IsRequired();

            membership.Property(x => x.AddedAtUtc)
                .IsRequired()
                .HasColumnName("AddedAt");

            membership.HasKey("CollectionId", "QuoteId");
        });
    }
}
