using Mawrid.Domain.Common;

namespace Mawrid.Application.Contracts.Requisitions;

/// <summary>Published by the Requisitions module: approved requisitions that procurement turns into RFQs or POs.</summary>
public interface IRequisitionSourcing
{
    /// <summary>Lines of requisitions that are in sourcing by RFQ. All must share one cost center.</summary>
    Task<Result<RequisitionsForSourcing>> GetForRfqAsync(IReadOnlyCollection<Guid> requisitionIds, CancellationToken cancellationToken);

    /// <summary>Lines of one requisition that is in sourcing by direct purchase.</summary>
    Task<Result<RequisitionsForSourcing>> GetForDirectPurchaseAsync(Guid requisitionId, CancellationToken cancellationToken);

    /// <summary>Marks requisitions as ordered (saved by the caller, in the same transaction as the PO).</summary>
    Task MarkOrderedAsync(IReadOnlyCollection<Guid> requisitionIds, CancellationToken cancellationToken);
}

public sealed record RequisitionsForSourcing(Guid CostCenterId, IReadOnlyList<RequisitionLineForSourcing> Lines);

public sealed record RequisitionLineForSourcing(
    Guid RequisitionId, int LineNumber, string Description, decimal Quantity, string Unit, decimal EstimatedUnitPrice, Guid? PreferredSupplierId);
