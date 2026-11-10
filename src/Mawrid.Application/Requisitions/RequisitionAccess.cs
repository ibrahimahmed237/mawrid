using Mawrid.Application.Common.Security;
using Mawrid.Domain.Common;
using Mawrid.Domain.Requisitions;

namespace Mawrid.Application.Requisitions;

internal static class RequisitionAccess
{
    /// <summary>Requesters see their own requisitions. Procurement, finance and approvers see all of the organization's.</summary>
    public static IQueryable<PurchaseRequisition> Visible(IQueryable<PurchaseRequisition> requisitions, ICurrentUser user) =>
        user.HasRole(Role.Admin) || user.HasRole(Role.Buyer) || user.HasRole(Role.Approver) || user.HasRole(Role.ApClerk)
            ? requisitions
            : requisitions.Where(r => r.RequesterId == user.UserId);

    /// <summary>A requester may raise requisitions for a cost center they are scoped to, or for any cost center if unscoped.</summary>
    public static bool CanRequestFor(ICurrentUser user, Guid costCenterId) =>
        user.Roles.Any(r => r.Role == Role.Requester && (r.CostCenterId is null || r.CostCenterId == costCenterId));
}
