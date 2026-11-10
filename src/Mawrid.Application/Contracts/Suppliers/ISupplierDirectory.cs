namespace Mawrid.Application.Contracts.Suppliers;

/// <summary>
/// Published by the Suppliers module. Other modules use this instead of reading
/// supplier tables directly (ADR-0001).
/// </summary>
public interface ISupplierDirectory
{
    /// <summary>A supplier of the current organization, or null.</summary>
    Task<SupplierInfo?> FindAsync(Guid supplierId, CancellationToken cancellationToken);

    /// <summary>Suppliers of the current organization, by id. Unknown ids are left out.</summary>
    Task<IReadOnlyList<SupplierInfo>> FindManyAsync(IReadOnlyCollection<Guid> supplierIds, CancellationToken cancellationToken);
}

/// <summary>What other modules need to know about a supplier.</summary>
/// <param name="IsAwardable">Approved and every required document is valid today.</param>
/// <param name="HasPendingBankChange">A bank change waits for verification; payments must be held.</param>
public sealed record SupplierInfo(
    Guid Id,
    string NameEn,
    string NameAr,
    string VatNumber,
    bool IsAwardable,
    bool HasPendingBankChange);
