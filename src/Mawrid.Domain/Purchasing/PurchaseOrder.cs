using Mawrid.Domain.Common;

namespace Mawrid.Domain.Purchasing;

/// <summary>
/// A binding order to one supplier. Life cycle:
/// Draft → PendingApproval → Approved → Sent → Acknowledged → PartiallyReceived → Received → Closed, or Cancelled.
/// See DESIGN section 6.6.
/// </summary>
public sealed class PurchaseOrder : AggregateRoot<Guid>, ITenantOwned
{
    public const int ReasonMaxLength = 500;
    public const int DeliveryAddressMaxLength = 300;
    public const int MaxPaymentTermsDays = 180;

    private readonly List<PurchaseOrderLine> _lines = [];
    private readonly List<GoodsReceipt> _receipts = [];

    private PurchaseOrder(Guid id, Guid organizationId, string number, Guid supplierId, Guid costCenterId, Guid buyerId,
        PurchaseOrderSource source, Guid sourceId, List<Guid> requisitionIds) : base(id)
    {
        OrganizationId = organizationId;
        Number = number;
        SupplierId = supplierId;
        CostCenterId = costCenterId;
        BuyerId = buyerId;
        Source = source;
        SourceId = sourceId;
        RequisitionIds = requisitionIds;
        PaymentTermsDays = 30;
        Status = PurchaseOrderStatus.Draft;
    }

    private PurchaseOrder()
    {
        Number = null!;
        RequisitionIds = [];
    }

    public Guid OrganizationId { get; private set; }

    public string Number { get; private set; }

    public Guid SupplierId { get; private set; }

    public Guid CostCenterId { get; private set; }

    public Guid BuyerId { get; private set; }

    public PurchaseOrderSource Source { get; private set; }

    /// <summary>The RFQ (for an award) or the requisition (for a direct purchase).</summary>
    public Guid SourceId { get; private set; }

    public List<Guid> RequisitionIds { get; private set; }

    public int PaymentTermsDays { get; private set; }

    /// <summary>Where the supplier delivers. Empty means the organization's main address.</summary>
    public string? DeliveryAddress { get; private set; }

    public PurchaseOrderStatus Status { get; private set; }

    public Guid? ApprovalRequestId { get; private set; }

    /// <summary>Why the last approval was rejected. The buyer can fix the PO and submit again.</summary>
    public string? RejectionReason { get; private set; }

    public string? CancellationReason { get; private set; }

    /// <summary>Set when a PO is closed by hand before everything was delivered ("short close").</summary>
    public string? ClosedReason { get; private set; }

    public DateTimeOffset? SentAtUtc { get; private set; }

    public DateTimeOffset? AcknowledgedAtUtc { get; private set; }

    public IReadOnlyList<PurchaseOrderLine> Lines => _lines;

    public IReadOnlyList<GoodsReceipt> Receipts => _receipts;

    public decimal NetTotal => _lines.Sum(l => l.NetAmount);

    public decimal VatTotal => _lines.Sum(l => l.VatAmount);

    public decimal Total => NetTotal + VatTotal;

