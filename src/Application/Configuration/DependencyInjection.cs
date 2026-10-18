using Domain.Interfaces;
using Infra.Outbox;
using Infra.Persistence;
using Infra.Repositories;
using Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Application.Configuration;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
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
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddSingleton<IOutboxMessageFactory, OutboxMessageFactory>();
        return services;
    }
}
