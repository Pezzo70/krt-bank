using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infra.Persistence.Configurations;

internal sealed class AccountConfiguration : IEntityTypeConfiguration<Account>
{
    public void Configure(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");
        builder.ConfigureEntity();
        builder.Property(x => x.Name).HasColumnName("name").IsRequired();
        builder.Property(x => x.Cpf)
            .HasColumnName("cpf")
            .HasMaxLength(11)
            .HasConversion(cpf => cpf.Value, value => Cpf.Create(value).Value)
            .IsRequired();
        builder.HasIndex(x => x.Cpf).IsUnique();
    }
}
