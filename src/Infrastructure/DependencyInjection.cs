using Domain.Interfaces;
using Infrastructure.Outbox;
using Infrastructure.Persistence;
using Infrastructure.Queries;
using Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var postgres = configuration.GetConnectionString("Postgres")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:Postgres.");
        var readySet = configuration.GetConnectionString("ReadySet")
            ?? throw new InvalidOperationException("Configure ConnectionStrings:ReadySet.");

        services.AddSingleton(_ => new DatabaseConnections(postgres, readySet));
        services.AddDbContext<BankDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<DatabaseConnections>().Postgres));
        services.AddLogging();
        services.AddScoped<IContaRepository, ContaRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IContaQueries, ContaQueries>();
        services.AddSingleton<IOutboxPayloadMapper, ContaOutboxPayloadMapper>();
        services.AddSingleton<OutboxMessageFactory>();
        return services;
    }
}
