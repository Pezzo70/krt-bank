using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infra.Persistence;

public sealed class ReadySetDbContext(DbContextOptions<ReadySetDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankDbContext).Assembly);
}
