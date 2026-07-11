namespace Arquetipo.Domain.Abstractions;

public abstract class AuditableEntity<TId> : Entity<TId>
{
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    protected AuditableEntity() { }
    protected AuditableEntity(TId id) : base(id) { }

    public void MarkUpdated() => UpdatedAt = DateTime.UtcNow;
    public void SoftDelete() => IsDeleted = true;
}