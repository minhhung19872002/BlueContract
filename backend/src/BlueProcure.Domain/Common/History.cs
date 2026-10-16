namespace BlueProcure.Domain.Common;

/// <summary>Marks master data whose every change is versioned into a *_history table (spec §5).</summary>
public interface IHasHistory
{
}

public enum HistoryChangeType
{
    Created = 0,
    Updated = 1,
    Deleted = 2,
}

/// <summary>Immutable snapshot of a master-data row after a change.</summary>
public abstract class HistoryRecord
{
    protected HistoryRecord()
    {
        Id = Guid.CreateVersion7();
        Snapshot = "{}";
    }

    public Guid Id { get; private set; }

    public Guid EntityId { get; set; }

    public int Version { get; set; }

    public HistoryChangeType ChangeType { get; set; }

    public DateTimeOffset ChangedAt { get; set; }

    public Guid? ChangedBy { get; set; }

    public string? ChangedByName { get; set; }

    /// <summary>JSON snapshot of the entity's scalar properties after the change.</summary>
    public string Snapshot { get; set; }
}

public sealed class DepartmentHistory : HistoryRecord
{
}

public sealed class UserHistory : HistoryRecord
{
}

public sealed class SupplierHistory : HistoryRecord
{
}
