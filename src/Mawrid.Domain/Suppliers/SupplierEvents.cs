using Mawrid.Domain.Common;

namespace Mawrid.Domain.Suppliers;

public sealed record SupplierRegistered(Guid SupplierId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierSubmittedForReview(Guid SupplierId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierApproved(Guid SupplierId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierRejected(Guid SupplierId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierBlocked(Guid SupplierId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierUnblocked(Guid SupplierId, Guid OrganizationId) : IDomainEvent;

public sealed record SupplierBankAccountRequested(Guid SupplierId, Guid OrganizationId, Guid BankAccountId) : IDomainEvent;

public sealed record SupplierBankAccountVerified(Guid SupplierId, Guid OrganizationId, Guid BankAccountId) : IDomainEvent;

/// <param name="DaysLeft">30 or 7 before expiry, or -1 when the document expired yesterday.</param>
public sealed record SupplierDocumentExpiring(
    Guid SupplierId, Guid OrganizationId, SupplierDocumentType DocumentType, DateOnly ExpiresOn, int DaysLeft) : IDomainEvent;
