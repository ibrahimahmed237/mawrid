using MediatR;

namespace Mawrid.Application.Contracts.Suppliers;

/// <summary>A supplier's new bank account was verified; payments held for it can continue.</summary>
public sealed record SupplierBankAccountVerifiedIntegrationEvent(Guid OrganizationId, Guid SupplierId) : INotification;
