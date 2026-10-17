using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Infrastructure.Persistence;

public sealed class BankDbContextFactory : IDesignTimeDbContextFactory<BankDbContext>
{
    public BankDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? throw new InvalidOperationException("Defina ConnectionStrings__Postgres para executar migrations.");
        return new BankDbContext(new DbContextOptionsBuilder<BankDbContext>()
            .UseNpgsql(connectionString).Options);
    }
}
