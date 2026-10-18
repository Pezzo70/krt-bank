using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infra.Persistence.Configurations;

internal sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("outbox_messages");
        builder.ConfigureEntity();
        builder.Property(x => x.Type).HasColumnName("type").IsRequired();
        builder.Property(x => x.EntityId).HasColumnName("entity_id");
        builder.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.PublishedAt).HasColumnName("published_at");
        builder.HasIndex(x => x.CreatedAt).HasFilter("published_at IS NULL");
    }
}
