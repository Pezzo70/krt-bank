using Domain.Entities;

namespace Infrastructure.Outbox;

public interface IOutboxPayloadMapper
{
    Type EntityType { get; }
    string EventPrefix { get; }
    object Map(Entity entity);
}
