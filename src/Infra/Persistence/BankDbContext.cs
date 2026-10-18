using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infra.Persistence;

public sealed class BankDbContext(DbContextOptions<BankDbContext> options) : DbContext(options)
{
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(BankDbContext).Assembly);
}
