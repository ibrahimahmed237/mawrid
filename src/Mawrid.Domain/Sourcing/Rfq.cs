using Mawrid.Domain.Common;

namespace Mawrid.Domain.Sourcing;

/// <summary>
/// A request for quotation to selected suppliers. Quotations are sealed until the deadline (ADR-0007).
/// Life cycle: Draft → Published → Closed → Awarded, or Cancelled. See DESIGN section 6.5.
/// </summary>
public sealed class Rfq : AggregateRoot<Guid>, ITenantOwned
{
    public const int TitleMaxLength = 200;
    public const int DescriptionMaxLength = 4000;
    public const int ReasonMaxLength = 500;
    public const int MaxItems = 100;

    private readonly List<RfqItem> _items = [];
    private readonly List<RfqInvitation> _invitations = [];
    private readonly List<RfqAward> _awards = [];

    private Rfq(Guid id, Guid organizationId, string number, Guid buyerId, Guid costCenterId, string title, string? description) : base(id)
    {
        OrganizationId = organizationId;
        Number = number;
        BuyerId = buyerId;
        CostCenterId = costCenterId;
        Title = title;
        Description = description;
        Status = RfqStatus.Draft;
    }

    private Rfq()
    {
        Number = null!;
        Title = null!;
    }

    public Guid OrganizationId { get; private set; }

    public string Number { get; private set; }

    /// <summary>The buyer who owns the RFQ.</summary>
    public Guid BuyerId { get; private set; }

    /// <summary>All requisitions of one RFQ share a cost center; the purchase orders inherit it.</summary>
    public Guid CostCenterId { get; private set; }

    public string Title { get; private set; }

    public string? Description { get; private set; }

    public RfqStatus Status { get; private set; }

    /// <summary>Last moment (UTC) a supplier can submit or revise a quotation.</summary>
    public DateTimeOffset? Deadline { get; private set; }

    /// <summary>Goes up with every amendment after publishing. Older quotations must be re-confirmed.</summary>
    public int Revision { get; private set; }

    public DateTimeOffset? PublishedAtUtc { get; private set; }

