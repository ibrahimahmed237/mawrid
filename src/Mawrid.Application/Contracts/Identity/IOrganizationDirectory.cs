namespace Mawrid.Application.Contracts.Identity;

/// <summary>Published by the Identity module: organization settings, cost centers and approvers.</summary>
public interface IOrganizationDirectory
{
    Task<ProcurementSettingsInfo> GetSettingsAsync(CancellationToken cancellationToken);

    Task<bool> IsActiveCostCenterAsync(Guid costCenterId, CancellationToken cancellationToken);

    /// <summary>Active users with the Approver role for this cost center or for all cost centers.</summary>
    Task<IReadOnlyCollection<Guid>> CostCenterApproversAsync(Guid costCenterId, CancellationToken cancellationToken);

    /// <summary>The ids among <paramref name="userIds"/> that are active employees (not supplier users) of this organization.</summary>
    Task<IReadOnlyCollection<Guid>> ActiveInternalUsersAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

/// <summary>A copy of the organization's procurement settings (see Domain.Identity.ProcurementSettings).</summary>
public sealed record ProcurementSettingsInfo(
    decimal DirectPurchaseLimit,
    int MinimumSuppliersPerRfq,
    int MinimumRfqWindowDays,
    decimal PriceTolerancePercent,
    decimal PriceToleranceMaxAmount,
    decimal OverDeliveryPercent);
