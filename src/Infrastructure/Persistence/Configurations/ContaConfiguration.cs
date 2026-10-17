using Domain.Entities;
using Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Infrastructure.Persistence.Configurations;

internal sealed class ContaConfiguration : IEntityTypeConfiguration<Conta>
{
    public void Configure(EntityTypeBuilder<Conta> builder)
    {
        builder.ToTable("contas");
        builder.ConfigureEntity();
        builder.Property(x => x.Nome).HasColumnName("nome").IsRequired();
        builder.Property(x => x.Cpf)
            .HasColumnName("cpf")
            .HasMaxLength(11)
            .HasConversion(cpf => cpf.Value, value => Cpf.Create(value).Value)
            .IsRequired();
        builder.HasIndex(x => x.Cpf).IsUnique();
    }
}
