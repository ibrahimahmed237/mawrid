using Mawrid.Domain.Common;

namespace Mawrid.Application.Contracts.Approvals;

/// <summary>Published by the Approvals module: start or stop the approval of a document.</summary>
public interface IApprovalService
{
    /// <summary>
    /// Starts an approval from the policy for the document type. The new request is added to the
    /// unit of work; the caller's <c>SaveChanges</c> saves it together with the document.
    /// </summary>
    Task<Result<Guid>> StartAsync(ApprovalStart start, CancellationToken cancellationToken);

    /// <summary>Cancels a pending approval (the document was withdrawn). Saved by the caller.</summary>
    Task CancelAsync(Guid approvalRequestId, CancellationToken cancellationToken);
}

/// <param name="Amount">Total including VAT; it picks the band of the approval matrix.</param>
public sealed record ApprovalStart(
    string DocumentType, Guid DocumentId, string DocumentTitle, decimal Amount, Guid CostCenterId, Guid RequestedBy);

/// <summary>
/// Implemented by each module that owns an approvable document. The Approvals module calls it,
/// in the same transaction, when the last step approves or any step rejects.
/// </summary>
public interface IApprovalDocumentHandler
{
    string DocumentType { get; }

    Task<Result> OnApprovedAsync(Guid documentId, CancellationToken cancellationToken);

    Task<Result> OnRejectedAsync(Guid documentId, string reason, CancellationToken cancellationToken);
}

public static class ApprovalDocumentTypes
{
    public const string PurchaseRequisition = nameof(PurchaseRequisition);
    public const string PurchaseOrder = nameof(PurchaseOrder);

    public static IReadOnlyList<string> All { get; } = [PurchaseRequisition, PurchaseOrder];
}