    public string? SingleSourceReason { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>When a buyer first opened the sealed quotations. Recorded once, for the audit trail.</summary>
    public DateTimeOffset? QuotationsOpenedAtUtc { get; private set; }

    public Guid? QuotationsOpenedBy { get; private set; }

    public IReadOnlyList<RfqItem> Items => _items;

    public IReadOnlyList<RfqInvitation> Invitations => _invitations;

    public IReadOnlyList<RfqAward> Awards => _awards;

    public static Result<Rfq> CreateDraft(
        Guid organizationId, string number, Guid buyerId, Guid costCenterId, string title, string? description, IReadOnlyList<RfqItemInput> items)
    {
        title = title?.Trim() ?? string.Empty;
        description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (title.Length is < 3 or > TitleMaxLength || description?.Length > DescriptionMaxLength)
        {
            return SourcingErrors.TitleInvalid;
        }

        if (items.Count is 0 or > MaxItems || items.Any(i => i.Quantity <= 0 || string.IsNullOrWhiteSpace(i.Description)))
        {
            return SourcingErrors.ItemsInvalid;
        }

        var rfq = new Rfq(Guid.CreateVersion7(), organizationId, number, buyerId, costCenterId, title, description);
        rfq._items.AddRange(items.Select((item, index) => new RfqItem(index + 1, item.Description.Trim(), item.Quantity, item.Unit.Trim(),
            item.RequisitionId, item.RequisitionLineNumber)));
        rfq.Raise(new RfqCreated(rfq.Id, organizationId));
        return rfq;
    }

    /// <summary>Open means suppliers can still submit: published and before the deadline.</summary>
    public bool IsOpen(DateTimeOffset now) => Status == RfqStatus.Published && now < Deadline;

    /// <summary>True when quotations may be revealed: the deadline passed or the RFQ was closed or awarded.</summary>
    public bool IsSealed(DateTimeOffset now) => !(Status is RfqStatus.Closed or RfqStatus.Awarded || (Status == RfqStatus.Published && now >= Deadline));

    public bool IsInvited(Guid supplierId) => _invitations.Any(i => i.SupplierId == supplierId);

    public Result Invite(IReadOnlyCollection<Guid> supplierIds, DateTimeOffset now)
    {
        if (Status != RfqStatus.Draft)
        {
            return SourcingErrors.NotDraft;
        }

        foreach (var supplierId in supplierIds.Where(id => !IsInvited(id)))
        {
            _invitations.Add(new RfqInvitation(supplierId, now));
        }

        return Result.Success();
    }

    public Result RemoveInvitation(Guid supplierId)
    {
        if (Status != RfqStatus.Draft)
        {
            return SourcingErrors.NotDraft;
        }

        _invitations.RemoveAll(i => i.SupplierId == supplierId);
        return Result.Success();
    }

    public Result SetDeadline(DateTimeOffset deadline)
    {
        if (Status != RfqStatus.Draft)
        {
            return SourcingErrors.NotDraft;
        }

        Deadline = deadline;
        return Result.Success();
    }

    /// <summary>
    /// Sends the RFQ to the invited suppliers. The deadline must leave at least the minimum window.
    /// Inviting fewer suppliers than the minimum needs a single-source reason.
    /// The caller checks that every invited supplier is awardable.
    /// </summary>
    public Result Publish(DateTimeOffset now, int minimumWindowDays, int minimumSuppliers, string? singleSourceReason)
    {
        if (Status != RfqStatus.Draft)
        {
            return SourcingErrors.NotDraft;
        }

        if (_invitations.Count == 0)
        {
            return SourcingErrors.NoInvitations;
        }

        if (Deadline is null || Deadline < now.AddDays(minimumWindowDays))
        {
            return SourcingErrors.DeadlineTooSoon(minimumWindowDays);
        }

        singleSourceReason = singleSourceReason?.Trim();
        if (_invitations.Count < minimumSuppliers)
        {
            if (singleSourceReason is null || singleSourceReason.Length is < 10 or > ReasonMaxLength)
            {
                return SourcingErrors.SingleSourceReasonRequired(minimumSuppliers);
            }

            SingleSourceReason = singleSourceReason;
        }

        Status = RfqStatus.Published;
        PublishedAtUtc = now;
        Raise(new RfqPublished(Id, OrganizationId, _invitations.Select(i => i.SupplierId).ToList(), Deadline.Value));
        return Result.Success();
    }

    /// <summary>
    /// Changes a published RFQ: new quantities and/or a later deadline. Suppliers who already
    /// submitted must re-confirm, so the deadline must again leave the minimum window.
    /// </summary>
    public Result Amend(DateTimeOffset now, DateTimeOffset newDeadline, IReadOnlyDictionary<int, decimal> newQuantities, int minimumWindowDays)
    {
        if (!IsOpen(now))
        {
            return SourcingErrors.NotOpen;
        }

        if (newDeadline < now.AddDays(minimumWindowDays) || newDeadline < Deadline)
        {
            return SourcingErrors.DeadlineTooSoon(minimumWindowDays);
        }

        foreach (var (lineNumber, quantity) in newQuantities)
        {
            var item = _items.SingleOrDefault(i => i.LineNumber == lineNumber);
            if (item is null || quantity <= 0)
            {
                return SourcingErrors.ItemsInvalid;
            }

            item.ChangeQuantity(quantity);
        }

        Deadline = newDeadline;
        Revision++;
        Raise(new RfqAmended(Id, OrganizationId, Revision, newDeadline));
        return Result.Success();
    }

    /// <summary>Closes submissions once the deadline has passed. Safe to call more than once.</summary>
    public Result Close(DateTimeOffset now)
    {
        if (Status == RfqStatus.Closed)
        {
            return Result.Success();
        }

        if (Status != RfqStatus.Published || now < Deadline)
        {
            return SourcingErrors.DeadlineNotPassed;
        }

        Status = RfqStatus.Closed;
        Raise(new RfqClosed(Id, OrganizationId));
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (Status is not (RfqStatus.Draft or RfqStatus.Published))
        {
            return SourcingErrors.InvalidStatusChange(Status, RfqStatus.Cancelled);
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > ReasonMaxLength)
        {
            return SourcingErrors.ReasonRequired;
        }

        Status = RfqStatus.Cancelled;
        CancellationReason = reason;
        Raise(new RfqCancelled(Id, OrganizationId, _invitations.Select(i => i.SupplierId).ToList()));
        return Result.Success();
    }

    /// <summary>Records the first time a buyer opened the sealed quotations.</summary>
    public void RecordQuotationsOpened(Guid userId, DateTimeOffset now)
    {
        if (QuotationsOpenedAtUtc is not null)
        {
            return;
        }

        QuotationsOpenedAtUtc = now;
        QuotationsOpenedBy = userId;
    }

    /// <summary>
    /// Awards RFQ lines to quotation lines. Each line can be awarded once; lines left out are not
    /// awarded. Choosing a quote that is not the cheapest for its line needs a justification.
    /// </summary>
    public Result Award(IReadOnlyList<AwardDecision> decisions, DateTimeOffset now)
    {
        var close = Close(now);
        if (close.IsFailure)
        {
            return Status == RfqStatus.Awarded ? SourcingErrors.AlreadyAwarded : close;
        }

        if (decisions.Count == 0 || decisions.Select(d => d.ItemLineNumber).Distinct().Count() != decisions.Count)
        {
            return SourcingErrors.AwardInvalid;
        }

        foreach (var decision in decisions)
        {
            if (_items.All(i => i.LineNumber != decision.ItemLineNumber) || !IsInvited(decision.SupplierId))
            {
                return SourcingErrors.AwardInvalid;
            }

            var justification = decision.Justification?.Trim();
            if (!decision.IsLowestPrice && (justification is null || justification.Length < 10))
            {
                return SourcingErrors.JustificationRequired(decision.ItemLineNumber);
            }
        }

        _awards.AddRange(decisions.Select(d => new RfqAward(d.ItemLineNumber, d.SupplierId, d.QuotationId, d.UnitPrice, d.VatCategory,
            d.IsLowestPrice, d.Justification?.Trim())));
        Status = RfqStatus.Awarded;
        Raise(new RfqAwarded(Id, OrganizationId,
            _invitations.Select(i => i.SupplierId).ToList(),
            decisions.Select(d => d.SupplierId).Distinct().ToList()));
        return Result.Success();
    }
}

public sealed record RfqItemInput(string Description, decimal Quantity, string Unit, Guid? RequisitionId = null, int? RequisitionLineNumber = null);

/// <param name="IsLowestPrice">Computed by the caller from the sealed quotations.</param>
public sealed record AwardDecision(
    int ItemLineNumber, Guid SupplierId, Guid QuotationId, decimal UnitPrice, VatCategory VatCategory, bool IsLowestPrice, string? Justification);

public sealed class RfqItem
{
    internal RfqItem(int lineNumber, string description, decimal quantity, string unit, Guid? requisitionId, int? requisitionLineNumber)
    {
        LineNumber = lineNumber;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        RequisitionId = requisitionId;
        RequisitionLineNumber = requisitionLineNumber;
    }

