using System.Text.Json;
using Domain.Entities;
using Domain.Interfaces;

namespace Infra.Outbox;

public sealed class OutboxMessageFactory(IEnumerable<IOutboxPayloadMapper> mappers) : IOutboxMessageFactory
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly Dictionary<Type, IOutboxPayloadMapper> _mappers =
        mappers.ToDictionary(mapper => mapper.EntityType);

    public OutboxMessage Create(Entity entity, Type entityType, string operation)
    {
        if (!_mappers.TryGetValue(entityType, out var mapper))
        {
            throw new InvalidOperationException($"No Outbox mapping registered for {entityType.Name}.");
        }

        return new OutboxMessage
        {
            EntityId = entity.Id,
            Type = $"{mapper.EventPrefix}.{operation}",
            Payload = JsonSerializer.Serialize(mapper.Map(entity), JsonOptions)
        };
    }
}
