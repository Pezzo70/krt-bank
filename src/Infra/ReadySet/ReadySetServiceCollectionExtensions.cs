using Infra.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Infra.ReadySet;

public static class ReadySetServiceCollectionExtensions
{
    public static IServiceCollection AddReadySetCache<TEntity>(this IServiceCollection services,
        string name, Func<BankDbContext, Guid, IQueryable<TEntity>> query) where TEntity : class
    {
        services.AddSingleton(new ReadySetCacheDefinition(name, context => query(context, Guid.Empty)));
        services.TryAddSingleton<IReadySetAdminClient, PsqlReadySetAdminClient>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, ReadySetCacheInitializer>());
        return services;
    }
}
