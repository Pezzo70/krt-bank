namespace Domain.Outbox;

public sealed record AccountOutboxPayload(
    Guid AccountId,
    string Name,
    string Cpf,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);
