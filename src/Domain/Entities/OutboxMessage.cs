namespace Domain.Entities;

public sealed class OutboxMessage : Entity
{
    public required string Type { get; init; }
    public Guid EntityId { get; init; }
    public required string Payload { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
}
