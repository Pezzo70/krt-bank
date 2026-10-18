using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infra.Persistence;

public sealed class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        ConfigureModel(modelBuilder);

    public static void ConfigureModel(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<Account>();
        account.ToTable("accounts");
        ConfigureEntity(account);
        account.Property(x => x.Name)
            .HasColumnName("name")
            .HasConversion(name => name.Value, value => AccountName.Create(value).Value)
            .IsRequired();
        account.Property(x => x.Cpf)
            .HasColumnName("cpf")
            .HasMaxLength(11)
            .HasConversion(cpf => cpf.Value, value => Cpf.Create(value).Value)
            .IsRequired();
        account.HasIndex(x => x.Cpf).IsUnique();

        var outbox = modelBuilder.Entity<OutboxMessage>();
        outbox.ToTable("outbox_messages");
        ConfigureEntity(outbox);
        outbox.Property(x => x.Type).HasColumnName("type").IsRequired();
        outbox.Property(x => x.EntityId).HasColumnName("entity_id");
        outbox.Property(x => x.Payload).HasColumnName("payload").HasColumnType("jsonb").IsRequired();
        outbox.Property(x => x.PublishedAt).HasColumnName("published_at");
        outbox.HasIndex(x => x.CreatedAt).HasFilter("published_at IS NULL");
    }

    private static void ConfigureEntity<TEntity>(EntityTypeBuilder<TEntity> builder)
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
