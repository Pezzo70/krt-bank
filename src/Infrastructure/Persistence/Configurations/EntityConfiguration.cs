using Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal static class EntityConfiguration
{
    public static void ConfigureEntity<TEntity>(this EntityTypeBuilder<TEntity> builder)
        where TEntity : Entity
    {
        builder.HasKey(x => x.InternalId);
        builder.Property(x => x.InternalId).HasColumnName("internal_id").UseIdentityByDefaultColumn();
        builder.Property(x => x.Id).HasColumnName("id").ValueGeneratedNever();
        builder.HasIndex(x => x.Id).IsUnique();
        builder.Property(x => x.IsActive).HasColumnName("is_active").IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        builder.HasQueryFilter(x => x.IsActive);
    }
}
