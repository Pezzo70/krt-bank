namespace Domain.Entities;

public abstract class Entity
{
    public int InternalId { get; private set; }
    public Guid Id { get; private set; } = Guid.NewGuid();
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? UpdatedAt { get; private set; }

    public void SetActive(bool isActive)
    {
        if (IsActive == isActive)
        {
            return;
        }

        IsActive = isActive;
        MarkAsUpdated();
    }

    protected void MarkAsUpdated() => UpdatedAt = DateTimeOffset.UtcNow;
}
