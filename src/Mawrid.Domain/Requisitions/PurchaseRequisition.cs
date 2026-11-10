using Mawrid.Domain.Common;

namespace Mawrid.Domain.Requisitions;

/// <summary>
/// An employee's internal request to buy something, charged to a cost center.
/// Life cycle: Draft → PendingApproval → Approved → InSourcing → Ordered, or Rejected / Cancelled.
/// See DESIGN section 6.4.
/// </summary>
public sealed class PurchaseRequisition : AggregateRoot<Guid>, ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int JustificationMaxLength = 2000;
    public const int MaxLines = 100;

    /// <summary>Standard KSA VAT, used only to estimate the approval amount of a requisition.</summary>
    public const decimal EstimatedVatRate = 0.15m;

    private readonly List<RequisitionLine> _lines = [];

    private PurchaseRequisition(Guid id, Guid organizationId, string number, Guid requesterId, Guid costCenterId) : base(id)
    {
        OrganizationId = organizationId;
        Number = number;
        RequesterId = requesterId;
        CostCenterId = costCenterId;
        Status = RequisitionStatus.Draft;
        Title = null!;
        Justification = null!;
    }

    private PurchaseRequisition()
    {
        Number = null!;
        Title = null!;
        Justification = null!;
    }

    public Guid OrganizationId { get; private set; }

    /// <summary>Human-friendly number, such as PR-2026-000012. Unique per organization.</summary>
    public string Number { get; private set; }

    public Guid RequesterId { get; private set; }

    public Guid CostCenterId { get; private set; }

    public string Title { get; private set; }

    public string Justification { get; private set; }

    public DateOnly NeededBy { get; private set; }

    public RequisitionStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    public string? RejectionReason { get; private set; }

    public SourcingRoute? SourcingRoute { get; private set; }

    /// <summary>Required when an RFQ invites fewer suppliers than the organization's minimum.</summary>
    public string? SingleSourceReason { get; private set; }

    public IReadOnlyList<RequisitionLine> Lines => _lines;

    /// <summary>Sum of quantity × estimated unit price, without VAT.</summary>
    public decimal EstimatedNetTotal => _lines.Sum(l => l.EstimatedNetAmount);

    /// <summary>The amount used to pick the approval rule: the net total plus standard VAT.</summary>
    public decimal EstimatedTotalWithVat => Math.Round(EstimatedNetTotal * (1 + EstimatedVatRate), 2, MidpointRounding.AwayFromZero);

    public static Result<PurchaseRequisition> CreateDraft(
        Guid organizationId, string number, Guid requesterId, Guid costCenterId, RequisitionDetails details, DateOnly today)
    {
        var requisition = new PurchaseRequisition(Guid.CreateVersion7(), organizationId, number, requesterId, costCenterId);
        var result = requisition.ApplyDetails(details, today);
        if (result.IsFailure)
        {
            return result.Error!;
        }

        requisition.Raise(new RequisitionCreated(requisition.Id, organizationId));
        return requisition;
    }

    public Result UpdateDraft(Guid costCenterId, RequisitionDetails details, DateOnly today)
    {
        if (Status != RequisitionStatus.Draft)
        {
            return RequisitionErrors.NotDraft;
        }

        CostCenterId = costCenterId;
        return ApplyDetails(details, today);
    }

    /// <summary>Sends the draft for approval. The caller starts the approval request and passes its id.</summary>
    public Result Submit(Guid approvalRequestId)
    {
        if (Status != RequisitionStatus.Draft)
        {
            return RequisitionErrors.NotDraft;
        }

        if (_lines.Count == 0)
        {
            return RequisitionErrors.LinesRequired;
        }

        Status = RequisitionStatus.PendingApproval;
        ApprovalRequestId = approvalRequestId;
        Raise(new RequisitionSubmitted(Id, OrganizationId, approvalRequestId));
        return Result.Success();
    }

    public Result MarkApproved()
    {
        if (Status != RequisitionStatus.PendingApproval)
        {
            return RequisitionErrors.InvalidStatusChange(Status, RequisitionStatus.Approved);
        }

        Status = RequisitionStatus.Approved;
        Raise(new RequisitionApproved(Id, OrganizationId));
        return Result.Success();
    }

    public Result MarkRejected(string reason)
    {
        if (Status != RequisitionStatus.PendingApproval)
        {
            return RequisitionErrors.InvalidStatusChange(Status, RequisitionStatus.Rejected);
        }

        Status = RequisitionStatus.Rejected;
        RejectionReason = reason;
        Raise(new RequisitionRejected(Id, OrganizationId));
        return Result.Success();
    }

    /// <summary>The requester withdraws the requisition before it is sourced.</summary>
    public Result Cancel()
    {
        if (Status is not (RequisitionStatus.Draft or RequisitionStatus.PendingApproval or RequisitionStatus.Approved))
        {
            return RequisitionErrors.InvalidStatusChange(Status, RequisitionStatus.Cancelled);
        }

        Status = RequisitionStatus.Cancelled;
        Raise(new RequisitionCancelled(Id, OrganizationId, ApprovalRequestId));
        return Result.Success();
    }

    /// <summary>
    /// Procurement decides how to buy an approved requisition.
    /// A direct PO is allowed only up to the organization's direct purchase limit.
    /// </summary>
    public Result ChooseSourcingRoute(SourcingRoute route, decimal directPurchaseLimit)
    {
        if (Status != RequisitionStatus.Approved)
        {
            return RequisitionErrors.InvalidStatusChange(Status, RequisitionStatus.InSourcing);
        }

        if (route == Requisitions.SourcingRoute.DirectPurchase && EstimatedNetTotal > directPurchaseLimit)
        {
            return RequisitionErrors.AboveDirectPurchaseLimit(directPurchaseLimit);
        }

        SourcingRoute = route;
        Status = RequisitionStatus.InSourcing;
        Raise(new RequisitionSourcingStarted(Id, OrganizationId, route));
        return Result.Success();
    }

    /// <summary>Records why fewer suppliers than the minimum were invited (set by the Sourcing module).</summary>
    public void RecordSingleSourceReason(string reason) => SingleSourceReason = reason;

    /// <summary>A purchase order now covers this requisition.</summary>
    public Result MarkOrdered()
    {
        if (Status != RequisitionStatus.InSourcing)
        {
            return RequisitionErrors.InvalidStatusChange(Status, RequisitionStatus.Ordered);
        }

        Status = RequisitionStatus.Ordered;
        Raise(new RequisitionOrdered(Id, OrganizationId));
        return Result.Success();
    }

    private Result ApplyDetails(RequisitionDetails details, DateOnly today)
    {
        var title = details.Title?.Trim() ?? string.Empty;
        var justification = details.Justification?.Trim() ?? string.Empty;

        if (title.Length is < 3 or > TitleMaxLength)
        {
            return RequisitionErrors.TitleInvalid;
        }

        if (justification.Length is < 10 or > JustificationMaxLength)
        {
            return RequisitionErrors.JustificationInvalid;
        }

        if (details.NeededBy <= today)
        {
            return RequisitionErrors.NeededByInPast;
        }

        if (details.Lines.Count is 0 or > MaxLines)
        {
            return RequisitionErrors.LinesRequired;
        }

        var lines = new List<RequisitionLine>();
        for (var i = 0; i < details.Lines.Count; i++)
        {
            var line = RequisitionLine.Create(i + 1, details.Lines[i]);
            if (line.IsFailure)
            {
                return line.Error!;
            }

            lines.Add(line.Value);
        }

        Title = title;
        Justification = justification;
        NeededBy = details.NeededBy;
        _lines.Clear();
        _lines.AddRange(lines);
        return Result.Success();
    }
}

/// <summary>The editable part of a requisition.</summary>
public sealed record RequisitionDetails(string Title, string Justification, DateOnly NeededBy, IReadOnlyList<RequisitionLineInput> Lines);

public sealed record RequisitionLineInput(string Description, decimal Quantity, string Unit, decimal EstimatedUnitPrice, Guid? PreferredSupplierId = null);

public enum RequisitionStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Rejected = 4,
    InSourcing = 5,
    Ordered = 6,
    Cancelled = 7,
}

public enum SourcingRoute
{
    /// <summary>Order straight from one supplier (small amounts only).</summary>
    DirectPurchase = 1,

    /// <summary>Ask several suppliers for sealed quotations.</summary>
    Rfq = 2,
}
