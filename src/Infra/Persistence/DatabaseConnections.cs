using Npgsql;

namespace Infra.Persistence;

public sealed class DatabaseConnections(string postgresConnectionString, string readySetConnectionString)
    : IDisposable, IAsyncDisposable
{
    public NpgsqlDataSource Postgres { get; } = Create(postgresConnectionString);
    public NpgsqlDataSource ReadySet { get; } = Create(readySetConnectionString);

    private static NpgsqlDataSource Create(string connectionString) =>
        NpgsqlDataSource.Create(new NpgsqlConnectionStringBuilder(connectionString)
        {
            Timezone = "UTC"
        }.ConnectionString);

    public void Dispose()
    {
        ReadySet.Dispose();
        Postgres.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await ReadySet.DisposeAsync();
        await Postgres.DisposeAsync();
    }
}