    public static Result<PurchaseOrder> CreateDraft(
        Guid organizationId, string number, Guid supplierId, Guid costCenterId, Guid buyerId, PurchaseOrderSource source, Guid sourceId,
        IReadOnlyList<Guid> requisitionIds, IReadOnlyList<PurchaseOrderLineInput> lines, DateOnly today)
    {
        if (lines.Count == 0 || lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Description)))
        {
            return PurchasingErrors.LinesInvalid;
        }

        var order = new PurchaseOrder(Guid.CreateVersion7(), organizationId, number, supplierId, costCenterId, buyerId, source, sourceId,
            requisitionIds.Distinct().ToList());
        order._lines.AddRange(lines.Select((l, i) => PurchaseOrderLine.Create(i + 1, l, today)));
        order.Raise(new PurchaseOrderCreated(order.Id, organizationId, supplierId));
        return order;
    }

    /// <summary>Changes payment terms and delivery address while the PO is a draft.</summary>
    public Result UpdateTerms(int paymentTermsDays, string? deliveryAddress)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            return PurchasingErrors.NotDraft;
        }

        deliveryAddress = string.IsNullOrWhiteSpace(deliveryAddress) ? null : deliveryAddress.Trim();
        if (paymentTermsDays is < 0 or > MaxPaymentTermsDays || deliveryAddress?.Length > DeliveryAddressMaxLength)
        {
            return PurchasingErrors.TermsInvalid;
        }

        PaymentTermsDays = paymentTermsDays;
        DeliveryAddress = deliveryAddress;
        return Result.Success();
    }

    public Result Submit(Guid approvalRequestId)
    {
        if (Status != PurchaseOrderStatus.Draft)
        {
            return PurchasingErrors.InvalidStatusChange(Status, PurchaseOrderStatus.PendingApproval);
        }

        Status = PurchaseOrderStatus.PendingApproval;
        ApprovalRequestId = approvalRequestId;
        RejectionReason = null;
        Raise(new PurchaseOrderSubmitted(Id, OrganizationId, approvalRequestId));
        return Result.Success();
    }

    public Result MarkApproved()
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
        {
            return PurchasingErrors.InvalidStatusChange(Status, PurchaseOrderStatus.Approved);
        }

        Status = PurchaseOrderStatus.Approved;
        Raise(new PurchaseOrderApproved(Id, OrganizationId, SupplierId, Total));
        return Result.Success();
    }

    /// <summary>A rejected PO goes back to Draft with the reason, so the buyer can fix it or cancel it.</summary>
    public Result MarkRejected(string reason)
    {
        if (Status != PurchaseOrderStatus.PendingApproval)
        {
            return PurchasingErrors.InvalidStatusChange(Status, PurchaseOrderStatus.Draft);
        }

        Status = PurchaseOrderStatus.Draft;
        RejectionReason = reason;
        ApprovalRequestId = null;
        Raise(new PurchaseOrderRejected(Id, OrganizationId));
        return Result.Success();
    }

    /// <summary>Sends the approved PO to the supplier. From now on it is binding.</summary>
    public Result Send(DateTimeOffset now)
    {
        if (Status != PurchaseOrderStatus.Approved)
        {
            return PurchasingErrors.InvalidStatusChange(Status, PurchaseOrderStatus.Sent);
        }

        Status = PurchaseOrderStatus.Sent;
        SentAtUtc = now;
        Raise(new PurchaseOrderSent(Id, OrganizationId, SupplierId));
        return Result.Success();
    }

    /// <summary>The supplier confirms it received the order.</summary>
    public Result Acknowledge(DateTimeOffset now)
    {
        if (Status != PurchaseOrderStatus.Sent)
        {
            return PurchasingErrors.InvalidStatusChange(Status, PurchaseOrderStatus.Acknowledged);
        }

        Status = PurchaseOrderStatus.Acknowledged;
        AcknowledgedAtUtc = now;
        Raise(new PurchaseOrderAcknowledged(Id, OrganizationId));
        return Result.Success();
    }

    /// <summary>
    /// Records a delivery. Accepted quantities count as received; rejected ones (damaged, wrong item) do not.
    /// The total accepted per line can never exceed the ordered quantity plus the over-delivery tolerance.
    /// </summary>
    public Result<Guid> RecordReceipt(Guid receivedBy, IReadOnlyList<ReceiptLineInput> lines, decimal overDeliveryPercent, string? note, DateTimeOffset now)
    {
        if (Status is not (PurchaseOrderStatus.Sent or PurchaseOrderStatus.Acknowledged or PurchaseOrderStatus.PartiallyReceived))
        {
            return PurchasingErrors.NotReceivable;
        }

        // Segregation of duties: the buyer who placed the order does not confirm its delivery.
        if (receivedBy == BuyerId)
        {
            return PurchasingErrors.ReceiverIsBuyer;
        }

        if (lines.Count == 0 || lines.Select(l => l.LineNumber).Distinct().Count() != lines.Count)
        {
            return PurchasingErrors.ReceiptInvalid;
        }

        foreach (var input in lines)
        {
            var line = _lines.SingleOrDefault(l => l.LineNumber == input.LineNumber);
            if (line is null || input.AcceptedQuantity < 0 || input.RejectedQuantity < 0 || input.AcceptedQuantity + input.RejectedQuantity == 0)
            {
                return PurchasingErrors.ReceiptInvalid;
            }

            var hasReason = !string.IsNullOrWhiteSpace(input.RejectionReason);
            if ((input.RejectedQuantity > 0) != hasReason)
            {
                return PurchasingErrors.RejectionReasonRequired;
            }

            var maxAccepted = line.Quantity * (1 + overDeliveryPercent / 100m);
            if (line.AcceptedQuantity + input.AcceptedQuantity > maxAccepted)
            {
                return PurchasingErrors.OverDelivery(line.LineNumber);
            }
        }

        var receipt = new GoodsReceipt(Guid.CreateVersion7(), receivedBy, now, note?.Trim(),
            lines.Select(l => new GoodsReceiptLine(l.LineNumber, l.AcceptedQuantity, l.RejectedQuantity, l.RejectionReason?.Trim())).ToList());
        _receipts.Add(receipt);

        foreach (var input in lines)
        {
            _lines.Single(l => l.LineNumber == input.LineNumber).AddReceived(input.AcceptedQuantity, input.RejectedQuantity);
        }

        Status = _lines.All(l => l.AcceptedQuantity >= l.Quantity) ? PurchaseOrderStatus.Received : PurchaseOrderStatus.PartiallyReceived;
        Raise(new GoodsReceived(Id, OrganizationId, receipt.Id, Status == PurchaseOrderStatus.Received));
        return receipt.Id;
    }

    /// <summary>Called by Payables when an invoice line is matched against this PO.</summary>
    public Result RecordInvoiced(int lineNumber, decimal quantity)
    {
        var line = _lines.SingleOrDefault(l => l.LineNumber == lineNumber);
        if (line is null || quantity <= 0)
        {
            return PurchasingErrors.LinesInvalid;
        }

        line.AddInvoiced(quantity);
        return Result.Success();
    }

    public Result Cancel(string reason)
    {
        if (_receipts.Count > 0 || Status is PurchaseOrderStatus.Closed or PurchaseOrderStatus.Cancelled)
        {
            return PurchasingErrors.CannotCancel;
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > ReasonMaxLength)
        {
            return PurchasingErrors.ReasonRequired;
        }

        Status = PurchaseOrderStatus.Cancelled;
        CancellationReason = reason;
        Raise(new PurchaseOrderCancelled(Id, OrganizationId, ApprovalRequestId));
        return Result.Success();
    }

    /// <summary>
    /// Closes the PO. A PO that is fully received and fully invoiced closes without a reason.
    /// A partly received PO can be "short closed" with a reason: the rest will not be delivered.
    /// </summary>
    public Result Close(string? reason = null)
    {
        reason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
        var complete = Status == PurchaseOrderStatus.Received && _lines.All(l => l.InvoicedQuantity >= l.AcceptedQuantity);

        if (!complete)
        {
            if (Status != PurchaseOrderStatus.PartiallyReceived)
            {
                return PurchasingErrors.CannotClose;
            }

            if (reason is null || reason.Length is < 3 or > ReasonMaxLength)
            {
                return PurchasingErrors.ShortCloseReasonRequired;
            }
        }
        else if (reason?.Length > ReasonMaxLength)
        {
            return PurchasingErrors.ReasonRequired;
        }

        Status = PurchaseOrderStatus.Closed;
        ClosedReason = reason;
        Raise(new PurchaseOrderClosed(Id, OrganizationId));
        return Result.Success();
    }
}