    private RfqItem()
    {
        Description = null!;
        Unit = null!;
    }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; }

    /// <summary>The requisition line this item came from.</summary>
    public Guid? RequisitionId { get; private set; }

    public int? RequisitionLineNumber { get; private set; }

    internal void ChangeQuantity(decimal quantity) => Quantity = quantity;
}

public sealed class RfqInvitation
{
    internal RfqInvitation(Guid supplierId, DateTimeOffset invitedAtUtc)
    {
        Id = Guid.CreateVersion7();
        SupplierId = supplierId;
        InvitedAtUtc = invitedAtUtc;
    }

    private RfqInvitation()
    {
    }

    /// <summary>
    /// Own key, so EF Core sees a new invitation as new. (With a composite key that includes the
    /// owner's id, EF Core treats an item added later as an existing row and updates it.)
    /// </summary>
    public Guid Id { get; private set; }

    public Guid SupplierId { get; private set; }

    public DateTimeOffset InvitedAtUtc { get; private set; }
}

public sealed class RfqAward
{
    internal RfqAward(int itemLineNumber, Guid supplierId, Guid quotationId, decimal unitPrice, VatCategory vatCategory, bool isLowestPrice, string? justification)
    {
        Id = Guid.CreateVersion7();
        ItemLineNumber = itemLineNumber;
        SupplierId = supplierId;
        QuotationId = quotationId;
        UnitPrice = unitPrice;
        VatCategory = vatCategory;
        IsLowestPrice = isLowestPrice;
        Justification = justification;
    }

    private RfqAward()
    {
    }

    public Guid Id { get; private set; }

    public int ItemLineNumber { get; private set; }

    public Guid SupplierId { get; private set; }

    public Guid QuotationId { get; private set; }

    public decimal UnitPrice { get; private set; }

    public VatCategory VatCategory { get; private set; }

    public bool IsLowestPrice { get; private set; }

    public string? Justification { get; private set; }
}

public enum RfqStatus
{
    Draft = 1,
    Published = 2,
    Closed = 3,
    Awarded = 4,
    Cancelled = 5,
}
