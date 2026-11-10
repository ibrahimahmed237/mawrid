using Mawrid.Domain.Requisitions;

namespace Mawrid.Application.Requisitions;

public sealed record RequisitionLineDto(string Description, decimal Quantity, string Unit, decimal EstimatedUnitPrice, Guid? PreferredSupplierId = null);

public sealed record RequisitionDto(
    Guid Id,
    string Number,
    string Title,
    string Justification,
    Guid RequesterId,
    Guid CostCenterId,
    DateOnly NeededBy,
    string Status,
    decimal EstimatedNetTotal,
    decimal EstimatedTotalWithVat,
    Guid? ApprovalRequestId,
    string? RejectionReason,
    string? SourcingRoute,
    IReadOnlyList<RequisitionLineDto> Lines);

public sealed record RequisitionSummaryDto(Guid Id, string Number, string Title, string Status, decimal EstimatedNetTotal, DateOnly NeededBy, Guid RequesterId);

internal static class RequisitionMapping
{
    public static RequisitionDto ToDto(PurchaseRequisition pr) => new(
        pr.Id, pr.Number, pr.Title, pr.Justification, pr.RequesterId, pr.CostCenterId, pr.NeededBy, pr.Status.ToString(),
        pr.EstimatedNetTotal, pr.EstimatedTotalWithVat, pr.ApprovalRequestId, pr.RejectionReason, pr.SourcingRoute?.ToString(),
        pr.Lines.OrderBy(l => l.LineNumber)
            .Select(l => new RequisitionLineDto(l.Description, l.Quantity, l.Unit, l.EstimatedUnitPrice, l.PreferredSupplierId))
            .ToList());

    public static RequisitionDetails ToDetails(string title, string justification, DateOnly neededBy, IReadOnlyList<RequisitionLineDto> lines) =>
        new(title, justification, neededBy,
            lines.Select(l => new RequisitionLineInput(l.Description, l.Quantity, l.Unit, l.EstimatedUnitPrice, l.PreferredSupplierId)).ToList());
}
