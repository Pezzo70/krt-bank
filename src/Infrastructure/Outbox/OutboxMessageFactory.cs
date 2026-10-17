using System.Text.Json;
using Domain.Entities;

namespace Infrastructure.Outbox;

public sealed class OutboxMessageFactory(IEnumerable<IOutboxPayloadMapper> mappers)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Type, IOutboxPayloadMapper> _mappers =
        mappers.ToDictionary(mapper => mapper.EntityType);

    public OutboxMessage Create(Entity entity, Type entityType, string operation)
    {
        if (!_mappers.TryGetValue(entityType, out var mapper))
        {
            throw new InvalidOperationException($"Não há mapeamento de Outbox para {entityType.Name}.");
        }

        return new OutboxMessage
        {
            EntityId = entity.Id,
            Type = $"{mapper.EventPrefix}.{operation}",
            Payload = JsonSerializer.Serialize(mapper.Map(entity), JsonOptions)
        };
    }
}
