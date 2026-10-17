namespace Domain.Entities;

public abstract class Entity
{
    private readonly TimeProvider _timeProvider;

    protected Entity(TimeProvider? timeProvider = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        CreatedAt = _timeProvider.GetUtcNow();
    }

    public int InternalId { get; private set; }
    public Guid Id { get; } = Guid.NewGuid();
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        MarkAsUpdated();
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        MarkAsUpdated();
    }

    protected void MarkAsUpdated() => UpdatedAt = _timeProvider.GetUtcNow();
}
