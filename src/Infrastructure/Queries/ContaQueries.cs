using Domain.Interfaces;
using Domain.Models;
using Infrastructure.Persistence;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Infrastructure.Queries;

public sealed class ContaQueries(DatabaseConnections connections, ILogger<ContaQueries> logger) : IContaQueries
{
    // A mesma consulta deve ser registrada por scripts/readyset-cache.sql.
    public const string GetByIdSql = """
        SELECT id, nome, cpf, is_active, created_at, updated_at
        FROM public.contas
        WHERE id = $1 AND is_active = TRUE
        """;

    public async Task<ContaConsulta?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        try
        {
            return await QueryAsync(connections.ReadySet, id, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested &&
            exception is NpgsqlException { IsTransient: true } or TimeoutException)
        {
            // Não registra a exceção/conexão para não expor credenciais ou parâmetros.
            logger.LogWarning("ReadySet indisponível ({ErrorType}); consulta encaminhada ao PostgreSQL.",
                exception.GetType().Name);
            return await QueryAsync(connections.Postgres, id, cancellationToken);
        }
    }

    private static async Task<ContaConsulta?> QueryAsync(
        NpgsqlDataSource source, Guid id, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand(GetByIdSql);
        command.Parameters.Add(new NpgsqlParameter<Guid> { TypedValue = id });
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ContaConsulta(
            reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetBoolean(3),
            reader.GetFieldValue<DateTimeOffset>(4),
            reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5));
    }
}
