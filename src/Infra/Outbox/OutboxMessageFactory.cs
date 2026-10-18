using System.Text.Json;
using Domain.Entities;
using Domain.Interfaces;

namespace Infra.Outbox;

public sealed class OutboxMessageFactory : IOutboxMessageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public OutboxMessage Create(Entity entity, string operation)
    {
        if (entity is not Account account)
        {
            throw new InvalidOperationException($"No Outbox event is defined for {entity.GetType().Name}.");
        }

        return new OutboxMessage
        {
            EntityId = account.Id,
            Type = $"account.{operation}",
            Payload = JsonSerializer.Serialize(new
            {
                AccountId = account.Id,
                account.Name,
                Cpf = account.Cpf.Value,
                account.IsActive,
                account.CreatedAt,
                account.UpdatedAt
            }, JsonOptions)
        };
    }
}
