using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace Domain.Entities;

public sealed class OutboxMessage : Entity
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxMessage() { }

    [SetsRequiredMembers]
    public OutboxMessage(Account account, string operation)
    {
        EntityId = account.Id;
        Type = $"account.{operation}";
        Payload = JsonSerializer.Serialize(new
        {
            AccountId = account.Id,
            Name = account.Name.Value,
            Cpf = account.Cpf.Value,
            account.IsActive,
            account.CreatedAt,
            account.UpdatedAt
        }, JsonOptions);
    }

    public required string Type { get; init; }
    public Guid EntityId { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
}
