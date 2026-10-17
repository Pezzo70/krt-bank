namespace Infrastructure.Outbox;

public sealed class OutboxMessage
{
    public Guid EventId { get; init; } = Guid.NewGuid();
    public required string Type { get; init; }
    public Guid EntityId { get; init; }
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    public required string Payload { get; init; }
    public DateTimeOffset? PublishedAt { get; set; }
}
