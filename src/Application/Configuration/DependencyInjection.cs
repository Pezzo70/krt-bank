using Application.Workers;
using Domain.Interfaces;
using Infra.Persistence;
using Infra.Repositories;
using Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Application.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres.");
        var readySet = configuration.GetConnectionString("ReadySet")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:ReadySet.");

        postgres = new NpgsqlConnectionStringBuilder(postgres) { Timezone = "UTC" }.ConnectionString;
        readySet = new NpgsqlConnectionStringBuilder(readySet) { Timezone = "UTC" }.ConnectionString;
        services.AddDbContext<BankDbContext>(options => options.UseNpgsql(postgres));
        services.AddDbContext<ReadySetDbContext>(options =>
            options.UseNpgsql(readySet)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddLogging();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IOutboxService, OutboxService>();
        services.AddScoped<IOutboxRepository, OutboxRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddHostedService<OutboxWorker>();
        return services;
    }
}
