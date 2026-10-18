using Domain.Entities;
using Domain.Interfaces;
using Domain.Outbox;
using Infra.Outbox;
using Infra.Persistence;
using Infra.Queries;
using Infra.ReadySet;
using Infra.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Infra;

public static class DependencyInjection
{
    public static IServiceCollection AddInfra(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres.");
        var readySet = configuration.GetConnectionString("ReadySet")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:ReadySet.");

        services.TryAddSingleton(configuration);
        services.AddSingleton(_ => new DatabaseConnections(postgres, readySet));
        services.AddDbContext<BankDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<DatabaseConnections>().Postgres));
        services.AddDbContext<ReadySetDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<DatabaseConnections>().ReadySet)
                .UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking));
        services.AddLogging();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAccountQueries, AccountQueries>();
        services.AddSingleton<IOutboxPayloadMapper, AccountOutboxPayloadMapper>();
        services.AddSingleton<IOutboxMessageFactory, OutboxMessageFactory>();
        var startupTimeout = int.Parse(configuration["ReadySet:StartupTimeoutSeconds"] ?? "120",
            System.Globalization.CultureInfo.InvariantCulture);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(startupTimeout);
        services.AddSingleton(new ReadySetCacheOptions
        {
            PsqlPath = configuration["ReadySet:PsqlPath"] ?? "psql",
            StartupTimeoutSeconds = startupTimeout
        });
        services.AddReadySetCache<Account>("accounts_by_id",
            (context, id) => AccountReadQuery.ById(context.Accounts, id));
        return services;
    }
}
