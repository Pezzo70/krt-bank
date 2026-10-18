using Domain.Entities;

namespace Domain.Interfaces;

public interface IOutboxMessageFactory
{
    OutboxMessage Create(Entity entity, Type entityType, string operation);
}