public sealed record PurchaseOrderLineInput(string Description, decimal Quantity, string Unit, decimal UnitPrice, VatCategory VatCategory);

public sealed record ReceiptLineInput(int LineNumber, decimal AcceptedQuantity, decimal RejectedQuantity = 0, string? RejectionReason = null);

public sealed class PurchaseOrderLine
{
    private PurchaseOrderLine(int lineNumber, string description, decimal quantity, string unit, decimal unitPrice,
        VatCategory vatCategory, decimal vatRate)
    {
        LineNumber = lineNumber;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        UnitPrice = unitPrice;
        VatCategory = vatCategory;
        VatRate = vatRate;
        NetAmount = Money.Net(quantity, unitPrice);
        VatAmount = Money.Vat(NetAmount, vatRate);
    }

    private PurchaseOrderLine()
    {
        Description = null!;
        Unit = null!;
    }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public string Unit { get; private set; }

    /// <summary>SAR per unit, without VAT.</summary>
    public decimal UnitPrice { get; private set; }

    public VatCategory VatCategory { get; private set; }

    /// <summary>The rate on the day the PO was created, so the PO never changes if rates change later.</summary>
    public decimal VatRate { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal VatAmount { get; private set; }

    public decimal AcceptedQuantity { get; private set; }

    public decimal RejectedQuantity { get; private set; }

    public decimal InvoicedQuantity { get; private set; }

    internal static PurchaseOrderLine Create(int lineNumber, PurchaseOrderLineInput input, DateOnly today) =>
        new(lineNumber, input.Description.Trim(), input.Quantity, input.Unit.Trim(), input.UnitPrice, input.VatCategory,
            VatRates.RateFor(input.VatCategory, today));

    internal void AddReceived(decimal accepted, decimal rejected)
    {
        AcceptedQuantity += accepted;
        RejectedQuantity += rejected;
    }

    internal void AddInvoiced(decimal quantity) => InvoicedQuantity += quantity;
}

/// <summary>One delivery recorded by a receiver (the GRN).</summary>
public sealed class GoodsReceipt
{
    internal GoodsReceipt(Guid id, Guid receivedBy, DateTimeOffset receivedAtUtc, string? note, List<GoodsReceiptLine> lines)
    {
        Id = id;
        ReceivedBy = receivedBy;
        ReceivedAtUtc = receivedAtUtc;
        Note = note;
        Lines = lines;
    }

    private GoodsReceipt()
    {
        Lines = [];
    }

    public Guid Id { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public string? Note { get; private set; }

    public List<GoodsReceiptLine> Lines { get; private set; }
}

public sealed record GoodsReceiptLine(int LineNumber, decimal AcceptedQuantity, decimal RejectedQuantity, string? RejectionReason);

public enum PurchaseOrderSource
{
    Rfq = 1,
    DirectPurchase = 2,
}

public enum PurchaseOrderStatus
{
    Draft = 1,
    PendingApproval = 2,
    Approved = 3,
    Sent = 4,
    Acknowledged = 5,
    PartiallyReceived = 6,
    Received = 7,
    Closed = 8,
    Cancelled = 9,
}
