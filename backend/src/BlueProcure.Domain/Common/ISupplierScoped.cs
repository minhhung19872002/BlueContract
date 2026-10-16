namespace BlueProcure.Domain.Common;

/// <summary>
/// Business data that belongs to exactly one supplier (contracts, call orders, receipts...).
/// Supplier users only ever see rows with their own SupplierId — enforced by an EF Core
/// global query filter, not just the UI (spec §4.2).
/// </summary>
public interface ISupplierScoped
{
    Guid SupplierId { get; }
}
