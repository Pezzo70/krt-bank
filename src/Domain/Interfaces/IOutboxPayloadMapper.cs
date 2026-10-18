using Domain.Entities;

namespace Domain.Interfaces;

public interface IOutboxPayloadMapper
{
    Type EntityType { get; }
    string EventPrefix { get; }
    object Map(Entity entity);
}
