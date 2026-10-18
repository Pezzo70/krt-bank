using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infra.Persistence;

public sealed class BankDbContextFactory : IDesignTimeDbContextFactory<BankDbContext>
{
    public BankDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Set ConnectionStrings__Postgres to run migrations.");
        return new BankDbContext(new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}
