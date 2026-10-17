namespace Domain.Models;

public sealed record ContaConsulta(
    Guid Id,
    string Nome,
    string Cpf,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
