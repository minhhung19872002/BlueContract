namespace BlueProcure.Domain.Common;

/// <summary>
/// Base type for all persisted entities. Ids are UUID v7 (time-ordered, not guessable as sequential ints).
/// Audit columns are filled by the persistence layer; RowVersion maps to PostgreSQL xmin.
/// </summary>
public abstract class Entity
{
    protected Entity()
    {
        Id = Guid.CreateVersion7();
    }

    public Guid Id { get; protected set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public bool IsDeleted { get; private set; }

    public uint RowVersion { get; private set; }

    public void MarkDeleted() => IsDeleted = true;

    public void Restore() => IsDeleted = false;
}
